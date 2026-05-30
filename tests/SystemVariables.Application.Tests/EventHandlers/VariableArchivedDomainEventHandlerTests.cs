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

public class VariableArchivedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishes_V1_archived_event_with_the_variable_name()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        VariableArchivedDomainEventHandler handler = new(
            bus, index, repo, new Resolver(),
            NullLogger<VariableArchivedDomainEventHandler>.Instance);

        VariableIdentifier id = VariableIdentifier.New();
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());

        await handler.Handle(
            new VariableArchivedDomainEvent(id, VariableName.From("oeeLine1"), FixedMoment, op),
            CancellationToken.None);

        SystemVariableArchivedV1 v1 = bus.Published.OfType<SystemVariableArchivedV1>()
            .ShouldHaveSingleItem();
        v1.Name.ShouldBe("oeeLine1");
        v1.Variable.ShouldBe(id.Value);
    }

    [Fact]
    public async Task Re_resolves_each_affected_overlay_with_the_archived_variable_reverted_to_literal()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        // Sibling 'shift' stays Defined+set; 'oeeLine1' is the one being archived.
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier definer = OperatorIdentifier.From(Guid.CreateVersion7());
        Variable shift = Variable.Define(
            VariableName.From("shift"), VariableType.String,
            new VariableValue.StringValue("A"), null, definer, clock);
        repo.Add(shift);

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "{{shift}} - OEE: {{oeeLine1}}%");

        VariableArchivedDomainEventHandler handler = new(
            bus, index, repo, new Resolver(),
            NullLogger<VariableArchivedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableArchivedDomainEvent(
                VariableIdentifier.New(), VariableName.From("oeeLine1"),
                FixedMoment, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();
        push.Overlay.ShouldBe(overlay);
        // 'shift' renders, 'oeeLine1' reverts to its literal placeholder.
        push.ResolvedText.ShouldBe("A - OEE: {{oeeLine1}}%");
    }

    [Fact]
    public async Task Skips_archived_and_unset_siblings_when_building_the_snapshot()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        FakeClock clock = new(FixedMoment);
        OperatorIdentifier definer = OperatorIdentifier.From(Guid.CreateVersion7());

        // 'shift' is Defined but Unset → should be skipped (renders as literal).
        Variable shift = Variable.Define(
            VariableName.From("shift"), VariableType.String, null, null, definer, clock);
        repo.Add(shift);

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "{{shift}}-{{target}}");

        VariableArchivedDomainEventHandler handler = new(
            bus, index, repo, new Resolver(),
            NullLogger<VariableArchivedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableArchivedDomainEvent(
                VariableIdentifier.New(), VariableName.From("target"),
                FixedMoment, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();
        // Both placeholders revert to literal: shift is Unset, target is the one archived.
        push.ResolvedText.ShouldBe("{{shift}}-{{target}}");
    }

    [Fact]
    public async Task With_no_referencing_overlays_only_publishes_V1_and_does_not_push()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        VariableArchivedDomainEventHandler handler = new(
            bus, index, repo, new Resolver(),
            NullLogger<VariableArchivedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableArchivedDomainEvent(
                VariableIdentifier.New(), VariableName.From("orphan"),
                FixedMoment, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        bus.Published.OfType<SystemVariableArchivedV1>().ShouldHaveSingleItem();
        bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldBeEmpty();
    }
}
