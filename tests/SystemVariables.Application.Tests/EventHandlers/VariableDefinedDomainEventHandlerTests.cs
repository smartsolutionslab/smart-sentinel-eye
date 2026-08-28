using System.Globalization;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class VariableDefinedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishes_the_V1_defined_event()
    {
        FakeEventBus bus = new();
        VariableDefinedDomainEventHandler handler = new(bus);

        VariableIdentifier id = VariableIdentifier.New();
        OperatorIdentifier definedBy = OperatorIdentifier.From(Guid.CreateVersion7());

        await handler.Handle(
            new VariableDefinedDomainEvent(
                id,
                FabIdentifier.From("munich"),
                VariableName.From("oeeLine1"),
                VariableType.Number,
                FixedMoment,
                definedBy),
            CancellationToken.None);

        SystemVariableDefinedV1 v1 = bus.Published.OfType<SystemVariableDefinedV1>().ShouldHaveSingleItem();
        v1.Variable.ShouldBe(id.Value);
        v1.Name.ShouldBe("oeeLine1");
        v1.Type.ShouldBe("Number");
        v1.DefinedAt.ShouldBe(FixedMoment);
        v1.DefinedBy.ShouldBe(definedBy.Value);
    }

    // The audit row pivots on the envelope, not the payload: without the fab
    // and actor here the trail records that *a* variable was defined without
    // saying where or by whom, which is most of what an audit row is for.
    [Fact]
    public async Task Stamps_the_fab_and_the_actor_on_the_event_metadata()
    {
        FakeEventBus bus = new();
        VariableDefinedDomainEventHandler handler = new(bus);

        OperatorIdentifier definedBy = OperatorIdentifier.From(Guid.CreateVersion7());

        await handler.Handle(
            new VariableDefinedDomainEvent(
                VariableIdentifier.New(),
                FabIdentifier.From("dresden"),
                VariableName.From("shift"),
                VariableType.String,
                FixedMoment,
                definedBy),
            CancellationToken.None);

        SystemVariableDefinedV1 v1 = bus.Published.OfType<SystemVariableDefinedV1>().ShouldHaveSingleItem();
        v1.Metadata.Fab.ShouldBe("dresden");
        v1.Metadata.Actor.ShouldBe(definedBy.Value);
        v1.Metadata.OccurredAt.ShouldBe(FixedMoment);
        v1.Metadata.EventIdentifier.ShouldNotBe(Guid.Empty);
    }

    // Defining a variable is not a value change: nothing already on screen
    // moves, so this handler must publish exactly one event. Its two siblings
    // both fan out to ResolvedOverlayTextChangedV1 and copying either of them
    // wholesale would push a redundant frame at every kiosk.
    [Fact]
    public async Task Does_not_fan_out_to_overlay_resolution()
    {
        FakeEventBus bus = new();
        VariableDefinedDomainEventHandler handler = new(bus);

        await handler.Handle(
            new VariableDefinedDomainEvent(
                VariableIdentifier.New(),
                FabIdentifier.From("munich"),
                VariableName.From("oeeLine1"),
                VariableType.Number,
                FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        bus.Published.ShouldHaveSingleItem().ShouldBeOfType<SystemVariableDefinedV1>();
    }
}
