using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class GetOverlaySnapshotQueryHandlerTests
{
    [Fact]
    public async Task Returns_OverlayNotInReverseIndex_when_the_overlay_has_no_published_revision()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        FakeOverlayTextVersions versions = new();
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver(), versions);

        Guid overlay = Guid.CreateVersion7();
        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetOverlaySnapshotError.OverlayNotInReverseIndex>();

        // #2426 SC-5 -- an unknown overlay must not create a counter row: the
        // handler returns before ever touching the version store.
        versions.AdvanceCalls.ShouldBeEmpty();
        versions.CurrentAsyncCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_the_resolved_label_and_current_version_when_the_overlay_is_indexed()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        repo.Add(new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build());

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");

        // #2426 -- the version comes from the durable store, not the reverse
        // index. Seeded above the cutover floor, a value the retired
        // in-memory counter could never produce, so a handler that still read
        // IReverseIndex for its version would fail this assertion rather than
        // pass it by coincidence.
        FakeOverlayTextVersions versions = new();
        versions.Seed(overlay, FakeOverlayTextVersions.Floor + 3);

        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver(), versions);

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OverlayIdentifier.ShouldBe(overlay);
        result.Value.ResolvedText.ShouldBe("OEE: 82.5%");
        result.Value.Version.ShouldBe(FakeOverlayTextVersions.Floor + 3);
    }

    [Fact]
    public async Task Skips_archived_and_unset_variables_so_they_render_as_literal_placeholders()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        // 'shift' is defined but unset → renders as literal.
        repo.Add(new VariableBuilder().Named("shift").OfType(VariableType.String).Build());

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "{{shift}} - {{unknown}}");

        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver(), new FakeOverlayTextVersions());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResolvedText.ShouldBe("{{shift}} - {{unknown}}");
    }

    // ---- spec 014 T037 (as amended by ADR-0115): the viewer's fab ----

    [Fact]
    public async Task An_overlay_resolves_the_viewers_fab_not_another()
    {
        // The same overlay, rendered for two different plants. This is the
        // whole point of ADR-0115: one template, per-fab values.
        Guid overlay = Guid.CreateVersion7();
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());
        repo.Add(new VariableBuilder().WithFab("dresden").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(7)).Build());

        InMemoryReverseIndex index = new();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver(), new FakeOverlayTextVersions());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> munich = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);
        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> dresden = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("dresden")], overlay), CancellationToken.None);

        munich.Value.ResolvedText.ShouldBe("OEE: 41%");
        // The assertion that matters: asserting munich alone would pass just as
        // well if resolution were still global.
        dresden.Value.ResolvedText.ShouldBe("OEE: 7%");
    }

    [Fact]
    public async Task A_variable_absent_from_the_viewers_fab_renders_the_literal_placeholder()
    {
        // Identical to a name that exists nowhere, per the contract.
        Guid overlay = Guid.CreateVersion7();
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());

        InMemoryReverseIndex index = new();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver(), new FakeOverlayTextVersions());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("dresden")], overlay), CancellationToken.None);

        result.Value.ResolvedText.ShouldBe("OEE: {{oeeLine1}}%");
    }

    // ---- #2426 (spec 202) Finding B / SC-7 -- version read before text resolved ----

    [Fact]
    public async Task Reads_the_version_before_resolving_the_text()
    {
        List<string> callOrder = [];

        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build());

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");

        FakeOverlayTextVersions versions = new() { CallOrder = callOrder };
        versions.Seed(overlay, FakeOverlayTextVersions.Floor);

        GetOverlaySnapshotQueryHandler handler = new(index, repo, new RecordingResolver(callOrder), versions);

        await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

        // plan.md §5 (Finding B): a version read before the text is a lower
        // bound on the text's freshness. Read after, a push committing
        // between the two lines stamps stale text with the newer push's
        // version, and the kiosk drops that push as not-newer -- permanently,
        // since nothing else ever tells it to re-fetch (plan.md §1, direction
        // 3). Asserted on the fakes' own recorded call order, not a comment.
        callOrder.ShouldBe(["VersionRead", "TextResolved"]);
    }

    /// <summary>
    /// Wraps the real <see cref="Resolver"/> and records <c>"TextResolved"</c>
    /// into the shared order list after resolving, so
    /// <see cref="Reads_the_version_before_resolving_the_text"/> can assert on
    /// the merged sequence both fakes recorded, rather than on a comment.
    /// </summary>
    private sealed class RecordingResolver(List<string> callOrder) : IResolver
    {
        private readonly Resolver inner = new();

        public string Resolve(string labelText, IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot)
        {
            string resolved = inner.Resolve(labelText, snapshot);
            callOrder.Add("TextResolved");
            return resolved;
        }
    }
}
