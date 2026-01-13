using System.Net;
using System.Threading.RateLimiting;
using DistributedRateLimiting.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;

// Parse instance ID from command line arguments
int instanceId = args.Length > 0 && int.TryParse(args[0], out var id) ? id : 0;

Console.WriteLine($"Starting Orleans silo instance {instanceId}...");

var builder = Host.CreateDefaultBuilder(args);

builder
    .UseOrleans(silo =>
    {
        // Configure localhost clustering for development
        silo.UseLocalhostClustering(
            siloPort: 11111 + instanceId,
            gatewayPort: 30000 + instanceId,
            primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, 11111));

        silo.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev";
            options.ServiceId = "distributed-rate-limiter-demo";
        });
    })
    .ConfigureServices(services =>
    {
        services.AddDistributedRateLimiter(options =>
        {
            options.GlobalPermitCount = 100;
            options.TargetPermitsPerClient = 20;
            options.QueueLimit = 200;
            options.IdleClientTimeout = TimeSpan.FromMinutes(1);
            options.ClientLeaseRefreshInterval = TimeSpan.FromSeconds(30);
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Orleans", LogLevel.Warning);
        logging.AddFilter("Microsoft", LogLevel.Warning);
    })
    .UseConsoleLifetime();

await using var host = builder.Build();
await host.StartAsync();

Console.WriteLine($"Silo instance {instanceId} started. Press Ctrl+C to stop.");

var rateLimiter = host.Services.GetRequiredService<RateLimiter>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Worker");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutting down...");
};

// Track active lease holders for logging
long activeLeaseHolders = 0;

// Start multiple worker tasks
const int workerCount = 5;
var workers = new List<Task>(workerCount);

for (int i = 0; i < workerCount; i++)
{
    var workerName = $"worker-{instanceId}-{i}";
    workers.Add(RunWorkerAsync(rateLimiter, workerName, cts.Token));
}

try
{
    await Task.WhenAll(workers);
}
catch (OperationCanceledException)
{
    // Normal shutdown
}

await host.StopAsync();

Console.WriteLine("Silo stopped.");

async Task RunWorkerAsync(RateLimiter limiter, string name, CancellationToken cancellationToken)
{
    const int permitCount = 25;
    var random = new Random();

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            // First try to acquire synchronously
            RateLimitLease lease = limiter.AttemptAcquire(permitCount);

            if (!lease.IsAcquired)
            {
                var holders = Interlocked.Read(ref activeLeaseHolders);
                logger.LogInformation("{Worker}: Waiting for {PermitCount} permits (active holders: {Holders})", name, permitCount, holders);

                // Wait asynchronously for permits
                lease = await limiter.AcquireAsync(permitCount, cancellationToken);
            }

            if (!lease.IsAcquired)
            {
                // Check if there's a reason for failure
                if (lease.TryGetMetadata(MetadataName.ReasonPhrase, out var reason))
                {
                    logger.LogWarning("{Worker}: Failed to acquire permits - {Reason}", name, reason);
                }
                else
                {
                    logger.LogWarning("{Worker}: Failed to acquire permits", name);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                continue;
            }

            try
            {
                var holders = Interlocked.Increment(ref activeLeaseHolders);
                logger.LogInformation("{Worker}: Acquired {PermitCount} permits (active holders: {Holders})", name, permitCount, holders);

                // Simulate work
                var workDuration = TimeSpan.FromMilliseconds(random.Next(200, 800));
                await Task.Delay(workDuration, cancellationToken);
            }
            finally
            {
                lease.Dispose();
                var holders = Interlocked.Decrement(ref activeLeaseHolders);
                logger.LogInformation("{Worker}: Released permits (active holders: {Holders})", name, holders);
            }

            // Brief pause between iterations
            await Task.Delay(TimeSpan.FromMilliseconds(random.Next(100, 500)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Worker}: Error during work iteration", name);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    logger.LogInformation("{Worker}: Worker stopped", name);
}
