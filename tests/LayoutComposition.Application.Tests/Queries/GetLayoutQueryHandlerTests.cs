using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Application.Queries;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Queries;

public class GetLayoutQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task Existing_layout_is_mapped_into_a_LayoutDto_with_ordered_revisions()
    {
        InMemoryLayoutRepository repository = new();
        LayoutBuilder builder = new LayoutBuilder().Named("Line-1").At(FixedMoment);
        OperatorIdentifier op = builder.Operator;
        IClock clock = builder.Clock;
        Layout layout = builder.Build();
        layout.Publish(LayoutRevisionNumber.One, op, clock);
        Revision branched = layout.BranchDraft(op, clock);
        repository.Add(layout);

        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery([Munich], layout.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        LayoutDto dto = result.Value;
        dto.LayoutIdentifier.ShouldBe(layout.Id.Value);
        dto.Name.ShouldBe("Line-1");
        dto.Revisions.Count.ShouldBe(2);
        dto.Revisions[0].RevisionNumber.ShouldBe(1);
        dto.Revisions[1].RevisionNumber.ShouldBe(branched.Number.Value);
        LayoutRevisionDto first = dto.Revisions[0];
        first.GridRows.ShouldBe(1);
        first.GridCols.ShouldBe(1);
        first.Tiles.ShouldHaveSingleItem().Row.ShouldBe(0);
    }

    // The version has to reach the read side or a caller has nothing to put in
    // If-Match, and the cross-request check silently degrades to no check at
    // all (ADR-0113 Layer 1).
    [Fact]
    public async Task The_dto_carries_the_aggregate_version()
    {
        InMemoryLayoutRepository repository = new();
        Layout layout = new LayoutBuilder().Named("Line-2").At(FixedMoment).Build();
        repository.Add(layout);

        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));
        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery([Munich], layout.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(layout.Version);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository repository = new();
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery([Munich], LayoutIdentifier.New()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetLayoutError.LayoutNotFound>();
    }

    /// <summary>
    /// FR-006. Not forbidden — the same failure an unknown identifier
    /// produces, so the caller cannot learn the layout exists. A distinguishable
    /// refusal lets an operator enumerate another plant's layouts one guess at
    /// a time.
    /// </summary>
    [Fact]
    public async Task A_layout_in_a_fab_the_caller_does_not_hold_is_reported_as_not_found()
    {
        InMemoryLayoutRepository repository = new();
        Layout inMunich = new LayoutBuilder().WithFab(Munich).Named("Line-1").At(FixedMoment).Build();
        repository.Add(inMunich);
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery([Dresden], inMunich.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetLayoutError.LayoutNotFound>();
    }

    [Fact]
    public async Task A_multi_fab_caller_reaches_a_layout_in_either_of_their_fabs()
    {
        InMemoryLayoutRepository repository = new();
        Layout inDresden = new LayoutBuilder().WithFab(Dresden).Named("Line-1").At(FixedMoment).Build();
        repository.Add(inDresden);
        GetLayoutQueryHandler handler = new(new InMemoryLayoutQuerySource(repository));

        Result<LayoutDto, GetLayoutError> result = await handler.HandleAsync(
            new GetLayoutQuery([Munich, Dresden], inDresden.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fab.ShouldBe("dresden");
    }
}
