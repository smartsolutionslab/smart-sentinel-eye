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
/// <c>develop</c> before <c>VariableValueChangedDomainEventHandler</c> moves its
/// V1 mapping and name parse into helpers. What it pins is everything the
/// existing tests leave to the reader's eye: the exact sequence of published
/// messages with the generic type each was published under, every field of the
/// V1 and of each V3, the metadata of each (the V1 carries no acceptance
/// moment, the V3s do, and no two messages share an event identifier), the
/// single version advance and the log line.
///
/// <para>
/// Counterfactual seen to fail: passing <c>accepted</c> as the fifth argument of
/// the V1's <c>EventMetadata</c> turns
/// <see cref="The_V1_precedes_the_V3_and_carries_every_field_with_no_acceptance_moment"/>
/// red.
/// </para>
///
/// <para>
/// Spec 349 section 6 oddity (a) is pinned as it is: the count in the log
/// includes an overlay that was skipped for having no cached label texts.
/// </para>
/// </summary>
public class ValueChangedFanOutCharacterisationTests
{
    private static readonly Guid VariableId = Guid.Parse("0f3a9c21-7b64-4d85-9e10-5a2c8b7d3e46");
    private static readonly Guid ChangedBy = Guid.Parse("d1e2f3a4-b5c6-4d7e-8f90-a1b2c3d4e5f6");
    private static readonly Guid OverlayA = Guid.Parse("a1a1a1a1-0000-4000-8000-00000000000a");
    private static readonly Guid OverlayB = Guid.Parse("b2b2b2b2-0000-4000-8000-00000000000b");
    private static readonly Guid OverlayC = Guid.Parse("c3c3c3c3-0000-4000-8000-00000000000c");

    private static readonly DateTimeOffset ChangedAt =
        DateTimeOffset.Parse("2026-06-02T10:15:30Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset RootIngestedAt =
        DateTimeOffset.Parse("2026-06-02T10:15:29.400Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset DefinedAt =
        DateTimeOffset.Parse("2026-06-01T07:00:00Z", CultureInfo.InvariantCulture);

    private const string FirstLabel = "OEE {{oeeLine1}}% on {{shift}}";
    private const string SecondLabel = "{{unsetvar}} / {{retired}} / {{dresdenOnly}} / {{neverDefined}}";

    [Fact]
    public async Task The_V1_precedes_the_V3_and_carries_every_field_with_no_acceptance_moment()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.Some(RootIngestedAt)),
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel, SecondLabel],
                [OverlayB] = null,
            });

        run.Bus.Published.Select(published => published.Type).ShouldBe(
            [typeof(SystemVariableValueChangedV1), typeof(ResolvedOverlayTextChangedV3)]);

        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>();
        v1.Variable.ShouldBe(VariableId);
        v1.Name.ShouldBe("oeeLine1");
        v1.Type.ShouldBe("Number");
        v1.Value.ShouldBe("82.5");
        v1.ChangedAt.ShouldBe(ChangedAt);
        v1.ChangedBy.ShouldBe(ChangedBy);
        v1.Metadata.EventIdentifier.ShouldNotBe(Guid.Empty);
        v1.Metadata.OccurredAt.ShouldBe(ChangedAt);
        v1.Metadata.Fab.ShouldBe("munich");
        v1.Metadata.Actor.ShouldBe(ChangedBy);
        v1.Metadata.RootIngestedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_V3_carries_the_overlay_its_resolved_texts_its_advanced_version_and_the_acceptance_moment()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.Some(RootIngestedAt)),
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel, SecondLabel, FirstLabel, string.Empty],
                [OverlayB] = null,
            });

        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>();
        var v3 = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        v3.Overlay.ShouldBe(OverlayA);
        v3.Version.ShouldBe(42);
        v3.Texts.Count.ShouldBe(2);
        v3.Texts[0].Template.ShouldBe("OEE {{oeeLine1}}% on {{shift}}");
        v3.Texts[0].Resolved.ShouldBe("OEE 82.5% on B-shift");
        v3.Texts[1].Template.ShouldBe("{{unsetvar}} / {{retired}} / {{dresdenOnly}} / {{neverDefined}}");
        v3.Texts[1].Resolved.ShouldBe("{{unsetvar}} / {{retired}} / {{dresdenOnly}} / {{neverDefined}}");
        v3.Metadata.EventIdentifier.ShouldNotBe(Guid.Empty);
        v3.Metadata.EventIdentifier.ShouldNotBe(v1.Metadata.EventIdentifier);
        v3.Metadata.OccurredAt.ShouldBe(ChangedAt);
        v3.Metadata.Fab.ShouldBe("munich");
        v3.Metadata.Actor.ShouldBe(ChangedBy);
        v3.Metadata.RootIngestedAt.ShouldBe(RootIngestedAt);
    }

    [Fact]
    public async Task The_versions_are_advanced_once_for_both_listed_overlays_and_the_log_counts_the_skipped_one()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.Some(RootIngestedAt)),
            overlays: [OverlayA, OverlayB],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayA] = [FirstLabel],
                [OverlayB] = null,
            });

        run.Versions.AdvanceCalls.ShouldHaveSingleItem().ShouldBe([OverlayA, OverlayB]);
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("Pushed ResolvedOverlayTextChanged to 2 overlays after 'oeeLine1' changed.");
    }

    [Fact]
    public async Task Two_labelled_overlays_are_pushed_in_index_order_each_with_its_own_event_identifier()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.Some(RootIngestedAt)),
            overlays: [OverlayC, OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>
            {
                [OverlayC] = ["Line {{shift}}: {{oeeLine1}}"],
                [OverlayA] = [FirstLabel],
            });

        run.Bus.Published.Select(published => published.Type).ShouldBe(
            [
                typeof(SystemVariableValueChangedV1),
                typeof(ResolvedOverlayTextChangedV3),
                typeof(ResolvedOverlayTextChangedV3),
            ]);
        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>();
        var first = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        var second = run.Bus.Published[2].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();

        first.Overlay.ShouldBe(OverlayC);
        first.Version.ShouldBe(8);
        first.Texts.Single().Template.ShouldBe("Line {{shift}}: {{oeeLine1}}");
        first.Texts.Single().Resolved.ShouldBe("Line B-shift: 82.5");
        second.Overlay.ShouldBe(OverlayA);
        second.Version.ShouldBe(42);
        second.Texts.Single().Resolved.ShouldBe("OEE 82.5% on B-shift");

        first.Metadata.EventIdentifier.ShouldNotBe(second.Metadata.EventIdentifier);
        first.Metadata.EventIdentifier.ShouldNotBe(v1.Metadata.EventIdentifier);
        second.Metadata.EventIdentifier.ShouldNotBe(v1.Metadata.EventIdentifier);
        run.Logger.Entries.ShouldHaveSingleItem().Message
            .ShouldBe("Pushed ResolvedOverlayTextChanged to 2 overlays after 'oeeLine1' changed.");
    }

    [Fact]
    public async Task With_no_acceptance_moment_the_V3_carries_none()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.None),
            overlays: [OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?> { [OverlayA] = [FirstLabel] });

        run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>()
            .Metadata.RootIngestedAt.ShouldBeNull();
        run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>()
            .Metadata.RootIngestedAt.ShouldBeNull();
    }

    [Fact]
    public async Task With_no_referencing_overlay_only_the_V1_is_published_and_the_debug_line_is_logged()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(82.5), Option<DateTimeOffset>.Some(RootIngestedAt)),
            overlays: [],
            labels: new Dictionary<Guid, IReadOnlyList<string>?>());

        run.Bus.Published.Select(published => published.Type).ShouldBe([typeof(SystemVariableValueChangedV1)]);
        run.Versions.AdvanceCalls.ShouldBeEmpty();
        var entry = run.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe("No overlays reference variable 'oeeLine1'; skipping push fan-out.");
    }

    [Fact]
    public async Task A_boolean_change_publishes_its_wire_value_and_resolves_with_the_event_labels()
    {
        var run = await RunAsync(
            Changed(
                "munich",
                "gateOpen",
                VariableType.Boolean,
                new VariableValue.BooleanValue(true),
                Option<DateTimeOffset>.None,
                BooleanLabels.From("OPEN", "SHUT")),
            overlays: [OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?> { [OverlayA] = ["Gate {{gateOpen}}"] });

        var v1 = run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>();
        v1.Name.ShouldBe("gateOpen");
        v1.Type.ShouldBe("Boolean");
        v1.Value.ShouldBe("true");
        var v3 = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        v3.Texts.Single().Resolved.ShouldBe("Gate OPEN");
    }

    [Fact]
    public async Task An_unset_change_publishes_an_empty_wire_value_and_leaves_its_placeholder_literal()
    {
        var run = await RunAsync(
            Changed("munich", "oeeLine1", VariableType.Number, VariableValue.Unset.Instance, Option<DateTimeOffset>.None),
            overlays: [OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?> { [OverlayA] = [FirstLabel] });

        run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>()
            .Value.ShouldBe(string.Empty);
        run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>()
            .Texts.Single().Resolved.ShouldBe("OEE {{oeeLine1}}% on B-shift");
    }

    [Fact]
    public async Task Siblings_resolve_in_the_fab_that_changed_not_in_another_fab()
    {
        var run = await RunAsync(
            Changed("dresden", "oeeLine1", VariableType.Number, new VariableValue.NumberValue(61), Option<DateTimeOffset>.None),
            overlays: [OverlayA],
            labels: new Dictionary<Guid, IReadOnlyList<string>?> { [OverlayA] = [FirstLabel] });

        run.Bus.Published[0].Message.ShouldBeOfType<SystemVariableValueChangedV1>()
            .Metadata.Fab.ShouldBe("dresden");
        var v3 = run.Bus.Published[1].Message.ShouldBeOfType<ResolvedOverlayTextChangedV3>();
        v3.Metadata.Fab.ShouldBe("dresden");
        v3.Texts.Single().Resolved.ShouldBe("OEE 61% on D-shift");
    }

    private static VariableValueChangedDomainEvent Changed(
        string fab,
        string name,
        VariableType type,
        VariableValue value,
        Option<DateTimeOffset> rootIngestedAt,
        BooleanLabels? booleanLabels = null) =>
        new(
            VariableIdentifier.From(VariableId),
            FabIdentifier.From(fab),
            VariableName.From(name),
            type,
            value,
            ChangedAt,
            OperatorIdentifier.From(ChangedBy),
            booleanLabels,
            rootIngestedAt);

    private static InMemoryVariableRepository SiblingRepository()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(StringVariable("munich", "shift", "B-shift"));
        repo.Add(StringVariable("dresden", "shift", "D-shift"));
        repo.Add(StringVariable("dresden", "dresdenOnly", "elsewhere"));
        repo.Add(StringVariable("munich", "oeeLine1", "stale"));
        repo.Add(new VariableBuilder()
            .WithFab("munich")
            .Named("unsetvar")
            .OfType(VariableType.String)
            .At(DefinedAt)
            .Build());

        var retired = new VariableBuilder()
            .WithFab("munich")
            .Named("retired")
            .OfType(VariableType.String)
            .WithInitialValue(new VariableValue.StringValue("was-here"))
            .At(DefinedAt)
            .Build();
        retired.Archive(OperatorIdentifier.From(ChangedBy), new FakeClock(DefinedAt));
        repo.Add(retired);

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
        VariableValueChangedDomainEvent domainEvent,
        IReadOnlyList<Guid> overlays,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>?> labels)
    {
        TypeRecordingEventBus bus = new();
        FakeOverlayTextVersions versions = new();
        versions.Seed(OverlayA, 41);
        versions.Seed(OverlayC, 7);
        CapturingLogger<VariableValueChangedDomainEventHandler> logger = new();
        VariableValueChangedDomainEventHandler handler = new(
            bus,
            new ListedReverseIndex(overlays, labels),
            versions,
            SiblingRepository(),
            new Resolver(),
            logger);

        await handler.Handle(domainEvent, CancellationToken.None);

        return new Run(bus, versions, logger);
    }

    private sealed record Run(
        TypeRecordingEventBus Bus,
        FakeOverlayTextVersions Versions,
        CapturingLogger<VariableValueChangedDomainEventHandler> Logger);
}
