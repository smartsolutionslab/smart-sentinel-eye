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

/// <summary>
/// Spec 021 T017. Domain-event handlers now run <b>before</b> their write is
/// committed, so the message can be captured inside the transaction. Eleven of
/// the twelve handlers publish and nothing else, and are indifferent to when
/// they run. This one reads, so it is the only place that ordering change could
/// be wrong — and "it should be fine" is not a reason to skip it.
///
/// <para>
/// The claim: the value this handler resolves comes from the domain event, not
/// from a query, so whether the write has been committed yet cannot change what
/// it renders.
/// </para>
/// </summary>
public class VariableValueChangedPreCommitTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-08-19T10:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// The repository is deliberately holding the <i>old</i> value — which is
    /// exactly what it holds pre-commit, because the new one has not been
    /// written yet. If the handler resolved from storage, this would render the
    /// stale number and every kiosk would show it.
    /// </summary>
    [Fact]
    public async Task The_changed_value_is_taken_from_the_event_not_from_uncommitted_storage()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repository = new();
        FixedClock clock = new();

        // Committed state: 40.0. The write in flight changes it to 82.5 and has
        // not been committed, so this is what a query would return right now.
        repository.Add(Variable.Define(
            FabIdentifier.From("munich"),
            VariableName.From("oeeLine1"),
            VariableType.Number,
            new VariableValue.NumberValue(40.0),
            booleanLabels: null,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock));

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, repository, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableValueChangedDomainEvent(
                VariableIdentifier.New(),
                FabIdentifier.From("munich"),
                VariableName.From("oeeLine1"),
                VariableType.Number,
                new VariableValue.NumberValue(82.5),
                FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()),
                BooleanLabels: null),
            CancellationToken.None);

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();

        push.ResolvedText.ShouldBe(
            "OEE: 82.5%",
            "the handler resolved the changed variable from storage, so running it "
            + "before the commit renders the value the write is replacing");
    }

    /// <summary>
    /// The other half. A <i>sibling</i> variable in the same label is not
    /// written by this transaction, so it is committed and reads normally —
    /// which is why the ordering change is safe rather than merely untested.
    /// </summary>
    [Fact]
    public async Task A_sibling_variable_still_resolves_from_storage()
    {
        FakeEventBus bus = new();
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repository = new();
        FixedClock clock = new();

        repository.Add(Variable.Define(
            FabIdentifier.From("munich"),
            VariableName.From("shift"),
            VariableType.String,
            new VariableValue.StringValue("Nights"),
            booleanLabels: null,
            OperatorIdentifier.From(Guid.CreateVersion7()),
            clock));

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "{{shift}} — OEE {{oeeLine1}}%");

        VariableValueChangedDomainEventHandler handler = new(
            bus, index, repository, new Resolver(),
            NullLogger<VariableValueChangedDomainEventHandler>.Instance);

        await handler.Handle(
            new VariableValueChangedDomainEvent(
                VariableIdentifier.New(),
                FabIdentifier.From("munich"),
                VariableName.From("oeeLine1"),
                VariableType.Number,
                new VariableValue.NumberValue(82.5),
                FixedMoment,
                OperatorIdentifier.From(Guid.CreateVersion7()),
                BooleanLabels: null),
            CancellationToken.None);

        ResolvedOverlayTextChangedV1 push =
            bus.Published.OfType<ResolvedOverlayTextChangedV1>().ShouldHaveSingleItem();

        push.ResolvedText.ShouldBe("Nights — OEE 82.5%");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => FixedMoment;
    }
}
