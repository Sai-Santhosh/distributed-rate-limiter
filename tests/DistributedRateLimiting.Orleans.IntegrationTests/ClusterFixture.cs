using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DistributedRateLimiting.Orleans.IntegrationTests;

/// <summary>
/// Fixture that provides a test Orleans cluster for integration tests.
/// </summary>
public sealed class ClusterFixture : IAsyncLifetime
{
    /// <summary>
    /// Gets the test cluster instance.
    /// </summary>
    public TestCluster Cluster { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();

        builder.AddSiloBuilderConfigurator<SiloConfigurator>();

        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
        await Cluster.DisposeAsync();
    }

    /// <summary>
    /// Silo configurator for the test cluster.
    /// </summary>
    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.ConfigureServices(services =>
            {
                services.AddDistributedRateLimiter(options =>
                {
                    options.GlobalPermitCount = 100;
                    options.TargetPermitsPerClient = 20;
                    options.QueueLimit = 200;
                    options.IdleClientTimeout = TimeSpan.FromMinutes(1);
                    options.ClientLeaseRefreshInterval = TimeSpan.FromSeconds(30);
                });
            });
        }
    }
}

/// <summary>
/// Collection definition for cluster-based integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    /// <summary>
    /// The collection name.
    /// </summary>
    public const string Name = "ClusterCollection";
}
