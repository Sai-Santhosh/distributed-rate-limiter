using Orleans;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// Orleans grain interface for the distributed rate limiter coordinator.
/// The coordinator manages the global permit pool and tracks client state.
/// </summary>
/// <remarks>
/// <para>
/// This grain is responsible for:
/// <list type="bullet">
///   <item><description>Managing the global permit pool across all clients</description></item>
///   <item><description>Tracking client state including in-use permits and last activity</description></item>
///   <item><description>Purging idle clients to reclaim leaked permits</description></item>
///   <item><description>Notifying clients when capacity becomes available</description></item>
/// </list>
/// </para>
/// <para>
/// Uses sequence numbers for idempotent request handling to support safe retries.
/// </para>
/// </remarks>
internal interface IDistributedRateLimiterCoordinator : IGrainWithStringKey
{
    /// <summary>
    /// Attempts to acquire permits from the global pool.
    /// </summary>
    /// <param name="client">The client requesting permits.</param>
    /// <param name="sequenceNumber">
    /// A monotonically increasing sequence number for idempotent request handling.
    /// </param>
    /// <param name="permitCount">The number of permits to acquire.</param>
    /// <returns>
    /// The number of permits actually acquired, which may be less than requested
    /// if insufficient permits are available.
    /// </returns>
    ValueTask<int> TryAcquire(IRateLimiterClient client, int sequenceNumber, int permitCount);

    /// <summary>
    /// Releases permits back to the global pool.
    /// </summary>
    /// <param name="client">The client releasing permits.</param>
    /// <param name="sequenceNumber">
    /// A monotonically increasing sequence number for idempotent request handling.
    /// </param>
    /// <param name="permitCount">The number of permits to release.</param>
    ValueTask Release(IRateLimiterClient client, int sequenceNumber, int permitCount);

    /// <summary>
    /// Refreshes the client's lease to indicate it is still active.
    /// </summary>
    /// <param name="client">The client refreshing its lease.</param>
    /// <remarks>
    /// Clients should call this periodically to prevent being purged as idle.
    /// </remarks>
    ValueTask RefreshLeases(IRateLimiterClient client);

    /// <summary>
    /// Unregisters a client and releases all of its in-use permits.
    /// </summary>
    /// <param name="client">The client to unregister.</param>
    /// <remarks>
    /// This should be called during graceful shutdown to immediately release
    /// permits instead of waiting for the idle timeout.
    /// </remarks>
    ValueTask Unregister(IRateLimiterClient client);
}
