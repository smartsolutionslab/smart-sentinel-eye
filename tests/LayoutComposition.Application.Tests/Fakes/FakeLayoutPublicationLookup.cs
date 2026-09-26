using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Stands in for the cross-aggregate read (spec 258 plan.md §4.2) that lets a
/// Wall command handler check candidate scene layouts without loading the
/// <c>Layout</c> aggregate itself (one transaction touches one aggregate).
///
/// <para>
/// <see cref="FabsOf"/> is this test double's own addition, beyond the single
/// <c>PublishedAmong</c> method plan.md §2 names for
/// <c>ILayoutPublicationLookup</c>: create-time validation (FR-001, US1-10)
/// needs to tell "doesn't exist" (<c>WALL_SCENE_NOT_FOUND</c>) apart from
/// "exists in a different fab" (<c>WALL_SCENE_OTHER_FAB</c>), which
/// publishedness alone cannot answer. Flagged here for the backend engineer
/// to confirm or rename.
/// </para>
/// </summary>
public sealed class FakeLayoutPublicationLookup : ILayoutPublicationLookup
{
    private readonly Dictionary<LayoutIdentifier, FabIdentifier> fabsByLayout = [];
    private readonly HashSet<LayoutIdentifier> published = [];

    public FakeLayoutPublicationLookup WithLayout(LayoutIdentifier layout, FabIdentifier fab, bool isPublished = true)
    {
        fabsByLayout[layout] = fab;
        if (isPublished)
        {
            published.Add(layout);
        }
        else
        {
            published.Remove(layout);
        }
        return this;
    }

    public FakeLayoutPublicationLookup Unpublish(LayoutIdentifier layout)
    {
        published.Remove(layout);
        return this;
    }

    public Task<IReadOnlySet<LayoutIdentifier>> PublishedAmong(
        IEnumerable<LayoutIdentifier> candidates, FabIdentifier fab, CancellationToken cancellationToken)
    {
        IReadOnlySet<LayoutIdentifier> result = candidates
            .Where(candidate => published.Contains(candidate) &&
                fabsByLayout.TryGetValue(candidate, out FabIdentifier? actual) && actual == fab)
            .ToHashSet();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<LayoutIdentifier, FabIdentifier>> FabsOf(
        IEnumerable<LayoutIdentifier> candidates, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<LayoutIdentifier, FabIdentifier> result = candidates
            .Where(fabsByLayout.ContainsKey)
            .ToDictionary(candidate => candidate, candidate => fabsByLayout[candidate]);
        return Task.FromResult(result);
    }
}
