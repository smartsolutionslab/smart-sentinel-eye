using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay.Builders;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Commands;

/// <summary>
/// Characterisation (spec 350, ADR-0144 phase 4a: behaviour-preserving, so
/// green on the unrefactored handler and green unmodified after). Pins the
/// order in which <see cref="BranchDraftRevisionCommandHandler"/> refuses, the
/// repository calls it makes on the way, and its log text.
///
/// <para>
/// Counterfactual this file was seen to catch: moving the stale-version check
/// below the no-Published block turns the open-draft-and-stale case from
/// OVERLAY_REVISION_STALE into OVERLAY_NO_PUBLISHED_REVISION.
/// </para>
///
/// <para>
/// Every expected message is a hand-written template in which only the
/// fixture identifier is interpolated. None is built with the
/// <c>BranchDraftRevisionFailures</c> factories under test.
/// </para>
/// </summary>
public class BranchDraftRefusalOrderCharacterisationTests
{
    private const int StaleExpectedVersion = 7;

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Publisher =
        OperatorIdentifier.From(Guid.Parse("11111111-2222-7333-8444-555555555555"));

    private static readonly OperatorIdentifier Brancher =
        OperatorIdentifier.From(Guid.Parse("aaaaaaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));

    private readonly FakeClock clock = new(FixedMoment);
    private readonly InMemoryOverlayRepository store = new();
    private readonly RecordingOverlayRepository repository;
    private readonly CapturingLogger<BranchDraftRevisionCommandHandler> logger = new();

    public BranchDraftRefusalOrderCharacterisationTests()
    {
        repository = new RecordingOverlayRepository(store);
    }

    [Fact]
    public async Task An_unknown_overlay_is_not_found_after_one_identifier_lookup()
    {
        var unknown = OverlayIdentifier.From(Guid.Parse("5e0f0c3a-1b2c-7d3e-8f40-516273849a0b"));

        var result = await Branch(unknown, StaleExpectedVersion);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_NOT_FOUND");
        result.Error.Message.ShouldBe("Overlay 5e0f0c3a-1b2c-7d3e-8f40-516273849a0b does not exist.");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_open_draft_with_a_stale_version_answers_stale_not_no_published_revision()
    {
        var overlay = AddOverlay("Line-1");

        var result = await Branch(overlay.Id, StaleExpectedVersion);

        AssertStale(result, overlay);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fully_archived_chain_whose_name_is_held_and_whose_version_is_stale_answers_stale_without_a_name_lookup()
    {
        var stranded = AddFullyArchived("Line-2");
        AddOverlay("Line-2");

        var result = await Branch(stranded.Id, StaleExpectedVersion);

        AssertStale(result, stranded);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_open_draft_at_the_right_version_names_that_draft_and_looks_up_no_name()
    {
        var overlay = AddOverlay("Line-3");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.BranchDraft(Publisher, clock);
        overlay.Publish(OverlayRevisionNumber.From(2), Publisher, clock);
        overlay.Revert(OverlayRevisionNumber.From(2), Publisher, clock);

        var result = await Branch(overlay.Id, 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_NO_PUBLISHED_REVISION");
        result.Error.Message.ShouldBe(
            $"Overlay {overlay.Id.Value} already has a Draft revision 2. Edit that draft rather than branching another.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fully_archived_chain_whose_name_another_overlay_holds_is_refused_after_one_name_lookup_and_not_saved()
    {
        var stranded = AddFullyArchived("Line-4");
        AddOverlay("Line-4");

        var result = await Branch(stranded.Id, 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_NAME_TAKEN");
        result.Error.Message.ShouldBe(
            $"Overlay {stranded.Id.Value} cannot be recovered: the name 'Line-4' is now used by another overlay.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        AssertCalls("get-by-identifier", "get-by-name");
        stranded.Revisions.Count.ShouldBe(1);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fully_archived_chain_whose_name_is_free_is_recovered_after_the_name_lookup()
    {
        var stranded = AddFullyArchived("Line-5");

        var result = await Branch(stranded.Id, 0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        AssertCalls("get-by-identifier", "get-by-name", "save");
        stranded.Revisions.Count.ShouldBe(2);
        AssertSingleLog($"Branched draft revision 2 on overlay {stranded.Id.Value} by {Brancher.Value}.");
    }

    [Fact]
    public async Task A_healthy_published_chain_is_branched_without_a_name_lookup_and_saved_once()
    {
        var overlay = AddOverlay("Line-6");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);

        var result = await Branch(overlay.Id, 0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        AssertCalls("get-by-identifier", "save");
        overlay.Revisions.Count.ShouldBe(2);
        AssertSingleLog($"Branched draft revision 2 on overlay {overlay.Id.Value} by {Brancher.Value}.");
    }

    [Fact]
    public async Task A_published_chain_with_two_revisions_logs_the_newly_branched_number_not_the_first()
    {
        var overlay = AddOverlay("Line-7");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.BranchDraft(Publisher, clock);
        overlay.Publish(OverlayRevisionNumber.From(2), Publisher, clock);

        var result = await Branch(overlay.Id, 0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(3);
        AssertCalls("get-by-identifier", "save");
        AssertSingleLog($"Branched draft revision 3 on overlay {overlay.Id.Value} by {Brancher.Value}.");
    }

    [Fact]
    public async Task A_published_chain_with_a_stale_version_answers_stale_and_is_not_saved()
    {
        var overlay = AddOverlay("Line-8");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);

        var result = await Branch(overlay.Id, StaleExpectedVersion);

        AssertStale(result, overlay);
        AssertCalls("get-by-identifier");
        overlay.Revisions.Count.ShouldBe(1);
        logger.Entries.ShouldBeEmpty();
    }

    private async Task<Result<OverlayRevisionNumber, BranchDraftRevisionError>> Branch(
        OverlayIdentifier overlay,
        int expectedVersion)
    {
        BranchDraftRevisionCommandHandler handler = new(repository, clock, logger);
        var result = await handler.HandleAsync(
            new BranchDraftRevisionCommand(overlay, Brancher, expectedVersion),
            CancellationToken.None);
        return result;
    }

    private Overlay AddOverlay(string name)
    {
        var overlay = new OverlayBuilder().At(FixedMoment).Named(name).Build();
        store.Add(overlay);
        return overlay;
    }

    private Overlay AddFullyArchived(string name)
    {
        var overlay = AddOverlay(name);
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, Publisher, clock);
        return overlay;
    }

    private static void AssertStale(Result<OverlayRevisionNumber, BranchDraftRevisionError> result, Overlay overlay)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_STALE");
        result.Error.Message.ShouldBe(
            $"Overlay {overlay.Id.Value} has changed since version 7 (now 0). Re-read it and reapply the change.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private void AssertCalls(params string[] expected) =>
        repository.Calls.ShouldBe(expected);

    private void AssertSingleLog(string message)
    {
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(message);
    }
}
