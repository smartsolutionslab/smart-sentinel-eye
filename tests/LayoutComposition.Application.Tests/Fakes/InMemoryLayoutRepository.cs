using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ILayoutRepository"/> for handler tests.
/// SaveAsync clears pending events to mimic the real
/// dispatcher-after-Save flow.
/// </summary>
public sealed class InMemoryLayoutRepository : ILayoutRepository
{
    private readonly List<Layout> _layouts = [];

    public IReadOnlyList<Layout> Layouts => _layouts;

    public Task<Option<Layout>> GetByIdentifierAsync(
        IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fabs);
        // Fab as part of the lookup, mirroring the real repository (FR-006).
        Layout? found = _layouts.SingleOrDefault(
            candidate => candidate.Id == layout && fabs.Contains(candidate.Fab));
        return Task.FromResult(found is null ? Option<Layout>.None : Option<Layout>.Some(found));
    }

    public Task<Option<Layout>> GetByNameAsync(
        FabIdentifier fab, LayoutName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fab);
        ArgumentNullException.ThrowIfNull(name);
        // Fab first, mirroring the real repository: a name is unique only
        // within one (spec 017 FR-019).
        Layout? found = _layouts.SingleOrDefault(candidate =>
            candidate.Fab == fab &&
            candidate.Name == name &&
            candidate.Revisions.Any(r => r.State != LayoutRevisionState.Archived));
        return Task.FromResult(found is null ? Option<Layout>.None : Option<Layout>.Some(found));
    }

    public void Add(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layouts.Add(layout);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (Layout layout in _layouts)
        {
            layout.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
