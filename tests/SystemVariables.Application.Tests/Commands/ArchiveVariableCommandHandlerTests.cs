using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

public class ArchiveVariableCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Archiving_a_defined_variable_transitions_state_and_releases_the_name()
    {
        InMemoryVariableRepository repo = new();
        VariableBuilder builder = new VariableBuilder().Named("oeeLine1").OfType(VariableType.Number);
        Variable v = builder.Build();
        repo.Add(v);

        ArchiveVariableCommandHandler handler = new(
            repo, builder.Clock, NullLogger<ArchiveVariableCommandHandler>.Instance);
        Result<VariableIdentifier, ArchiveVariableError> result = await handler.HandleAsync(
            new ArchiveVariableCommand(
                VariableName.From("oeeLine1"),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        v.State.ShouldBe(VariableState.Archived);
    }

    [Fact]
    public async Task Archiving_an_unknown_variable_returns_VariableNotFound()
    {
        InMemoryVariableRepository repo = new();
        ArchiveVariableCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<ArchiveVariableCommandHandler>.Instance);

        Result<VariableIdentifier, ArchiveVariableError> result = await handler.HandleAsync(
            new ArchiveVariableCommand(
                VariableName.From("ghost"),
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ArchiveVariableError.VariableNotFound>();
    }
}
