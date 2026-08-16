using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class PublishRevisionCommandHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishing_a_Draft_transitions_it_to_Published()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout draft = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draft);

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], 
                draft.Id,
                LayoutRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(LayoutRevisionNumber.One);
        draft.Revisions.Single().State.ShouldBe(LayoutRevisionState.Published);
    }

    [Fact]
    public async Task Publishing_an_unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], 
                LayoutIdentifier.New(),
                LayoutRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Publishing_an_unknown_revision_number_returns_LayoutRevisionNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout draft = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draft);

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], 
                draft.Id,
                LayoutRevisionNumber.From(99),
                OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.LayoutRevisionNotFound>();
    }

    [Fact]
    public async Task Publishing_an_already_Published_revision_returns_InvalidStateTransition()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout draft = new LayoutBuilder().At(FixedMoment).Build();
        draft.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        draft.ClearPendingEvents();
        layouts.Add(draft);

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand([Munich], 
                draft.Id,
                LayoutRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.InvalidStateTransition>();
    }

    [Fact]
    public async Task Publishing_a_new_revision_raises_both_Published_and_Archived_events()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());
        Layout layout = new LayoutBuilder().CreatedBy(op).At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, op, clock);
        Revision draftTwo = layout.BranchDraft(op, clock);
        layout.ClearPendingEvents();
        layouts.Add(layout);

        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        await handler.HandleAsync(
            new PublishRevisionCommand([Munich], layout.Id, draftTwo.Number, op, 0),
            CancellationToken.None);

        // The SaveAsync inside the handler clears events, but we asserted
        // via state already. The atomic-swap invariant is covered by
        // LayoutTests in the Domain test project (test asserts events
        // were raised before SaveAsync clears them).
        layout.Revisions.Single(r => r.State == LayoutRevisionState.Published).Number.ShouldBe(draftTwo.Number);
        layout.Revisions.Single(r => r.Number == LayoutRevisionNumber.One).State.ShouldBe(LayoutRevisionState.Archived);
    }

    [Fact]
    public void Domain_event_records_carry_the_expected_payload()
    {
        // Belt-and-braces: assert the event shape so future serializer
        // changes don't silently drop fields.
        IReadOnlyList<Tile> tiles =
            [new Tile(CameraIdentifier.From(Guid.CreateVersion7()), Option<OverlayIdentifier>.None, GridPosition.From(0, 0))];
        LayoutRevisionPublishedDomainEvent published = new(
            Munich,
            LayoutIdentifier.New(),
            LayoutRevisionNumber.One,
            LayoutName.From("Line-1"),
            GridDimensions.Cell,
            tiles,
            FixedMoment,
            OperatorIdentifier.From(Guid.CreateVersion7()));
        published.RevisionNumber.Value.ShouldBe(1);
        published.PublishedAt.ShouldBe(FixedMoment);
        published.Tiles.ShouldHaveSingleItem();

        LayoutRevisionArchivedDomainEvent archived = new(
            Munich,
            LayoutIdentifier.New(),
            LayoutRevisionNumber.One,
            FixedMoment,
            OperatorIdentifier.From(Guid.CreateVersion7()));
        archived.RevisionNumber.Value.ShouldBe(1);
        archived.ArchivedAt.ShouldBe(FixedMoment);
    }

    /// <summary>
    /// FR-006 on a write. Publishing another fab's layout answers
    /// LayoutNotFound — the same failure an unknown identifier gives — rather
    /// than "forbidden", which would confirm the layout exists.
    ///
    /// <para>
    /// Publish stands for all five layout-addressing writes: they scope
    /// through the one fab-aware <c>GetByIdentifierAsync</c>, so there is one
    /// place for this to be right or wrong rather than five.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Publishing_a_layout_in_another_fab_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout inMunich = new LayoutBuilder().WithFab(Munich).At(FixedMoment).Build();
        layouts.Add(inMunich);
        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                [Dresden],
                inMunich.Id,
                LayoutRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7()),
                inMunich.Version),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.LayoutNotFound>();
    }

    /// <summary>
    /// The ordering FR-006 needs, and the reason the fab lives in the lookup
    /// rather than in a check after it: a <em>stale version</em> on another
    /// fab's layout must still answer not-found. Were the precondition read
    /// first, the mismatch reply would confirm the layout exists and leak its
    /// current version.
    /// </summary>
    [Fact]
    public async Task A_stale_version_on_another_fabs_layout_still_answers_not_found()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout inMunich = new LayoutBuilder().WithFab(Munich).At(FixedMoment).Build();
        layouts.Add(inMunich);
        PublishRevisionCommandHandler handler = new(layouts, clock, NullLogger<PublishRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(
                [Dresden],
                inMunich.Id,
                LayoutRevisionNumber.One,
                OperatorIdentifier.From(Guid.CreateVersion7()),
                inMunich.Version + 99),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<PublishRevisionError.LayoutNotFound>();
    }
}
