using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Cross-aggregate read (spec 258 plan.md §4.2) that lets a Wall command
/// handler check candidate scene layouts without loading the
/// <see cref="Layout.Layout"/> aggregate itself — one transaction touches
/// one aggregate.
/// </summary>
public interface ILayoutPublicationLookup
{
    /// <summary>
    /// Which of the candidate layouts currently have a Published revision
    /// <b>in the given fab</b> (PD-6). Used to compute the <c>publishable</c>
    /// set <see cref="Wall.SwitchTo"/> takes, and to validate a create/edit's
    /// scene set.
    /// </summary>
    Task<IReadOnlySet<LayoutIdentifier>> PublishedAmong(
        IEnumerable<LayoutIdentifier> candidates, FabIdentifier fab, CancellationToken cancellationToken);

    /// <summary>
    /// The fab each candidate layout actually belongs to (only entries that
    /// exist at all). Publishedness alone cannot tell "doesn't exist"
    /// (<c>WALL_SCENE_NOT_FOUND</c>) apart from "exists in a different fab"
    /// (<c>WALL_SCENE_OTHER_FAB</c>) — this does (US1-10).
    /// </summary>
    Task<IReadOnlyDictionary<LayoutIdentifier, FabIdentifier>> FabsOf(
        IEnumerable<LayoutIdentifier> candidates, CancellationToken cancellationToken);
}
