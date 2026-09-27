using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// Phase 4a (spec 269 T003e). Exercises the declare/change command handlers
/// against the signatures T002 introduced with their bodies withheld.
///
/// <para>
/// Not every case here is red on arrival. T002's change handler always
/// answers <c>SourceModeNotDeclared</c> (tasks.md's own prelude spec), which
/// is also the correct answer for "an undeclared pair" and "a stale version
/// against an undeclared pair" — two of the seven cases below pass
/// immediately as a direct, unavoidable consequence of that canned answer,
/// not because the lookup or the version gate exist yet. Named individually
/// at each test, mirroring spec 143's
/// <c>EventTypeCommandHandlerTests</c>.
/// </para>
/// </summary>
public class SourceModeCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Fact]
    public async Task Declaring_a_mode_stores_it_against_the_resolved_fab()
    {
        InMemorySourceModeRepository repo = new();
        DeclareSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<DeclareSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, DeclareSourceModeError> result = await handler.HandleAsync(
            new DeclareSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.SourceModes.ShouldHaveSingleItem().Fab.ShouldBe(Dresden);
    }

    [Fact]
    public async Task Declaring_a_pair_already_declared_is_refused()
    {
        InMemorySourceModeRepository repo = new();
        SourceMode seeded = SourceMode.Declare(
            Dresden, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Add(seeded);

        DeclareSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<DeclareSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, DeclareSourceModeError> result = await handler.HandleAsync(
            new DeclareSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Discovery, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<DeclareSourceModeError.SourceModeAlreadyDeclared>();
    }

    [Fact]
    public async Task Declaring_the_same_source_in_another_fab_is_allowed()
    {
        InMemorySourceModeRepository repo = new();
        SourceMode seeded = SourceMode.Declare(
            Munich, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Add(seeded);

        DeclareSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<DeclareSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, DeclareSourceModeError> result = await handler.HandleAsync(
            new DeclareSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.SourceModes.Count.ShouldBe(2, "the munich row must stay and a dresden row must be added");
        repo.SourceModes.ShouldContain(sourceMode => sourceMode.Fab == Dresden && sourceMode.Source == Source.Manual);
    }

    [Fact]
    public async Task Changing_a_declared_mode_flips_it()
    {
        InMemorySourceModeRepository repo = new();
        SourceMode seeded = SourceMode.Declare(
            Dresden, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded);

        ChangeSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<ChangeSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, ChangeSourceModeError> result = await handler.HandleAsync(
            new ChangeSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Discovery, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.SourceModes.ShouldHaveSingleItem().Mode.ShouldBe(EventTypeMode.Discovery);
    }

    /// <summary>
    /// Green on arrival: T002's change handler always answers
    /// <c>SourceModeNotDeclared</c>, which happens to already be the right
    /// answer here — not because the fab-scoped lookup exists yet.
    /// </summary>
    [Fact]
    public async Task Changing_an_undeclared_pair_is_not_found()
    {
        InMemorySourceModeRepository repo = new();

        ChangeSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<ChangeSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, ChangeSourceModeError> result = await handler.HandleAsync(
            new ChangeSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Discovery, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<ChangeSourceModeError.SourceModeNotDeclared>();
    }

    [Fact]
    public async Task Changing_with_a_stale_expected_version_is_refused()
    {
        InMemorySourceModeRepository repo = new();
        SourceMode seeded = SourceMode.Declare(
            Dresden, Source.Manual, EventTypeMode.Strict, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded, version: 3);

        ChangeSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<ChangeSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, ChangeSourceModeError> result = await handler.HandleAsync(
            new ChangeSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Discovery, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<ChangeSourceModeError.SourceModeStale>();
        repo.SourceModes.ShouldHaveSingleItem().Mode.ShouldBe(EventTypeMode.Strict, "a refused change flipped it anyway");
    }

    /// <summary>
    /// plan.md §6.4: the lookup runs before the version gate, so a stale
    /// version against an undeclared pair must still answer 404, not 409.
    ///
    /// <para>
    /// Green on arrival — see
    /// <see cref="Changing_an_undeclared_pair_is_not_found"/>. This case
    /// exists so the ordering has a test that would notice it reversed once
    /// T006 lands; it does not notice the reversal itself yet.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_version_gate_runs_after_the_lookup()
    {
        InMemorySourceModeRepository repo = new();

        ChangeSourceModeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<ChangeSourceModeCommandHandler>.Instance);

        Result<SourceModeIdentifier, ChangeSourceModeError> result = await handler.HandleAsync(
            new ChangeSourceModeCommand(
                Dresden, Source.Manual, EventTypeMode.Discovery, 99, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<ChangeSourceModeError.SourceModeNotDeclared>();
    }
}
