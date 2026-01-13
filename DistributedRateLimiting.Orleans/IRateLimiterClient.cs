using Orleans;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// Orleans grain observer interface for rate limiter clients.
/// Allows the coordinator to notify clients when capacity becomes available.
/// </summary>
/// <remarks>
/// This interface is implemented by <see cref="DistributedRateLimiter"/> and
/// registered as a grain observer to receive asynchronous notifications from
/// the coordinator grain.
/// </remarks>
internal interface IRateLimiterClient : IGrainObserver
{
    /// <summary>
    /// Called by the coordinator to notify the client that permits are available.
    /// </summary>
    /// <param name="availablePermits">
    /// The approximate number of permits currently available in the global pool.
    /// </param>
    /// <remarks>
    /// This notification is advisory; the client should attempt to acquire permits
    /// through the normal <see cref="IDistributedRateLimiterCoordinator.TryAcquire"/>
    /// call path after receiving this notification.
    /// </remarks>
    void OnPermitsAvailable(int availablePermits);
}
