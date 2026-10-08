using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Tests;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Stands in for <c>SourceModeRepository</c> plus
/// <c>AggregateVersionInterceptor</c>, mirroring
/// <c>InMemoryRegisteredEventTypeRepository</c> (ADR-0054, no mocking
/// framework).
/// </summary>
public sealed class InMemorySourceModeRepository : ISourceModeRepository
{
    private readonly List<SourceMode> sourceModes = [];
    private readonly HashSet<Guid> persisted = [];

    public IReadOnlyList<SourceMode> SourceModes => sourceModes;

    /// <summary>
    /// Places a mode that already exists in the database, at
    /// <paramref name="version"/>. Distinct from <see cref="Add"/>, which is
    /// the production path for a row being created now.
    /// </summary>
    public void Seed(SourceMode sourceMode, int version = 0)
    {
        Ensure.That(sourceMode).IsNotNull();

        AggregateVersions.SetTo(sourceMode, version);

        sourceModes.Add(sourceMode);
        persisted.Add(sourceMode.Id.Value);
        sourceMode.ClearPendingEvents();
    }

    public Task<Option<SourceMode>> GetAsync(FabIdentifier fab, Source source, CancellationToken cancellationToken)
    {
        SourceMode? found = sourceModes.SingleOrDefault(
            sourceMode => sourceMode.Fab == fab && sourceMode.Source == source);

        return Task.FromResult(found is null
            ? Option<SourceMode>.None
            : Option<SourceMode>.Some(found));
    }

    public void Add(SourceMode sourceMode)
    {
        Ensure.That(sourceMode).IsNotNull();
        sourceModes.Add(sourceMode);
    }

    /// <summary>T016 (spec 317, #2325) — FR-013: undeclared means no row (spec 269 FR-003).</summary>
    public void Remove(SourceMode sourceMode)
    {
        Ensure.That(sourceMode).IsNotNull();
        sourceModes.Remove(sourceMode);
        persisted.Remove(sourceMode.Id.Value);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (SourceMode sourceMode in sourceModes)
        {
            bool wasAlreadyPersisted = !persisted.Add(sourceMode.Id.Value);
            if (wasAlreadyPersisted && sourceMode.PendingEvents.Count > 0)
            {
                AggregateVersions.Bump(sourceMode);
            }

            sourceMode.ClearPendingEvents();
        }

        return Task.CompletedTask;
    }
}
