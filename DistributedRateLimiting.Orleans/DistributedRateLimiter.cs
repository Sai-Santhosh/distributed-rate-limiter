using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// A distributed <see cref="RateLimiter"/> implementation that manages concurrent
/// access to a shared resource across multiple service instances using Orleans.
/// </summary>
/// <remarks>
/// <para>
/// This rate limiter maintains a local permit cache for fast-path decisions while
/// coordinating with a central Orleans grain to enforce global rate limits. It
/// supports queuing requests when permits are not immediately available.
/// </para>
/// <para>
/// Key features:
/// <list type="bullet">
///   <item><description>Global rate limiting across distributed instances</description></item>
///   <item><description>Local permit caching for reduced coordinator calls</description></item>
///   <item><description>Bounded queue with backpressure</description></item>
///   <item><description>Cancellation support</description></item>
///   <item><description>Idempotent requests via sequence numbers</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class DistributedRateLimiter : RateLimiter, IRateLimiterClient
{
    private static readonly double TickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    private static readonly RateLimitLease SuccessfulLease = new PermitLease(isAcquired: true, limiter: null, count: 0);
    private static readonly RateLimitLease FailedLease = new PermitLease(isAcquired: false, limiter: null, count: 0);
    private static readonly RateLimitLease QueueLimitLease = new PermitLease(isAcquired: false, limiter: null, count: 0, reason: "Queue limit reached");

    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<DistributedRateLimiter> _logger;
    private readonly DistributedRateLimiterOptions _options;
    private readonly Queue<RequestRegistration> _queue = new();
    private readonly IDistributedRateLimiterCoordinator _coordinator;
    private readonly SemaphoreSlim _processSignal = new(1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _backgroundTask;

    private int _localAvailablePermitCount;
    private int _outstandingRequestedPermitCount;
    private long? _idleSince = Stopwatch.GetTimestamp();
    private bool _disposed;

    // Use the queue as the lock object to avoid allocating a separate lock object
    private object Lock => _queue;

    /// <inheritdoc />
    public override TimeSpan? IdleDuration => _idleSince is null
        ? null
        : new TimeSpan((long)((Stopwatch.GetTimestamp() - _idleSince.Value) * TickFrequency));

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedRateLimiter"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The rate limiter options.</param>
    /// <param name="grainFactory">The Orleans grain factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    public DistributedRateLimiter(
        ILogger<DistributedRateLimiter> logger,
        IOptions<DistributedRateLimiterOptions> options,
        IGrainFactory grainFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Validate options on startup
        _options.Validate();

        _coordinator = _grainFactory.GetGrain<IDistributedRateLimiterCoordinator>("default");
        _backgroundTask = Task.Run(ProcessRequestsAsync);

        _logger.LogInformation(
            "DistributedRateLimiter initialized with GlobalPermitCount={GlobalPermitCount}, TargetPermitsPerClient={TargetPermitsPerClient}, QueueLimit={QueueLimit}",
            _options.GlobalPermitCount,
            _options.TargetPermitsPerClient,
            _options.QueueLimit);
    }

    /// <inheritdoc />
    public override int GetStatistics() => _localAvailablePermitCount;

    /// <inheritdoc />
    public override int GetAvailablePermits() => _localAvailablePermitCount;

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        if (permitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, "Permit count cannot be negative.");
        }

        // These amounts of resources can never be acquired
        if (permitCount > _options.GlobalPermitCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permitCount),
                permitCount,
                $"Permit count ({permitCount}) exceeds global limit ({_options.GlobalPermitCount}).");
        }

        ThrowIfDisposed();

        // Special case: zero permits requested
        if (permitCount == 0)
        {
            return _localAvailablePermitCount > 0 ? SuccessfulLease : FailedLease;
        }

        // Fast path: check if we have enough permits locally
        if (_localAvailablePermitCount >= permitCount)
        {
            lock (Lock)
            {
                if (TryLeaseUnsynchronized(permitCount, out var lease))
                {
                    return lease;
                }
            }
        }

        return FailedLease;
    }

    /// <inheritdoc />
    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken = default)
    {
        if (permitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitCount), permitCount, "Permit count cannot be negative.");
        }

        // These amounts of resources can never be acquired
        if (permitCount > _options.GlobalPermitCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permitCount),
                permitCount,
                $"Permit count ({permitCount}) exceeds global limit ({_options.GlobalPermitCount}).");
        }

        // Special case: zero permits requested and resources available
        if (permitCount == 0 && _localAvailablePermitCount > 0 && !_disposed)
        {
            return new ValueTask<RateLimitLease>(SuccessfulLease);
        }

        lock (Lock)
        {
            // Try to lease immediately
            if (TryLeaseUnsynchronized(permitCount, out var lease))
            {
                return new ValueTask<RateLimitLease>(lease);
            }

            // Check queue capacity (use subtraction to avoid overflow)
            Debug.Assert(_options.QueueLimit >= _outstandingRequestedPermitCount);
            if (_options.QueueLimit - _outstandingRequestedPermitCount < permitCount)
            {
                _logger.LogWarning(
                    "Queue limit reached. Requested={PermitCount}, Outstanding={Outstanding}, Limit={Limit}",
                    permitCount, _outstandingRequestedPermitCount, _options.QueueLimit);

                return new ValueTask<RateLimitLease>(QueueLimitLease);
            }

            // Create a registration for the queued request
            var tcs = new CancelQueueState(permitCount, this, cancellationToken);
            CancellationTokenRegistration ctr = default;

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(
                    static obj => ((CancelQueueState)obj!).TrySetCanceled(),
                    tcs);
            }

            var registration = new RequestRegistration(permitCount, tcs, ctr);
            _queue.Enqueue(registration);
            _outstandingRequestedPermitCount += permitCount;

            Debug.Assert(_outstandingRequestedPermitCount <= _options.QueueLimit);

            // Signal the background task to request more permits
            _processSignal.Release();

            return new ValueTask<RateLimitLease>(registration.Tcs.Task);
        }
    }

    /// <summary>
    /// Called by the coordinator when permits become available.
    /// </summary>
    /// <param name="availablePermits">The number of permits now available.</param>
    void IRateLimiterClient.OnPermitsAvailable(int availablePermits)
    {
        _logger.LogTrace("Notified of {AvailablePermits} available permits", availablePermits);
        _processSignal.Release();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Cancel background processing
            _shutdownCts.Cancel();

            // Fail all queued requests
            while (_queue.Count > 0)
            {
                var registration = _queue.Dequeue();
                registration.CancellationTokenRegistration.Dispose();
                registration.Tcs.TrySetResult(FailedLease);
            }

            _outstandingRequestedPermitCount = 0;
        }

        _logger.LogInformation("DistributedRateLimiter disposed");
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        Dispose(disposing: true);

        // Wait for the background task to complete
        try
        {
            await _backgroundTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private bool TryLeaseUnsynchronized(int permitCount, [NotNullWhen(true)] out RateLimitLease? lease)
    {
        ThrowIfDisposed();

        // If permits are available and no queue backlog exists, grant immediately
        if (_localAvailablePermitCount >= permitCount && _localAvailablePermitCount != 0)
        {
            if (permitCount == 0)
            {
                // Edge case: check revealed permits became available
                lease = SuccessfulLease;
                return true;
            }

            // Only lease if there's no queue (maintains FIFO ordering)
            if (_outstandingRequestedPermitCount == 0)
            {
                _idleSince = null;
                _localAvailablePermitCount -= permitCount;
                Debug.Assert(_localAvailablePermitCount >= 0);

                lease = new PermitLease(isAcquired: true, limiter: this, count: permitCount);
                return true;
            }
        }

        lease = null;
        return false;
    }

    private void Release(int releaseCount)
    {
        lock (Lock)
        {
            if (_disposed)
            {
                return;
            }

            _localAvailablePermitCount += releaseCount;
            Debug.Assert(_localAvailablePermitCount <= _options.GlobalPermitCount);

            // Try to satisfy queued requests
            while (_queue.Count > 0)
            {
                var nextRequest = _queue.Peek();

                if (_localAvailablePermitCount >= nextRequest.Count)
                {
                    _queue.Dequeue();
                    _localAvailablePermitCount -= nextRequest.Count;
                    _outstandingRequestedPermitCount -= nextRequest.Count;

                    Debug.Assert(_localAvailablePermitCount >= 0);

                    var lease = nextRequest.Count == 0
                        ? SuccessfulLease
                        : new PermitLease(isAcquired: true, limiter: this, count: nextRequest.Count);

                    // If the request was already cancelled, add permits back
                    if (!nextRequest.Tcs.TrySetResult(lease))
                    {
                        _localAvailablePermitCount += nextRequest.Count;
                        _outstandingRequestedPermitCount += nextRequest.Count;
                    }

                    nextRequest.CancellationTokenRegistration.Dispose();
                    Debug.Assert(_outstandingRequestedPermitCount >= 0);
                }
                else
                {
                    break;
                }
            }

            if (_outstandingRequestedPermitCount == 0)
            {
                _idleSince = Stopwatch.GetTimestamp();
            }

            // Signal background task to potentially release surplus permits
            _processSignal.Release();
        }
    }

    private async Task ProcessRequestsAsync()
    {
        // RequestId provides idempotency for coordinator calls
        int requestId = 1;
        IRateLimiterClient? selfReference = null;
        int pendingToRelease = 0;
        int pendingToAcquire = 0;
        var lastRefresh = Stopwatch.StartNew();

        while (!_shutdownCts.Token.IsCancellationRequested)
        {
            try
            {
                // Create object reference for callbacks if not yet created
                selfReference ??= await _grainFactory
                    .CreateObjectReference<IRateLimiterClient>(this)
                    .ConfigureAwait(false);

                // Wait for work or periodic refresh
                if (pendingToRelease == 0 && pendingToAcquire == 0)
                {
                    if (lastRefresh.Elapsed > _options.ClientLeaseRefreshInterval)
                    {
                        await _coordinator.RefreshLeases(selfReference).ConfigureAwait(false);
                        lastRefresh.Restart();
                    }
                    else
                    {
                        var waitTime = (int)(_options.ClientLeaseRefreshInterval.TotalMilliseconds - lastRefresh.Elapsed.TotalMilliseconds);
                        waitTime = Math.Max(waitTime, 1);

                        await _processSignal
                            .WaitAsync(waitTime, _shutdownCts.Token)
                            .ConfigureAwait(false);
                    }
                }

                // Calculate permits to acquire
                lock (Lock)
                {
                    if (pendingToAcquire == 0)
                    {
                        var deficit = _options.TargetPermitsPerClient - _localAvailablePermitCount;
                        pendingToAcquire = Math.Clamp(deficit, 0, _options.TargetPermitsPerClient);

                        // Ensure we request enough for any large queued request
                        if (_queue.TryPeek(out var request))
                        {
                            var neededForRequest = request.Count - _localAvailablePermitCount;
                            pendingToAcquire = Math.Max(pendingToAcquire, neededForRequest);
                        }
                    }
                }

                // Acquire permits from coordinator
                if (pendingToAcquire > 0)
                {
                    var acquired = await _coordinator
                        .TryAcquire(selfReference, requestId, pendingToAcquire)
                        .ConfigureAwait(false);

                    requestId++;
                    pendingToAcquire = 0;

                    if (acquired > 0)
                    {
                        Release(acquired);
                    }
                }

                // Calculate permits to release (surplus)
                lock (Lock)
                {
                    if (pendingToRelease == 0)
                    {
                        var surplus = _localAvailablePermitCount - _options.TargetPermitsPerClient;
                        if (surplus > 0)
                        {
                            pendingToRelease = surplus;
                            _localAvailablePermitCount -= pendingToRelease;
                        }
                    }
                }

                // Release surplus permits to coordinator
                if (pendingToRelease > 0)
                {
                    await _coordinator
                        .Release(selfReference, requestId, pendingToRelease)
                        .ConfigureAwait(false);

                    requestId++;
                    pendingToRelease = 0;
                }
            }
            catch (OperationCanceledException) when (_shutdownCts.Token.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate limiter background processing");

                // Back off before retrying
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // Try to unregister from coordinator during graceful shutdown
        if (selfReference is not null)
        {
            try
            {
                await _coordinator.Unregister(selfReference).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister from coordinator during shutdown");
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// A lease that releases permits back to the limiter when disposed.
    /// </summary>
    private sealed class PermitLease : RateLimitLease
    {
        private static readonly string[] AllMetadataNames = [MetadataName.ReasonPhrase.Name];

        private readonly DistributedRateLimiter? _limiter;
        private readonly int _count;
        private readonly string? _reason;
        private bool _disposed;

        public PermitLease(bool isAcquired, DistributedRateLimiter? limiter, int count, string? reason = null)
        {
            IsAcquired = isAcquired;
            _limiter = limiter;
            _count = count;
            _reason = reason;

            // No need to track limiter if count is 0 (nothing to release)
            Debug.Assert(count == 0 ? limiter is null : true);
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames => AllMetadataNames;

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_reason is not null && metadataName == MetadataName.ReasonPhrase.Name)
            {
                metadata = _reason;
                return true;
            }

            metadata = default;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing && _count > 0)
            {
                _limiter?.Release(_count);
            }
        }
    }

    /// <summary>
    /// Represents a queued request waiting for permits.
    /// </summary>
    private readonly struct RequestRegistration
    {
        public RequestRegistration(
            int count,
            TaskCompletionSource<RateLimitLease> tcs,
            CancellationTokenRegistration cancellationTokenRegistration)
        {
            Count = count;
            Tcs = tcs;
            CancellationTokenRegistration = cancellationTokenRegistration;
        }

        public int Count { get; }
        public TaskCompletionSource<RateLimitLease> Tcs { get; }
        public CancellationTokenRegistration CancellationTokenRegistration { get; }
    }

    /// <summary>
    /// TaskCompletionSource subclass that handles cancellation of queued requests.
    /// </summary>
    private sealed class CancelQueueState : TaskCompletionSource<RateLimitLease>
    {
        private readonly int _permitCount;
        private readonly DistributedRateLimiter _limiter;
        private readonly CancellationToken _cancellationToken;

        public CancelQueueState(
            int permitCount,
            DistributedRateLimiter limiter,
            CancellationToken cancellationToken)
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            _permitCount = permitCount;
            _limiter = limiter;
            _cancellationToken = cancellationToken;
        }

        public new bool TrySetCanceled()
        {
            if (TrySetCanceled(_cancellationToken))
            {
                lock (_limiter.Lock)
                {
                    _limiter._outstandingRequestedPermitCount -= _permitCount;
                }

                return true;
            }

            return false;
        }
    }
}
