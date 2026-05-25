namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Per-context migration entry point invoked by the MigrationRunner worker
/// before any Api service starts (ADR-0067). Each bounded context's
/// Infrastructure project implements this and registers it via its
/// Add<ContextName>Infrastructure module.
/// </summary>
public interface IMigrator
{
    string ContextName { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
