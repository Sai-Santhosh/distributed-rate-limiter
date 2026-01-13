using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// Orleans grain that coordinates rate limiting across all clients in the cluster.
/// This grain owns the global permit pool and tracks all client state.
/// </summary>
[Reentrant]
internal sealed class DistributedRateLimiterCoordinator : Grain, IDistributedRateLimiterCoordinator, IDisposable
{
    private readonly ILogger<DistributedRateLimiterCoordinator> _logger;
    private readonly DistributedRateLimiterOptions _options;
    private readonly Dictionary<IRateLimiterClient, ClientState> _clients = new();
    private readonly Queue<IRateLimiterClient> _pendingRequests = new();

    private IDisposable? _clientPurgeTimer;
    private int _availablePermits;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedRateLimiterCoordinator"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The rate limiter options.</param>
    public DistributedRateLimiterCoordinator(
        ILogger<DistributedRateLimiterCoordinator> logger,
        IOptions<DistributedRateLimiterOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _availablePermits = _options.GlobalPermitCount;
    }

    /// <inheritdoc />
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Coordinator grain activated with {GlobalPermitCount} global permits",
            _options.GlobalPermitCount);

        // Register a timer to periodically purge idle clients
        _clientPurgeTimer = RegisterTimer(
            static state => ((DistributedRateLimiterCoordinator)state).OnPurgeClientsAsync(),
            this,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Coordinator grain deactivating: {Reason}", reason);
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<int> TryAcquire(IRateLimiterClient client, int sequenceNumber, int permitCount)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (permitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, "Permit count cannot be negative.");
        }

        var state = GetOrCreateClientState(client);
        state.LastSeen.Restart();

        // Handle duplicate requests (idempotency)
        if (state.SequenceNumber >= sequenceNumber)
        {
            _logger.LogDebug(
                "Duplicate acquire request (seq={SequenceNumber}, existing={ExistingSeq}), returning cached result: {PermitCount}",
                sequenceNumber, state.SequenceNumber, state.LastAcquiredPermitCount);

            return new ValueTask<int>(state.LastAcquiredPermitCount);
        }

        // Opportunistically reclaim permits from idle clients
        DropIdleClients();

        int acquiredPermits;
        if (_availablePermits >= permitCount)
        {
            // Grant the full request
            state.InUsePermitCount += permitCount;
            _availablePermits -= permitCount;
            acquiredPermits = permitCount;

            if (state.HasPendingRequest)
            {
                state.HasPendingRequest = false;
            }

            _logger.LogDebug(
                "Granted {PermitCount} permits to client, {AvailablePermits} remaining",
                permitCount, _availablePermits);
        }
        else
        {
            // Enqueue the client for later notification when permits become available
            if (!state.HasPendingRequest)
            {
                state.PermitsToAcquire = permitCount;
                state.HasPendingRequest = true;
                _pendingRequests.Enqueue(client);

                _logger.LogDebug(
                    "Insufficient permits ({Available}/{Requested}), client queued",
                    _availablePermits, permitCount);
            }

            acquiredPermits = 0;
        }

        // Process any pending requests that can now be satisfied
        ServicePendingRequests();

        // Update client state for idempotency
        state.SequenceNumber = sequenceNumber;
        state.LastAcquiredPermitCount = acquiredPermits;

        return new ValueTask<int>(acquiredPermits);
    }

    /// <inheritdoc />
    public ValueTask Release(IRateLimiterClient client, int sequenceNumber, int permitCount)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (permitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, "Permit count cannot be negative.");
        }

        var state = GetOrCreateClientState(client);
        state.LastSeen.Restart();

        // Handle duplicate requests (idempotency)
        if (state.SequenceNumber >= sequenceNumber)
        {
            _logger.LogDebug(
                "Duplicate release request (seq={SequenceNumber}, existing={ExistingSeq}), ignoring",
                sequenceNumber, state.SequenceNumber);

            return default;
        }

        // Opportunistically reclaim permits from idle clients
        DropIdleClients();

        // Release permits back to the pool
        state.InUsePermitCount -= permitCount;
        _availablePermits = Math.Min(_availablePermits + permitCount, _options.GlobalPermitCount);

        _logger.LogDebug(
            "Released {PermitCount} permits from client, {AvailablePermits} now available",
            permitCount, _availablePermits);

        // Process any pending requests that can now be satisfied
        ServicePendingRequests();

        // Update client state for idempotency
        state.SequenceNumber = sequenceNumber;
        state.LastAcquiredPermitCount = 0;

        return default;
    }

    /// <inheritdoc />
    public ValueTask RefreshLeases(IRateLimiterClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (_clients.TryGetValue(client, out var state))
        {
            state.LastSeen.Restart();
            _logger.LogTrace("Client lease refreshed");
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask Unregister(IRateLimiterClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (_clients.TryGetValue(client, out var state))
        {
            var releasedPermits = state.InUsePermitCount;
            RemoveClient(client, state);

            _logger.LogInformation(
                "Client unregistered, released {PermitCount} permits, {AvailablePermits} now available",
                releasedPermits, _availablePermits);

            ServicePendingRequests();
        }

        return default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clientPurgeTimer?.Dispose();
        _clientPurgeTimer = null;
    }

    private ClientState GetOrCreateClientState(IRateLimiterClient client)
    {
        if (!_clients.TryGetValue(client, out var state))
        {
            state = new ClientState();
            _clients[client] = state;

            _logger.LogDebug("New client registered, total clients: {ClientCount}", _clients.Count);
        }

        return state;
    }

    private void ServicePendingRequests()
    {
        while (_availablePermits > 0 && _pendingRequests.TryPeek(out var client))
        {
            if (!_clients.TryGetValue(client, out var state))
            {
                // Client was dropped (possibly due to idle timeout)
                _pendingRequests.Dequeue();
                continue;
            }

            if (!state.HasPendingRequest)
            {
                // Request was already satisfied or cancelled
                _pendingRequests.Dequeue();
                continue;
            }

            if (_availablePermits >= state.PermitsToAcquire)
            {
                // Notify the client that permits are available
                try
                {
                    client.OnPermitsAvailable(_availablePermits);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify client of available permits");
                }

                state.HasPendingRequest = false;
                _pendingRequests.Dequeue();
            }
            else
            {
                // Not enough permits for the next request in queue
                break;
            }
        }
    }

    private Task OnPurgeClientsAsync()
    {
        var purgedCount = DropIdleClients();
        if (purgedCount > 0)
        {
            _logger.LogInformation(
                "Purged {PurgedCount} idle clients, {AvailablePermits} permits now available",
                purgedCount, _availablePermits);
        }

        ServicePendingRequests();
        return Task.CompletedTask;
    }

    private int DropIdleClients()
    {
        List<KeyValuePair<IRateLimiterClient, ClientState>>? clientsToDrop = null;

        foreach (var kvp in _clients)
        {
            if (kvp.Value.LastSeen.Elapsed > _options.IdleClientTimeout)
            {
                clientsToDrop ??= new List<KeyValuePair<IRateLimiterClient, ClientState>>();
                clientsToDrop.Add(kvp);
            }
        }

        if (clientsToDrop is null or { Count: 0 })
        {
            return 0;
        }

        foreach (var (client, state) in clientsToDrop)
        {
            var removed = RemoveClient(client, state);
            Debug.Assert(removed, "Client should have been removed");

            _logger.LogDebug(
                "Dropped idle client, reclaimed {PermitCount} permits",
                state.InUsePermitCount);
        }

        return clientsToDrop.Count;
    }

    private bool RemoveClient(IRateLimiterClient client, ClientState? state)
    {
        if (state is not null)
        {
            // Return all in-use permits to the pool
            _availablePermits = Math.Min(
                _availablePermits + state.InUsePermitCount,
                _options.GlobalPermitCount);
        }

        return _clients.Remove(client);
    }

    /// <summary>
    /// Tracks the state of a connected client.
    /// </summary>
    private sealed class ClientState
    {
        /// <summary>
        /// Gets the stopwatch tracking time since last activity.
        /// </summary>
        public Stopwatch LastSeen { get; } = Stopwatch.StartNew();

        /// <summary>
        /// Gets or sets the monotonically increasing sequence number for this client.
        /// Used for idempotent request handling.
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// Gets or sets the number of permits currently in use by this client.
        /// </summary>
        public int InUsePermitCount { get; set; }

        /// <summary>
        /// Gets or sets the number of permits requested in the pending request.
        /// </summary>
        public int PermitsToAcquire { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this client has a pending request.
        /// </summary>
        public bool HasPendingRequest { get; set; }

        /// <summary>
        /// Gets or sets the number of permits acquired in the previous request.
        /// Used for idempotent response handling.
        /// </summary>
        public int LastAcquiredPermitCount { get; set; }
    }
}
