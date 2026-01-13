using System.ComponentModel.DataAnnotations;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// Configuration options for the <see cref="DistributedRateLimiter"/>.
/// These options control the behavior of both the client-side rate limiter
/// and the cluster-wide coordinator grain.
/// </summary>
public sealed class DistributedRateLimiterOptions
{
    /// <summary>
    /// The default value for <see cref="IdleClientTimeout"/>.
    /// </summary>
    public static readonly TimeSpan DefaultIdleClientTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The default value for <see cref="ClientLeaseRefreshInterval"/>.
    /// </summary>
    public static readonly TimeSpan DefaultClientLeaseRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of permits that can be leased
    /// by all rate limiter instances combined across the entire cluster.
    /// </summary>
    /// <remarks>
    /// This value represents the global throttling limit. All client instances
    /// compete for permits from this shared pool.
    /// </remarks>
    /// <value>The global permit count. Must be greater than 0.</value>
    [Range(1, int.MaxValue, ErrorMessage = "GlobalPermitCount must be greater than 0.")]
    public int GlobalPermitCount { get; set; }

    /// <summary>
    /// Gets or sets the number of permits each client tries to maintain
    /// in its local cache for fast-path decisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Higher values reduce round-trips to the coordinator grain but may
    /// cause permit hoarding when there are many clients.
    /// </para>
    /// <para>
    /// A good rule of thumb is to set this to approximately
    /// <c>GlobalPermitCount / ExpectedNumberOfClients</c>.
    /// </para>
    /// </remarks>
    /// <value>The target permits per client. Must be greater than 0.</value>
    [Range(1, int.MaxValue, ErrorMessage = "TargetPermitsPerClient must be greater than 0.")]
    public int TargetPermitsPerClient { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of permits that can be queued
    /// concurrently when permits are not immediately available.
    /// </summary>
    /// <remarks>
    /// When this limit is reached, additional requests will fail immediately
    /// with a queue limit exceeded response. This provides backpressure to
    /// prevent unbounded memory growth during burst traffic.
    /// </remarks>
    /// <value>The queue limit. Must be greater than or equal to 0.</value>
    [Range(0, int.MaxValue, ErrorMessage = "QueueLimit must be greater than or equal to 0.")]
    public int QueueLimit { get; set; }

    /// <summary>
    /// Gets or sets the period of time after which idle clients are dropped
    /// and their in-use permits are reclaimed.
    /// </summary>
    /// <remarks>
    /// This prevents permit leaks when client processes crash without
    /// properly releasing their permits. Clients that are still alive
    /// periodically refresh their leases to avoid being purged.
    /// </remarks>
    /// <value>The idle client timeout. Defaults to 1 minute.</value>
    public TimeSpan IdleClientTimeout { get; set; } = DefaultIdleClientTimeout;

    /// <summary>
    /// Gets or sets the interval at which clients refresh their leases
    /// with the coordinator to indicate they are still alive.
    /// </summary>
    /// <remarks>
    /// This should be set to a value less than <see cref="IdleClientTimeout"/>
    /// to ensure active clients are not accidentally purged.
    /// </remarks>
    /// <value>The client lease refresh interval. Defaults to 30 seconds.</value>
    public TimeSpan ClientLeaseRefreshInterval { get; set; } = DefaultClientLeaseRefreshInterval;

    /// <summary>
    /// Validates the configuration options and throws if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the configuration is invalid.
    /// </exception>
    public void Validate()
    {
        if (GlobalPermitCount <= 0)
        {
            throw new ArgumentException(
                "GlobalPermitCount must be greater than 0.",
                nameof(GlobalPermitCount));
        }

        if (TargetPermitsPerClient <= 0)
        {
            throw new ArgumentException(
                "TargetPermitsPerClient must be greater than 0.",
                nameof(TargetPermitsPerClient));
        }

        if (QueueLimit < 0)
        {
            throw new ArgumentException(
                "QueueLimit must be greater than or equal to 0.",
                nameof(QueueLimit));
        }

        if (IdleClientTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "IdleClientTimeout must be greater than zero.",
                nameof(IdleClientTimeout));
        }

        if (ClientLeaseRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "ClientLeaseRefreshInterval must be greater than zero.",
                nameof(ClientLeaseRefreshInterval));
        }

        if (ClientLeaseRefreshInterval >= IdleClientTimeout)
        {
            throw new ArgumentException(
                "ClientLeaseRefreshInterval should be less than IdleClientTimeout to prevent active clients from being purged.",
                nameof(ClientLeaseRefreshInterval));
        }

        if (TargetPermitsPerClient > GlobalPermitCount)
        {
            throw new ArgumentException(
                "TargetPermitsPerClient should not exceed GlobalPermitCount.",
                nameof(TargetPermitsPerClient));
        }
    }
}
