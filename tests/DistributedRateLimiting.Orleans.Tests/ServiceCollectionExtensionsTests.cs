using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace DistributedRateLimiting.Orleans.Tests;

/// <summary>
/// Unit tests for <see cref="ServiceCollectionExtensions"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDistributedRateLimiter_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        var act = () => services!.AddDistributedRateLimiter(_ => { });
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddDistributedRateLimiter_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<DistributedRateLimiterOptions>? configureOptions = null;

        // Act & Assert
        var act = () => services.AddDistributedRateLimiter(configureOptions!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddDistributedRateLimiter_RegistersOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = 100;
            options.TargetPermitsPerClient = 20;
            options.QueueLimit = 200;
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfigureOptions<DistributedRateLimiterOptions>));
        descriptor.Should().NotBeNull();
    }

    [Fact]
    public void AddDistributedRateLimiter_RegistersDistributedRateLimiter()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = 100;
            options.TargetPermitsPerClient = 20;
            options.QueueLimit = 200;
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DistributedRateLimiter));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddDistributedRateLimiter_RegistersRateLimiterAbstraction()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = 100;
            options.TargetPermitsPerClient = 20;
            options.QueueLimit = 200;
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(RateLimiter));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddDistributedRateLimiter_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = 100;
            options.TargetPermitsPerClient = 20;
            options.QueueLimit = 200;
        });

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddDistributedRateLimiter_WithName_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddDistributedRateLimiter(null!, _ => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddDistributedRateLimiter_WithName_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddDistributedRateLimiter(string.Empty, _ => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddDistributedRateLimiter_WithName_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddDistributedRateLimiter("   ", _ => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddDistributedRateLimiter_ConfiguresOptionsCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        const int expectedGlobalPermitCount = 500;
        const int expectedTargetPermits = 50;
        const int expectedQueueLimit = 1000;

        // Act
        services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = expectedGlobalPermitCount;
            options.TargetPermitsPerClient = expectedTargetPermits;
            options.QueueLimit = expectedQueueLimit;
        });

        // Build provider and resolve options
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DistributedRateLimiterOptions>>().Value;

        // Assert
        options.GlobalPermitCount.Should().Be(expectedGlobalPermitCount);
        options.TargetPermitsPerClient.Should().Be(expectedTargetPermits);
        options.QueueLimit.Should().Be(expectedQueueLimit);
    }
}
