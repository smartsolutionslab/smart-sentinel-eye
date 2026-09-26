using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// Cross-aggregate read for candidate scene layouts (spec 258 plan.md §4.2):
/// one query over <c>layouts</c> joined to its owned
/// <c>layout_revisions</c>, filtered to <c>state = Published</c> and the
/// requested identifiers — never loads the <see cref="Layout"/> aggregate
/// for a write.
/// </summary>
public sealed class LayoutPublicationLookup(LayoutCompositionDbContext dbContext) : ILayoutPublicationLookup
{
    public async Task<IReadOnlySet<LayoutIdentifier>> PublishedAmong(
        IEnumerable<LayoutIdentifier> candidates, FabIdentifier fab, CancellationToken cancellationToken)
    {
        Ensure.That(candidates).IsNotNull();
        Ensure.That(fab).IsNotNull();

        LayoutIdentifier[] ids = [.. candidates];
        List<LayoutIdentifier> published = await dbContext.Layouts
            .AsNoTracking()
            .Where(layout => ids.Contains(layout.Id) && layout.Fab == fab)
            .Where(layout => layout.Revisions.Any(revision => revision.State == LayoutRevisionState.Published))
            .Select(layout => layout.Id)
            .ToListAsync(cancellationToken);

        return published.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<LayoutIdentifier, FabIdentifier>> FabsOf(
        IEnumerable<LayoutIdentifier> candidates, CancellationToken cancellationToken)
    {
        Ensure.That(candidates).IsNotNull();

        LayoutIdentifier[] ids = [.. candidates];
        List<Layout> found = await dbContext.Layouts
            .AsNoTracking()
            .Where(layout => ids.Contains(layout.Id))
            .ToListAsync(cancellationToken);

        return found.ToDictionary(layout => layout.Id, layout => layout.Fab);
    }
}
