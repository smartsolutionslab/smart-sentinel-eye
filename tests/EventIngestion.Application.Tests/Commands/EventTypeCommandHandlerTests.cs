using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// Phase 4a (spec 143 T003c). Exercises the command handlers against the
/// signatures T002 introduced with their bodies withheld.
///
/// <para>
/// Not every case here is red on arrival. T002's retire handler always
/// answers <c>EventTypeNotFound</c> (tasks.md's own prelude spec), which is
/// also the correct answer for "outside the caller's fabs", "already
/// retired" and "stale version against another fab's row" — three of the
/// nine cases below pass immediately as a direct, unavoidable consequence of
/// that canned answer, not because the uniqueness lookup or the version gate
/// exist yet. Named individually at each test. See the phase 4a report for
/// the confirmed list; plan.md §9's written prediction does not cover the
/// Application layer.
/// </para>
/// </summary>
public class EventTypeCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly Kind PersonInRestrictedZone = Kind.From("PersonInRestrictedZone");

    [Fact]
    public async Task Registering_a_kind_stores_it_against_the_resolved_fab()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisterEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.EventTypes.ShouldHaveSingleItem().Fab.ShouldBe(Dresden);
    }

    [Fact]
    public async Task Registering_a_kind_already_registered_in_that_fab_is_refused()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Add(seeded);

        RegisterEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterEventTypeError.EventTypeAlreadyRegistered>();
    }

    [Fact]
    public async Task Registering_a_kind_another_fab_already_has_is_allowed()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Munich, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Add(seeded);

        RegisterEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.EventTypes.Count.ShouldBe(2, "the munich row must stay and a dresden row must be added");
        repo.EventTypes.ShouldContain(eventType => eventType.Fab == Dresden && eventType.Kind == PersonInRestrictedZone);
    }

    [Fact]
    public async Task Registering_a_kind_a_fab_has_retired_is_allowed()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        seeded.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        repo.Add(seeded);

        RegisterEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(2)), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.EventTypes.Count.ShouldBe(2, "the retired row must stay and a freshly-registered row must be added");
    }

    [Fact]
    public async Task Retiring_a_registered_type_flips_it_and_leaves_the_row()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded);

        RetireEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<RetireEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(Dresden, PersonInRestrictedZone, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.EventTypes.ShouldHaveSingleItem().State.ShouldBe(RegistrationState.Retired);
    }

    /// <summary>
    /// Green on arrival: T002's retire handler always answers
    /// <c>EventTypeNotFound</c>, which happens to already be the right
    /// answer here — not because the fab-scoped lookup exists yet.
    /// </summary>
    [Fact]
    public async Task Retiring_a_type_outside_the_callers_fabs_reports_it_as_not_found()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Munich, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded);

        RetireEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<RetireEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(Dresden, PersonInRestrictedZone, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RetireEventTypeError.EventTypeNotFound>();
    }

    /// <summary>
    /// Green on arrival — see
    /// <see cref="Retiring_a_type_outside_the_callers_fabs_reports_it_as_not_found"/>.
    /// </summary>
    [Fact]
    public async Task Retiring_an_already_retired_type_reports_it_as_not_found()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        seeded.Retire(OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now.AddHours(1)));
        repo.Seed(seeded);

        RetireEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(2)), NullLogger<RetireEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(Dresden, PersonInRestrictedZone, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RetireEventTypeError.EventTypeNotFound>();
    }

    [Fact]
    public async Task Retiring_with_a_stale_expected_version_is_refused()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Dresden, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded, version: 3);

        RetireEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<RetireEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(Dresden, PersonInRestrictedZone, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RetireEventTypeError.EventTypeStale>();
        repo.EventTypes.ShouldHaveSingleItem().State.ShouldNotBe(RegistrationState.Retired, "a refused retire retired it anyway");
    }

    /// <summary>
    /// plan.md §6: the lookup runs before the version gate, so a stale
    /// version against a row in another fab must still answer 404, not 409.
    ///
    /// <para>
    /// Green on arrival — see
    /// <see cref="Retiring_a_type_outside_the_callers_fabs_reports_it_as_not_found"/>.
    /// This case exists so the ordering has a test that would notice it
    /// reversed once T005 lands; it does not notice the reversal itself yet.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_version_gate_runs_after_the_lookup()
    {
        InMemoryRegisteredEventTypeRepository repo = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Munich, PersonInRestrictedZone, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        repo.Seed(seeded, version: 3);

        RetireEventTypeCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)), NullLogger<RetireEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(Dresden, PersonInRestrictedZone, 0, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RetireEventTypeError.EventTypeNotFound>();
    }
}
