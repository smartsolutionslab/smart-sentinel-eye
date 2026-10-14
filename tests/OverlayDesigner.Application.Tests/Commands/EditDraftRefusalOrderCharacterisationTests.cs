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
/// order in which <see cref="EditDraftRevisionCommandHandler"/> refuses, the
/// repository calls it makes on the way, and its log text.
///
/// <para>
/// Counterfactual this file was seen to catch: moving
/// <c>Overlay.ValidateElements</c> below the overlay lookup turns the
/// empty-set-for-an-unknown-overlay case from OVERLAY_ELEMENTS_EMPTY (400,
/// zero repository calls) into OVERLAY_NOT_FOUND.
/// </para>
///
/// <para>
/// The element set is validated BEFORE the overlay is looked up, so an empty or
/// over-long set for an unknown overlay answers 400, not 404. Pinned as it is
/// (spec 350 section 6, oddity 1), not asserted as desirable.
/// </para>
///
/// <para>
/// Every expected message is a hand-written template in which only the fixture
/// identifier is interpolated, never built with the <c>EditDraftRevisionFailures</c>
/// factories under test.
/// </para>
/// </summary>
public class EditDraftRefusalOrderCharacterisationTests
{
    private const int StaleExpectedVersion = 7;

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Publisher =
        OperatorIdentifier.From(Guid.Parse("11111111-2222-7333-8444-555555555555"));

    private readonly FakeClock clock = new(FixedMoment);
    private readonly InMemoryOverlayRepository store = new();
    private readonly RecordingOverlayRepository repository;
    private readonly CapturingLogger<EditDraftRevisionCommandHandler> logger = new();

    public EditDraftRefusalOrderCharacterisationTests()
    {
        repository = new RecordingOverlayRepository(store);
    }

    [Fact]
    public async Task An_empty_set_for_an_unknown_overlay_answers_empty_with_no_repository_call()
    {
        var unknown = OverlayIdentifier.From(Guid.Parse("5e0f0c3a-1b2c-7d3e-8f40-516273849a0b"));

        var result = await Edit(unknown, 1, [], StaleExpectedVersion);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_ELEMENTS_EMPTY");
        result.Error.Message.ShouldBe("A revision must carry at least one element.");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        repository.Calls.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nine_elements_for_an_unknown_overlay_answer_too_many_with_no_repository_call()
    {
        var unknown = OverlayIdentifier.From(Guid.Parse("5e0f0c3a-1b2c-7d3e-8f40-516273849a0b"));
        var nine = Enumerable.Range(0, 9).Select(_ => Replacement("Nine")).ToList();

        var result = await Edit(unknown, 1, nine, StaleExpectedVersion);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_ELEMENTS_TOO_MANY");
        result.Error.Message.ShouldBe("A revision may carry at most 8 elements; 9 were submitted.");
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
        repository.Calls.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_overlay_with_a_valid_set_is_not_found_after_one_identifier_lookup()
    {
        var unknown = OverlayIdentifier.From(Guid.Parse("5e0f0c3a-1b2c-7d3e-8f40-516273849a0b"));

        var result = await Edit(unknown, 1, [Replacement("Valid")], StaleExpectedVersion);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_NOT_FOUND");
        result.Error.Message.ShouldBe("Overlay 5e0f0c3a-1b2c-7d3e-8f40-516273849a0b does not exist.");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stale_version_and_a_missing_revision_answers_stale()
    {
        var overlay = AddOverlay("Line-1");

        var result = await Edit(overlay.Id, 42, [Replacement("Stale")], StaleExpectedVersion);

        AssertStale(result, overlay);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stale_version_and_a_published_revision_answers_stale()
    {
        var overlay = AddOverlay("Line-2");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);

        var result = await Edit(overlay.Id, 1, [Replacement("Stale")], StaleExpectedVersion);

        AssertStale(result, overlay);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_revision_at_the_right_version_is_refused_by_number_and_not_saved()
    {
        var overlay = AddOverlay("Line-3");

        var result = await Edit(overlay.Id, 42, [Replacement("Missing")], 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_NOT_FOUND");
        result.Error.Message.ShouldBe($"Overlay {overlay.Id.Value} has no revision 42.");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_published_revision_is_refused_with_its_state_and_left_unchanged()
    {
        var overlay = AddOverlay("Line-4");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);

        var result = await Edit(overlay.Id, 1, [Replacement("Published")], 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_NOT_DRAFT");
        result.Error.Message.ShouldBe("Revision is in state 'Published'; only Draft revisions can be edited in place.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        AssertCalls("get-by-identifier");
        overlay.Revisions.Single().Elements.Single().Text!.Value.ShouldBe("Production Line 1");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_archived_revision_is_refused_with_its_own_state()
    {
        var overlay = AddOverlay("Line-5");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.ArchiveRevision(OverlayRevisionNumber.One, Publisher, clock);

        var result = await Edit(overlay.Id, 1, [Replacement("Archived")], 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_NOT_DRAFT");
        result.Error.Message.ShouldBe("Revision is in state 'Archived'; only Draft revisions can be edited in place.");
        AssertCalls("get-by-identifier");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_revision_is_chosen_by_number_so_the_published_first_revision_is_refused_while_the_second_is_a_draft()
    {
        var overlay = AddOverlay("Line-6");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.BranchDraft(Publisher, clock);

        var result = await Edit(overlay.Id, 1, [Replacement("First")], 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_NOT_DRAFT");
        result.Error.Message.ShouldBe("Revision is in state 'Published'; only Draft revisions can be edited in place.");
        AssertCalls("get-by-identifier");
    }

    [Fact]
    public async Task Editing_the_second_revision_of_a_branched_chain_replaces_its_elements_and_logs_that_number()
    {
        var overlay = AddOverlay("Line-7");
        overlay.Publish(OverlayRevisionNumber.One, Publisher, clock);
        overlay.BranchDraft(Publisher, clock);

        var result = await Edit(overlay.Id, 2, [Replacement("Second")], 0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(2);
        AssertCalls("get-by-identifier", "save");
        overlay.Revisions.Single(revision => revision.Number == OverlayRevisionNumber.From(2))
            .Elements.Single().Text!.Value.ShouldBe("Second");
        overlay.Revisions.Single(revision => revision.Number == OverlayRevisionNumber.One)
            .Elements.Single().Text!.Value.ShouldBe("Production Line 1");
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe($"Edited draft revision 2 on overlay {overlay.Id.Value}.");
    }

    private static OverlayElement Replacement(string text) =>
        OverlayElement.TextElement(
            text,
            40,
            NormalizedPosition.From(0.2m, 0.3m),
            NormalizedSize.From(0.4m, 0.5m),
            OverlayColor.Default);

    private async Task<Result<OverlayRevisionNumber, EditDraftRevisionError>> Edit(
        OverlayIdentifier overlay,
        int revision,
        IReadOnlyList<OverlayElement> elements,
        int expectedVersion)
    {
        EditDraftRevisionCommandHandler handler = new(repository, clock, logger);
        var result = await handler.HandleAsync(
            new EditDraftRevisionCommand(overlay, OverlayRevisionNumber.From(revision), elements, expectedVersion),
            CancellationToken.None);
        return result;
    }

    private Overlay AddOverlay(string name)
    {
        var overlay = new OverlayBuilder().At(FixedMoment).Named(name).Build();
        store.Add(overlay);
        return overlay;
    }

    private static void AssertStale(Result<OverlayRevisionNumber, EditDraftRevisionError> result, Overlay overlay)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OVERLAY_REVISION_STALE");
        result.Error.Message.ShouldBe(
            $"Overlay {overlay.Id.Value} has changed since version 7 (now 0). Re-read it and reapply the change.");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    private void AssertCalls(params string[] expected) =>
        repository.Calls.ShouldBe(expected);
}
