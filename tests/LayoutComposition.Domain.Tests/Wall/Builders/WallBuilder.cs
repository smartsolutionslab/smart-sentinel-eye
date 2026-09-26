using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall.Builders;

/// <summary>
/// Fluent builder for <c>Wall</c> aggregates in tests (ADR-0054, spec 258
/// plan.md §2). Sensible default: the smallest legal scene set (PD-5,
/// MinScenes = 2), two freshly-minted <see cref="LayoutIdentifier"/>s.
///
/// <para>
/// Reuses <see cref="LayoutBuilder.TestClock"/> rather than declaring a
/// second one — both aggregates live in the same Domain test project and a
/// second copy would be free to drift from the first (CLAUDE.md's "smallest
/// possible change" + this repo's own "grep the invariant half" lesson).
/// </para>
/// </summary>
public sealed class WallBuilder
{
    private FabIdentifier fab = FabIdentifier.From("munich");
    private Domain.Wall.WallName name = Domain.Wall.WallName.From("Line 3 rotation");
    private IReadOnlyList<LayoutIdentifier> scenes = [LayoutIdentifier.New(), LayoutIdentifier.New()];
    private OperatorIdentifier createdBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock clock = new LayoutBuilder.TestClock(
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture));

    public WallBuilder WithFab(FabIdentifier value)
    {
        fab = value;
        return this;
    }

    public WallBuilder Named(string value)
    {
        name = Domain.Wall.WallName.From(value);
        return this;
    }

    public WallBuilder WithScenes(IReadOnlyList<LayoutIdentifier> value)
    {
        scenes = value;
        return this;
    }

    public WallBuilder CreatedBy(OperatorIdentifier value)
    {
        createdBy = value;
        return this;
    }

    public WallBuilder At(DateTimeOffset moment)
    {
        clock = new LayoutBuilder.TestClock(moment);
        return this;
    }

    public Domain.Wall.Wall Build() => Domain.Wall.Wall.Create(fab, name, scenes, createdBy, clock);

    public IClock Clock => clock;

    public OperatorIdentifier Operator => createdBy;

    public IReadOnlyList<LayoutIdentifier> Scenes => scenes;

    public FabIdentifier Fab => fab;
}
