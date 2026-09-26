using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.SourceMode;

/// <summary>
/// Write-side repository for <see cref="SourceMode"/> declarations. The
/// ingest path does not use this — it reads through
/// <c>Application.Ingress.IEventTypeAdmissionSource</c> instead (plan.md §2,
/// §6.1).
/// </summary>
public interface ISourceModeRepository
{
    Task<Option<SourceMode>> GetAsync(FabIdentifier fab, Source source, CancellationToken cancellationToken);

    void Add(SourceMode sourceMode);

    Task SaveAsync(CancellationToken cancellationToken);
}
