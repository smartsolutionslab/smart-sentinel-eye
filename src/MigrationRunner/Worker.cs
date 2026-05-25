namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Placeholder. Runs all bounded-context migrations sequentially and stops the application.
/// Real migration orchestration is wired when the first context's persistence lands.
/// </summary>
public sealed class MigrationRunnerHostedService(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.StopApplication();
        return Task.CompletedTask;
    }
}
