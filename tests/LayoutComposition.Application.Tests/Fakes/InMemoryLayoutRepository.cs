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
    private readonly List<Layout> _layouts = new();

    public IReadOnlyList<Layout> Layouts => _layouts;

    public Task<Option<Layout>> GetByIdentifierAsync(LayoutIdentifier layout, CancellationToken cancellationToken)
    {
        Layout? found = _layouts.SingleOrDefault(candidate => candidate.Id == layout);
        return Task.FromResult(found is null ? Option<Layout>.None : Option<Layout>.Some(found));
    }

    public Task<Option<Layout>> GetByNameAsync(LayoutName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Layout? found = _layouts.SingleOrDefault(candidate =>
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
