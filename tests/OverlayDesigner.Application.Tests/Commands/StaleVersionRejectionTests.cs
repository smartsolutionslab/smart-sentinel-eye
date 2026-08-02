using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1 for OverlayDesigner, mirroring the LayoutComposition
/// suite. Each test also asserts the aggregate was left alone — the check is
/// only worth having if it runs *before* the mutation, and a handler that
/// rejected afterwards would return the right error while corrupting state.
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publish_rejects_a_stale_version_and_leaves_the_revision_in_Draft()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();

        PublishRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(overlay.Id, OverlayRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task Archive_rejects_a_stale_version_and_leaves_the_revision_in_Draft()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();

        ArchiveRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<ArchiveRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler.HandleAsync(
            new ArchiveRevisionCommand(overlay.Id, OverlayRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Draft);
    }

    [Fact]
    public async Task BranchDraft_rejects_a_stale_version_and_adds_no_revision()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();
        overlay.Publish(OverlayRevisionNumber.One, Editor(), clock);

        BranchDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<BranchDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(overlay.Id, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        overlay.Revisions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Revert_rejects_a_stale_version_and_leaves_the_revision_Published()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();
        overlay.Publish(OverlayRevisionNumber.One, Editor(), clock);

        RevertRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<RevertRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler.HandleAsync(
            new RevertRevisionCommand(overlay.Id, OverlayRevisionNumber.One, Editor(), Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        overlay.Revisions.Single().State.ShouldBe(OverlayRevisionState.Published);
    }

    [Fact]
    public async Task EditDraft_rejects_a_stale_version_and_leaves_the_label_untouched()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();
        string originalText = overlay.Revisions.Single().Label.Text;

        EditDraftRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<EditDraftRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler.HandleAsync(
            new EditDraftRevisionCommand(
                overlay.Id,
                OverlayRevisionNumber.One,
                Label.From("Replaced", 0.2m, 0.2m, 0.4m, 0.1m, 64),
                Stale),
            CancellationToken.None);

        ShouldBeStale(result.IsFailure, result.Error);
        overlay.Revisions.Single().Label.Text.ShouldBe(originalText);
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryOverlayRepository overlays, FakeClock clock, Overlay overlay) = Seeded();

        PublishRevisionCommandHandler handler = new(
            overlays, clock, NullLogger<PublishRevisionCommandHandler>.Instance);
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler.HandleAsync(
            new PublishRevisionCommand(overlay.Id, OverlayRevisionNumber.One, Editor(), overlay.Version),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    private static void ShouldBeStale(bool isFailure, ApiError error)
    {
        isFailure.ShouldBeTrue();
        error.Code.ShouldBe("OVERLAY_REVISION_STALE");
    }

    private static OperatorIdentifier Editor() => OperatorIdentifier.From(Guid.CreateVersion7());

    private static (InMemoryOverlayRepository, FakeClock, Overlay) Seeded()
    {
        InMemoryOverlayRepository overlays = new();
        FakeClock clock = new(FixedMoment);
        Overlay overlay = new OverlayBuilder().At(clock.UtcNow).Build();
        overlays.Add(overlay);

        return (overlays, clock, overlay);
    }
}
