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
        IResolver resolver = new Resolver();

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, repo, resolver,
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        VariableIdentifier id = VariableIdentifier.New();
        await handler.Handle(
            new VariableValueChangedDomainEvent(
                id, VariableName.From("oeeLine1"), VariableType.Number,
                new VariableValue.NumberValue(82.5), FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()), BooleanLabels: null),
            CancellationToken.None);

        SystemVariableValueChangedV1 v1 = bus.Published.OfType<SystemVariableValueChangedV1>()
            .ShouldHaveSingleItem();
        v1.Name.ShouldBe("oeeLine1");
        v1.Value.ShouldBe("82.5");

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();
        push.Overlay.ShouldBe(overlay);
        push.ResolvedText.ShouldBe("OEE: 82.5%");
        push.Version.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_referencing_overlays_only_publishes_V1_and_does_not_push()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, repo, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableValueChangedDomainEvent(
                VariableIdentifier.New(), VariableName.From("orphan"),
                VariableType.String, new VariableValue.StringValue("v"), FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()), BooleanLabels: null),
            CancellationToken.None);

        bus.Published.OfType<SystemVariableValueChangedV1>().ShouldHaveSingleItem();
        bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldBeEmpty();
    }
}
