namespace DistributedRateLimiting.Orleans.Tests;

/// <summary>
/// Unit tests for <see cref="DistributedRateLimiterOptions"/> validation.
/// </summary>
public class DistributedRateLimiterOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromMinutes(1),
            ClientLeaseRefreshInterval = TimeSpan.FromSeconds(30)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidGlobalPermitCount_ThrowsArgumentException(int invalidCount)
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = invalidCount,
            TargetPermitsPerClient = 20,
            QueueLimit = 200
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.GlobalPermitCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidTargetPermitsPerClient_ThrowsArgumentException(int invalidCount)
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = invalidCount,
            QueueLimit = 200
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.TargetPermitsPerClient));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithNegativeQueueLimit_ThrowsArgumentException(int invalidLimit)
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = invalidLimit
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.QueueLimit));
    }

    [Fact]
    public void Validate_WithZeroQueueLimit_DoesNotThrow()
    {
        // Arrange - Zero queue limit is valid (no queuing)
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 0,
            IdleClientTimeout = TimeSpan.FromMinutes(1),
            ClientLeaseRefreshInterval = TimeSpan.FromSeconds(30)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidIdleClientTimeout_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromSeconds(seconds),
            ClientLeaseRefreshInterval = TimeSpan.FromSeconds(1)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.IdleClientTimeout));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidClientLeaseRefreshInterval_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromMinutes(1),
            ClientLeaseRefreshInterval = TimeSpan.FromSeconds(seconds)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.ClientLeaseRefreshInterval));
    }

    [Fact]
    public void Validate_WhenRefreshIntervalExceedsIdleTimeout_ThrowsArgumentException()
    {
        // Arrange - Refresh interval must be less than idle timeout
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromSeconds(30),
            ClientLeaseRefreshInterval = TimeSpan.FromMinutes(1) // Greater than idle timeout
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.ClientLeaseRefreshInterval));
    }

    [Fact]
    public void Validate_WhenRefreshIntervalEqualsIdleTimeout_ThrowsArgumentException()
    {
        // Arrange - Equal values should also be rejected
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 100,
            TargetPermitsPerClient = 20,
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromMinutes(1),
            ClientLeaseRefreshInterval = TimeSpan.FromMinutes(1)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.ClientLeaseRefreshInterval));
    }

    [Fact]
    public void Validate_WhenTargetPermitsExceedsGlobalCount_ThrowsArgumentException()
    {
        // Arrange - Target permits per client should not exceed global count
        var options = new DistributedRateLimiterOptions
        {
            GlobalPermitCount = 50,
            TargetPermitsPerClient = 100, // Greater than global
            QueueLimit = 200,
            IdleClientTimeout = TimeSpan.FromMinutes(1),
            ClientLeaseRefreshInterval = TimeSpan.FromSeconds(30)
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(DistributedRateLimiterOptions.TargetPermitsPerClient));
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange
        var options = new DistributedRateLimiterOptions();

        // Assert
        options.IdleClientTimeout.Should().Be(DistributedRateLimiterOptions.DefaultIdleClientTimeout);
        options.ClientLeaseRefreshInterval.Should().Be(DistributedRateLimiterOptions.DefaultClientLeaseRefreshInterval);
        options.GlobalPermitCount.Should().Be(0);
        options.TargetPermitsPerClient.Should().Be(0);
        options.QueueLimit.Should().Be(0);
    }

    [Fact]
    public void DefaultIdleClientTimeout_IsOneMinute()
    {
        DistributedRateLimiterOptions.DefaultIdleClientTimeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DefaultClientLeaseRefreshInterval_IsThirtySeconds()
    {
        DistributedRateLimiterOptions.DefaultClientLeaseRefreshInterval.Should().Be(TimeSpan.FromSeconds(30));
    }
}
