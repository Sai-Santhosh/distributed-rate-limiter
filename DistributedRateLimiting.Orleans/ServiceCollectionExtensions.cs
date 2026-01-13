using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DistributedRateLimiting.Orleans;

/// <summary>
/// Extension methods for configuring distributed rate limiting services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the distributed rate limiter to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configureOptions">
    /// A delegate to configure the <see cref="DistributedRateLimiterOptions"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configureOptions"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddDistributedRateLimiter(options =>
    /// {
    ///     options.GlobalPermitCount = 100;
    ///     options.TargetPermitsPerClient = 20;
    ///     options.QueueLimit = 200;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddDistributedRateLimiter(
        this IServiceCollection services,
        Action<DistributedRateLimiterOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services
            .AddOptions<DistributedRateLimiterOptions>()
            .Configure(configureOptions)
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch
                {
                    return false;
                }
            }, "Invalid DistributedRateLimiterOptions configuration.");

        // Register the rate limiter as both abstract and concrete types
        services.TryAddSingleton<DistributedRateLimiter>();
        services.TryAddSingleton<RateLimiter>(sp => sp.GetRequiredService<DistributedRateLimiter>());

        return services;
    }

    /// <summary>
    /// Adds the distributed rate limiter to the service collection with a named configuration.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="name">The name of the rate limiter configuration.</param>
    /// <param name="configureOptions">
    /// A delegate to configure the <see cref="DistributedRateLimiterOptions"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty or whitespace.
    /// </exception>
    public static IServiceCollection AddDistributedRateLimiter(
        this IServiceCollection services,
        string name,
        Action<DistributedRateLimiterOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services
            .AddOptions<DistributedRateLimiterOptions>(name)
            .Configure(configureOptions)
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch
                {
                    return false;
                }
            }, $"Invalid DistributedRateLimiterOptions configuration for '{name}'.");

        return services;
    }
}
