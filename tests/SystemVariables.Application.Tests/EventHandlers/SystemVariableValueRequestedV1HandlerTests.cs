using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.EventHandlers;

public class SystemVariableValueRequestedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private sealed class FakeDedupStore : IVariableValueRequestDedupStore
    {
        public HashSet<(string, Guid)> Reserved { get; } = new();

        public Task<bool> TryReserveAsync(
            string variableName, Guid causingEventIdentifier, CancellationToken cancellationToken) =>
            Task.FromResult(Reserved.Add((variableName, causingEventIdentifier)));
    }

    private static SetVariableValueCommandHandler BuildSetHandler(InMemoryVariableRepository repo) =>
        new(repo, new FakeClock(Moment),
            NullLogger<SetVariableValueCommandHandler>.Instance);

    [Fact]
    public async Task First_delivery_dispatches_SetVariableValue_against_the_existing_handler()
    {
        InMemoryVariableRepository repo = new();
        Variable oeeLine1 = Variable.Define(
            VariableName.From("oeeLine1"), VariableType.Number, null, null,
            OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Moment));
        repo.Add(oeeLine1);

        FakeDedupStore dedup = new();
        SystemVariableValueRequestedV1Handler handler = new(
            dedup, BuildSetHandler(repo),
            NullLogger<SystemVariableValueRequestedV1Handler>.Instance);

        await handler.Handle(
            new SystemVariableValueRequestedV1("oeeLine1", "82.5", Moment, Guid.CreateVersion7()),
            CancellationToken.None);

        oeeLine1.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(82.5);
    }

    [Fact]
    public async Task Second_delivery_with_the_same_causing_event_is_a_no_op()
    {
        InMemoryVariableRepository repo = new();
        Variable oeeLine1 = Variable.Define(
            VariableName.From("oeeLine1"), VariableType.Number, null, null,
            OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Moment));
        repo.Add(oeeLine1);

        FakeDedupStore dedup = new();
        SystemVariableValueRequestedV1Handler handler = new(
            dedup, BuildSetHandler(repo),
            NullLogger<SystemVariableValueRequestedV1Handler>.Instance);

        Guid causing = Guid.CreateVersion7();
        await handler.Handle(
            new SystemVariableValueRequestedV1("oeeLine1", "82.5", Moment, causing),
            CancellationToken.None);
        await handler.Handle(
            new SystemVariableValueRequestedV1("oeeLine1", "999", Moment, causing),
            CancellationToken.None);

        // Second delivery's value (999) MUST NOT win.
        oeeLine1.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(82.5);
    }

    [Fact]
    public async Task Invalid_variable_name_is_logged_and_dropped()
    {
        InMemoryVariableRepository repo = new();
        FakeDedupStore dedup = new();
        SystemVariableValueRequestedV1Handler handler = new(
            dedup, BuildSetHandler(repo),
            NullLogger<SystemVariableValueRequestedV1Handler>.Instance);

        // The handler should not throw; it logs + returns.
        await handler.Handle(
            new SystemVariableValueRequestedV1("1bad", "1", Moment, Guid.CreateVersion7()),
            CancellationToken.None);

        repo.Variables.ShouldBeEmpty();
    }
}
