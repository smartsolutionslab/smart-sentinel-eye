using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class VariableValueChangedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishes_V1_event_and_a_resolved_text_event_for_each_affected_overlay()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        FakeOverlayTextVersions versions = new();

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, versions, repo, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        VariableIdentifier id = VariableIdentifier.New();
        await handler.Handle(
            new VariableValueChangedDomainEvent(
                id, FabIdentifier.From("munich"), VariableName.From("oeeLine1"), VariableType.Number,
                new VariableValue.NumberValue(82.5), FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()), BooleanLabels: null, RootIngestedAt: Option<DateTimeOffset>.None),
            CancellationToken.None);

        SystemVariableValueChangedV1 v1 = bus.Published.OfType<SystemVariableValueChangedV1>()
            .ShouldHaveSingleItem();
        v1.Name.ShouldBe("oeeLine1");
        v1.Value.ShouldBe("82.5");

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();
        push.Overlay.ShouldBe(overlay);
        push.ResolvedText.ShouldBe("OEE: 82.5%");
        // #2426 -- the version comes from the durable store (a first-ever
        // advance returns the cutover floor, never 1), not from
        // IReverseIndex. A handler that still read the reverse index's
        // retired counter would push 1 here and fail this assertion.
        push.Version.ShouldBe(FakeOverlayTextVersions.Floor);
        // The fab decides which plant's wall the push reaches (ADR-0115). Asserting
        // the value, not its presence: a null here is what the consumer drops.
        push.Metadata.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task With_no_referencing_overlays_only_publishes_V1_and_does_not_push()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        FakeOverlayTextVersions versions = new();

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, versions, repo, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableValueChangedDomainEvent(
                VariableIdentifier.New(), FabIdentifier.From("munich"), VariableName.From("orphan"),
                VariableType.String, new VariableValue.StringValue("v"), FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()), BooleanLabels: null, RootIngestedAt: Option<DateTimeOffset>.None),
            CancellationToken.None);

        bus.Published.OfType<SystemVariableValueChangedV1>().ShouldHaveSingleItem();
        bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldBeEmpty();

        // No overlay references the variable, so there is nothing to advance.
        versions.AdvanceCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// #2426 (spec 202) plan.md §2 -- "one round trip for the whole fan-out".
    /// A push per affected overlay must not become an advance per affected
    /// overlay: that would put a database statement per overlay on the
    /// `event → overlay state` leg, linear in fan-out width rather than
    /// constant (constitution §IV, 200 ms budget).
    /// </summary>
    [Fact]
    public async Task A_fan_out_over_several_overlays_advances_the_version_store_exactly_once()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        FakeOverlayTextVersions versions = new();

        Guid overlayA = Guid.CreateVersion7();
        Guid overlayB = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlayA, "A: {{oeeLine1}}%");
        index.UpsertOverlayReferences(overlayB, "B: {{oeeLine1}}%");

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, versions, repo, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableValueChangedDomainEvent(
                VariableIdentifier.New(), FabIdentifier.From("munich"), VariableName.From("oeeLine1"),
                VariableType.Number, new VariableValue.NumberValue(82.5), FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()), BooleanLabels: null, RootIngestedAt: Option<DateTimeOffset>.None),
            CancellationToken.None);

        versions.AdvanceCalls.Count.ShouldBe(
            1, "a fan-out over N affected overlays must advance the store once, not N times");
        versions.AdvanceCalls[0].ShouldBe([overlayA, overlayB], ignoreOrder: true);

        ResolvedOverlayTextChangedV1[] pushes = [.. bus.Published.OfType<ResolvedOverlayTextChangedV1>()];
        pushes.Length.ShouldBe(2);
        pushes.ShouldAllBe(push => push.Version == FakeOverlayTextVersions.Floor);
    }
}
