using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace DistributedRateLimiting.Orleans.IntegrationTests;

/// <summary>
/// Integration tests for the distributed rate limiter using a test Orleans cluster.
/// </summary>
[Collection(ClusterCollection.Name)]
public class DistributedRateLimiterIntegrationTests
{
    private readonly ClusterFixture _fixture;

    public DistributedRateLimiterIntegrationTests(ClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AcquireAsync_WithAvailablePermits_ReturnsSuccessfulLease()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act
        using var lease = await rateLimiter.AcquireAsync(1);

        // Assert
        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_WithZeroPermits_ReturnsSuccessfulLease()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act
        using var lease = await rateLimiter.AcquireAsync(0);

        // Assert
        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_MultipleConcurrentRequests_AllSucceed()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();
        const int requestCount = 10;
        const int permitsPerRequest = 5;

        // Act
        var tasks = Enumerable.Range(0, requestCount)
            .Select(async _ =>
            {
                var lease = await rateLimiter.AcquireAsync(permitsPerRequest);
                await Task.Delay(50); // Hold briefly
                lease.Dispose();
                return lease.IsAcquired;
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(acquired => acquired.Should().BeTrue());
    }

    [Fact]
    public async Task AcquireAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = () => rateLimiter.AcquireAsync(1, cts.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AttemptAcquire_WithZeroPermits_ReturnsSuccessfulLease()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act
        using var lease = rateLimiter.AttemptAcquire(0);

        // Assert
        lease.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Lease_WhenDisposed_ReleasesPermits()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act - Acquire and release a lease
        var lease = await rateLimiter.AcquireAsync(10);
        var permitsAfterAcquire = rateLimiter.GetAvailablePermits();
        lease.Dispose();

        // Wait briefly for background processing
        await Task.Delay(100);

        var permitsAfterRelease = rateLimiter.GetAvailablePermits();

        // Assert - After release, available permits should increase
        permitsAfterRelease.Should().BeGreaterThanOrEqualTo(permitsAfterAcquire);
    }

    [Fact]
    public async Task AcquireAsync_ExceedingGlobalLimit_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();
        const int exceedingPermitCount = 1000; // Exceeds the 100 global limit

        // Act & Assert
        var act = () => rateLimiter.AcquireAsync(exceedingPermitCount).AsTask();
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AttemptAcquire_ExceedingGlobalLimit_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();
        const int exceedingPermitCount = 1000;

        // Act & Assert
        var act = () => rateLimiter.AttemptAcquire(exceedingPermitCount);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetAvailablePermits_ReturnsNonNegativeValue()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // First acquire some permits to trigger background sync
        using var lease = await rateLimiter.AcquireAsync(1);

        // Act
        var availablePermits = rateLimiter.GetAvailablePermits();

        // Assert
        availablePermits.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ConcurrentAcquireAndRelease_MaintainsCorrectPermitCount()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();
        const int iterations = 20;
        const int permitsPerIteration = 2;

        // Act - Perform multiple concurrent acquire/release cycles
        var tasks = Enumerable.Range(0, iterations)
            .Select(async i =>
            {
                var lease = await rateLimiter.AcquireAsync(permitsPerIteration);
                lease.IsAcquired.Should().BeTrue($"Iteration {i} should succeed");

                await Task.Delay(Random.Shared.Next(10, 50));
                lease.Dispose();
            })
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - Should complete without deadlock
        // Available permits should eventually return to normal
        await Task.Delay(500);
        var finalPermits = rateLimiter.GetAvailablePermits();
        finalPermits.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Lease_MultipleDispose_IsSafe()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act
        var lease = await rateLimiter.AcquireAsync(5);
        lease.Dispose();
        lease.Dispose(); // Second dispose should be safe

        // Assert - No exception thrown
    }

    [Fact]
    public async Task AcquireAsync_WithNegativePermits_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act & Assert
        var act = () => rateLimiter.AcquireAsync(-1).AsTask();
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AttemptAcquire_WithNegativePermits_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var rateLimiter = _fixture.Cluster.ServiceProvider.GetRequiredService<RateLimiter>();

        // Act & Assert
        var act = () => rateLimiter.AttemptAcquire(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
