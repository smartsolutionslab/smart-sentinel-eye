using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.Commands;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Commands;

public class BranchDraftRevisionCommandHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Branching_off_a_Published_revision_mints_revision_N_plus_1()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout layout = new LayoutBuilder().At(FixedMoment).Build();
        layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        layouts.Add(layout);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], layout.Id, OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        layout.Revisions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_layout_returns_LayoutNotFound()
    {
        InMemoryLayoutRepository layouts = new();
        BranchDraftRevisionCommandHandler handler = new(
            layouts, new FakeClock(FixedMoment), NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], LayoutIdentifier.New(), OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.LayoutNotFound>();
    }

    [Fact]
    public async Task Chain_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        Layout draftOnly = new LayoutBuilder().At(FixedMoment).Build();
        layouts.Add(draftOnly);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], draftOnly.Id, OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.NoPublishedRevisionToBranchFrom>();
    }

    /// <summary>
    /// Spec 037 FR-001 (ADR-0121), asserted at the layer an API caller meets.
    /// The handler refused before the domain was ever reached, so the domain
    /// fallback alone changed nothing observable — this is the assertion that
    /// says the recovery is actually reachable.
    /// </summary>
    [Fact]
    public async Task A_fully_archived_chain_is_recovered_rather_than_refused()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Layout stranded = new LayoutBuilder().At(FixedMoment).Build();
        stranded.Publish(LayoutRevisionNumber.One, by, clock);
        stranded.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layouts.Add(stranded);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], stranded.Id, by, 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
    }

    /// <summary>
    /// Spec 037 FR-009. A chain becomes recoverable exactly when every revision
    /// is Archived, which is exactly when its name is released — so between
    /// archiving and recovering, another layout may legitimately have taken it.
    /// Recovering anyway would leave two live layouts sharing a name in one fab,
    /// which nothing downstream catches: uniqueness is checked only on create
    /// and <c>ix_layouts_fab_name</c> is not unique.
    /// </summary>
    [Fact]
    public async Task Recovering_a_chain_whose_name_was_taken_meanwhile_returns_LayoutNameTaken()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Layout stranded = new LayoutBuilder().Named("rolling-mill").At(FixedMoment).Build();
        stranded.Publish(LayoutRevisionNumber.One, by, clock);
        stranded.ArchiveRevision(LayoutRevisionNumber.One, by, clock);
        layouts.Add(stranded);

        // Legitimate today: the stranded chain released the name when its last
        // revision was archived.
        layouts.Add(new LayoutBuilder().Named("rolling-mill").At(FixedMoment).Build());

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], stranded.Id, by, 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.LayoutNameTaken>();
        stranded.Revisions.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 037 FR-008, and the assertion that looks like padding and is not.
    ///
    /// <para>
    /// FR-009's name check is correct only inside the recovery branch. Hoisted
    /// onto the Published path — which reads like the more thorough choice — a
    /// live chain becomes visible to its own name lookup, matches itself, and
    /// every branch of every healthy layout is refused. The refusal test above
    /// would still pass; only this one fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_healthy_published_chain_is_not_refused_for_holding_its_own_name()
    {
        InMemoryLayoutRepository layouts = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Layout live = new LayoutBuilder().Named("rolling-mill").At(FixedMoment).Build();
        live.Publish(LayoutRevisionNumber.One, by, clock);
        layouts.Add(live);

        BranchDraftRevisionCommandHandler handler = new(
            layouts, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<LayoutRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand([Munich], live.Id, by, 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
    }
}
