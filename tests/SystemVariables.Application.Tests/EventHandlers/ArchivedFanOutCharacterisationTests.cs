using System.Globalization;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Application.Tests.Support;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>VariableArchivedDomainEventHandler.Handle</c> splits
/// its V1 mapping and its snapshot loop into helpers. It pins the published
/// sequence with the generic type of each message, every V1 field and metadata,
/// the V3s' metadata (no acceptance moment, a fresh event identifier each), that
/// the archived name stays literal even when a live variable now holds it, that
/// siblings resolve in the archiving fab only, and the log.
///
/// <para>
/// Counterfactual seen to fail: reusing the V1's metadata local for every V3
/// (one shared event identifier) turns
/// <see cref="Every_message_gets_its_own_event_identifier"/> red.
/// </para>
///
/// <para>
/// Spec 349 section 6 oddity (a) was fixed by spec 358: the count in the log
/// names only the overlays a message was pushed for, not one that was skipped
/// for having no cached label texts.
/// </para>
///
/// <para>
/// Spec 349 section 6 oddity (b) was fixed by spec 358: with no overlay
/// referencing the variable the handler publishes the V1 and logs at debug,
/// as the value-changed handler does.
/// </para>
/// </summary>
public class ArchivedFanOutCharacterisationTests
{
    private static readonly Guid VariableId = Guid.Parse("5e7c1a93-2d48-4b06-a1f7-9c3e0d8b6a25");
    private static readonly Guid ArchivedBy = Guid.Parse("e9f8a7b6-c5d4-4e3f-82a1-b0c9d8e7f6a5");
    private static readonly Guid OverlayA = Guid.Parse("a1a1a1a1-0000-4000-8000-00000000000a");
    private static readonly Guid OverlayB = Guid.Parse("b2b2b2b2-0000-4000-8000-00000000000b");
    private static readonly Guid OverlayC = Guid.Parse("c3c3c3c3-0000-4000-8000-00000000000c");

    private static readonly DateTimeOffset ArchivedAt =
        DateTimeOffset.Parse("2026-06-03T14:20:10Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset DefinedAt =
        DateTimeOffset.Parse("2026-06-01T07:00:00Z", CultureInfo.InvariantCulture);

    private const string FirstLabel = "{{retired}} | {{shift}}";
    private const string SecondLabel = "{{unsetvar}} {{retired}} {{neverDefined}} {{dresdenOnly}}";

    [Fact]
    public async Task The_V1_precedes_the_V3_and_carries_every_field()
    {
        var run = await RunAsync(
            "munich",
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel, SecondLabel],
                [OverlayB] = null,
            });

        run.Bus.Published.Select(published => published.Type).ShouldBe(
            [typeof(SystemVariableArchivedV1), typeof(ResolvedOverlayTextChangedV3)]);

        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableArchivedV1>();
        v1.Variable.ShouldBe(VariableId);
        v1.Name.ShouldBe("retired");
        v1.ArchivedAt.ShouldBe(ArchivedAt);
        v1.ArchivedBy.ShouldBe(ArchivedBy);
        v1.Metadata.EventIdentifier.ShouldNotBe(Guid.Empty);
        v1.Metadata.OccurredAt.ShouldBe(ArchivedAt);
        v1.Metadata.Fab.ShouldBe("munich");
        v1.Metadata.Actor.ShouldBe(ArchivedBy);
        v1.Metadata.RootIngestedAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_archived_and_unset_placeholders_stay_literal_and_the_live_sibling_resolves_in_the_archiving_fab()
    {
        var run = await RunAsync(
            "munich",
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel, SecondLabel],
                [OverlayB] = null,
            });

        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableArchivedV1>();
        var v3 = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        v3.Overlay.ShouldBe(OverlayA);
        v3.Version.ShouldBe(42);
        v3.Texts.Count.ShouldBe(2);
        v3.Texts[0].Template.ShouldBe("{{retired}} | {{shift}}");
        v3.Texts[0].Resolved.ShouldBe("{{retired}} | B-shift");
        v3.Texts[1].Template.ShouldBe("{{unsetvar}} {{retired}} {{neverDefined}} {{dresdenOnly}}");
        v3.Texts[1].Resolved.ShouldBe("{{unsetvar}} {{retired}} {{neverDefined}} {{dresdenOnly}}");
        v3.Metadata.EventIdentifier.ShouldNotBe(Guid.Empty);
        v3.Metadata.EventIdentifier.ShouldNotBe(v1.Metadata.EventIdentifier);
        v3.Metadata.OccurredAt.ShouldBe(ArchivedAt);
        v3.Metadata.Fab.ShouldBe("munich");
        v3.Metadata.Actor.ShouldBe(ArchivedBy);
        v3.Metadata.RootIngestedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Siblings_resolve_in_the_archiving_fab_not_in_another_fab()
    {
        var run = await RunAsync(
            "dresden",
            overlays: [OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?> { [OverlayA] = [FirstLabel, SecondLabel] });

        run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableArchivedV1>()
            .Metadata.Fab.ShouldBe("dresden");
        var v3 = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        v3.Metadata.Fab.ShouldBe("dresden");
        v3.Texts[0].Resolved.ShouldBe("{{retired}} | D-shift");
        v3.Texts[1].Resolved.ShouldBe("{{unsetvar}} {{retired}} {{neverDefined}} elsewhere");
    }

    [Fact]
    public async Task The_versions_are_advanced_once_and_the_log_counts_only_the_pushed_overlay()
    {
        var run = await RunAsync(
            "munich",
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel],
                [OverlayB] = null,
            });

        run.Versions.AdvanceCalls.ShouldHaveSingleItem().ShouldBe([OverlayA, OverlayB]);
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("Pushed ResolvedOverlayTextChanged to 1 overlays after 'retired' was archived.");
    }

    [Fact]
    public async Task Two_labelled_overlays_are_pushed_in_index_order()
    {
        var run = await RunAsync(
            "munich",
            overlays: [OverlayC, OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayC] = ["Shift {{shift}}"],
                [OverlayA] = [FirstLabel],
            });

        run.Bus.Published.Select(published => published.Type).ShouldBe(
            [
                typeof(SystemVariableArchivedV1),
                typeof(ResolvedOverlayTextChangedV3),
                typeof(ResolvedOverlayTextChangedV3),
            ]);
        var first = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        var second = run.Bus.Published[2].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        first.Overlay.ShouldBe(OverlayC);
        first.Version.ShouldBe(8);
        first.Texts.Single().Resolved.ShouldBe("Shift B-shift");
        second.Overlay.ShouldBe(OverlayA);
        second.Version.ShouldBe(42);
        second.Texts.Single().Resolved.ShouldBe("{{retired}} | B-shift");
    }

    [Fact]
    public async Task Every_message_gets_its_own_event_identifier()
    {
        var run = await RunAsync(
            "munich",
            overlays: [OverlayC, OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayC] = ["Shift {{shift}}"],
                [OverlayA] = [FirstLabel],
            });

        var identifiers = run.Bus.Published
            .Select(published => published.Message switch
            {
                SystemVariableArchivedV1 archived => archived.Metadata.EventIdentifier,
                ResolvedOverlayTextChangedV3 changed => changed.Metadata.EventIdentifier,
                _ => Guid.Empty,
            })
            .ToArray();

        identifiers.Length.ShouldBe(3);
        identifiers.ShouldAllBe(identifier => identifier != Guid.Empty);
        identifiers.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task With_no_referencing_overlay_only_the_V1_is_published_and_the_debug_line_is_logged()
    {
        var run = await RunAsync("munich", overlays: [], labels: new Dictionary<Guid, IReadOnlyList<string>?>());

        run.Bus.Published.Select(published => published.Type).ShouldBe([typeof(SystemVariableArchivedV1)]);
        run.Versions.AdvanceCalls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe("No overlays reference variable 'retired'; skipping push fan-out.");
    }

    private static InMemoryVariableRepository SiblingRepository()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(StringVariable("munich", "shift", "B-shift"));
        repo.Add(StringVariable("dresden", "shift", "D-shift"));
        repo.Add(StringVariable("dresden", "dresdenOnly", "elsewhere"));
        // A live variable re-using the archived name: the archive must not
        // resolve it, or the placeholder would not revert to its literal.
        repo.Add(StringVariable("munich", "retired", "ALIVE"));
        repo.Add(StringVariable("dresden", "retired", "ALIVE-D"));
        repo.Add(new VariableBuilder()
            .WithFab("munich")
            .Named("unsetvar")
            .OfType(VariableType.String)
            .At(DefinedAt)
            .Build());
        repo.Add(new VariableBuilder()
            .WithFab("dresden")
            .Named("unsetvar")
            .OfType(VariableType.String)
            .At(DefinedAt)
            .Build());

        return repo;
    }

    private static Variable StringVariable(string fab, string name, string value) =>
        new VariableBuilder()
            .WithFab(fab)
            .Named(name)
            .OfType(VariableType.String)
            .WithInitialValue(new VariableValue.StringValue(value))
            .At(DefinedAt)
            .Build();

    private static async Task<Run> RunAsync(
        string fab,
        IReadOnlyList<Guid> overlays,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>?> labels)
    {
        TypeRecordingEventBus bus = new();
        FakeOverlayTextVersions versions = new();
        versions.Seed(OverlayA, 41);
        versions.Seed(OverlayC, 7);
        CapturingLogger<VariableArchivedDomainEventHandler> logger = new();
        VariableArchivedDomainEventHandler handler = new(
            bus,
            new ListedReverseIndex(overlays, labels),
            versions,
            SiblingRepository(),
            new Resolver(),
            logger);

        await handler.Handle(
            new VariableArchivedDomainEvent(
                VariableIdentifier.From(VariableId),
                FabIdentifier.From(fab),
                VariableName.From("retired"),
                ArchivedAt,
                OperatorIdentifier.From(ArchivedBy)),
            CancellationToken.None);

        return new Run(bus, versions, logger);
    }

    private sealed record Run(
        TypeRecordingEventBus Bus,
        FakeOverlayTextVersions Versions,
        CapturingLogger<VariableArchivedDomainEventHandler> Logger);
}
