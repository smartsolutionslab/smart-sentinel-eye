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

    /// <summary>
    /// Deletes the row (spec 317, #2325, FR-013). Undeclared means no row
    /// (spec 269 FR-003), so unlike every other aggregate in this solution
    /// there is no soft-delete state to flip.
    /// </summary>
    void Remove(SourceMode sourceMode);

    Task SaveAsync(CancellationToken cancellationToken);
}
