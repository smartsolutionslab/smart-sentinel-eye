using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

public class BranchDraftRevisionCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Branching_off_Published_yields_revision_two_in_Draft()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(Label.From("Hello", NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.3m, 0.08m), 32))
            .Build();
        overlays.Add(overlay);
        overlay.Publish(OverlayRevisionNumber.One, OperatorIdentifier.From(Guid.CreateVersion7()), clock);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                overlay.Id, OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        overlay.Revisions.Single(r => r.Number == OverlayRevisionNumber.From(2)).State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task Unknown_overlay_returns_OverlayNotFound()
    {
        InMemoryOverlayRepository overlays = new();
        BranchDraftRevisionCommandHandler handler = new(
            overlays, new FakeClock(FixedMoment), NullLogger<BranchDraftRevisionCommandHandler>.Instance);

        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                OverlayIdentifier.New(), OperatorIdentifier.From(Guid.CreateVersion7()), 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.OverlayNotFound>();
    }

    [Fact]
    public async Task Branching_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder()
            .At(clock.UtcNow)
            .Named("Line-1")
            .WithLabel(Label.From("Hello", NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.3m, 0.08m), 32))
            .Build();
        overlays.Add(overlay);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(
                overlay.Id, OperatorIdentifier.From(Guid.CreateVersion7()), 0),
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
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Overlay stranded = new OverlayBuilder().At(clock.UtcNow).Named("Line-1").Build();
        overlays.Add(stranded);
        stranded.Publish(OverlayRevisionNumber.One, by, clock);
        stranded.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(stranded.Id, by, 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
    }

    /// <summary>
    /// Spec 037 FR-009. A chain becomes recoverable exactly when every revision
    /// is Archived, which is exactly when its name is released — so between
    /// archiving and recovering, another overlay may legitimately have taken it.
    /// Recovering anyway would leave two live overlays sharing a name, which
    /// nothing downstream catches: uniqueness is checked only on create and
    /// <c>ix_overlays_name</c> is not unique.
    /// </summary>
    [Fact]
    public async Task Recovering_a_chain_whose_name_was_taken_meanwhile_returns_OverlayNameTaken()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Overlay stranded = new OverlayBuilder().At(clock.UtcNow).Named("Line-1").Build();
        overlays.Add(stranded);
        stranded.Publish(OverlayRevisionNumber.One, by, clock);
        stranded.ArchiveRevision(OverlayRevisionNumber.One, by, clock);

        // Legitimate today: the stranded chain released the name when its last
        // revision was archived.
        overlays.Add(new OverlayBuilder().At(clock.UtcNow).Named("Line-1").Build());

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(stranded.Id, by, 0),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<BranchDraftRevisionError.OverlayNameTaken>();
        stranded.Revisions.Count.ShouldBe(1);
    }

    /// <summary>
    /// Spec 037 FR-008, and the assertion that looks like padding and is not.
    ///
    /// <para>
    /// FR-009's name check is correct only inside the recovery branch. Hoisted
    /// onto the Published path — which reads like the more thorough choice — a
    /// live chain becomes visible to its own name lookup, matches itself, and
    /// every branch of every healthy overlay is refused. The refusal test above
    /// would still pass; only this one fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_healthy_published_chain_is_not_refused_for_holding_its_own_name()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        Overlay live = new OverlayBuilder().At(clock.UtcNow).Named("Line-1").Build();
        overlays.Add(live);
        live.Publish(OverlayRevisionNumber.One, by, clock);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(live.Id, by, 0),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
    }
}
