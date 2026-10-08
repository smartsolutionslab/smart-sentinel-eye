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
/// T014 (spec 317, #2325) — FR-007. Registering a kind promotes that fab's
/// held rows of it, on both the success path and the "already registered"
/// path (convergence, A7) — in a new file so the existing
/// <c>EventTypeCommandHandlerTests</c> stays characterisation-only (plan.md
/// §9's sanctioned-edit table does not name it).
/// </summary>
public class RegisterEventTypeCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Berlin = FabIdentifier.From("berlin");
    private static readonly Kind NobodyDeclaredThis = Kind.From("NobodyDeclaredThis");

    [Fact]
    public async Task A_successful_registration_promotes_held_rows_of_that_kind_once_after_saving()
    {
        InMemoryRegisteredEventTypeRepository eventTypes = new();
        InMemoryDeadLetterRepository deadLetters = new();
        RegisterEventTypeCommandHandler handler = new(
            eventTypes, deadLetters, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Berlin, NobodyDeclaredThis, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        deadLetters.PromoteHeldAsyncCalls.ShouldBe(1);
        deadLetters.PromoteHeldAsyncCallArgs.ShouldHaveSingleItem().ShouldBe((Berlin, NobodyDeclaredThis));
        eventTypes.EventTypes.ShouldHaveSingleItem();
    }

    /// <summary>
    /// FR-007's convergence: a registration that fails as already-registered
    /// still runs the promotion, which is exactly what lets a batch held just
    /// before a registration committed get picked up by a later retry.
    /// </summary>
    [Fact]
    public async Task An_already_registered_kind_still_promotes_before_returning_409()
    {
        InMemoryRegisteredEventTypeRepository eventTypes = new();
        RegisteredEventType seeded = RegisteredEventType.Register(
            Berlin, NobodyDeclaredThis, OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now));
        eventTypes.Add(seeded);
        InMemoryDeadLetterRepository deadLetters = new();
        RegisterEventTypeCommandHandler handler = new(
            eventTypes, deadLetters, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(Berlin, NobodyDeclaredThis, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterEventTypeError.EventTypeAlreadyRegistered>();
        deadLetters.PromoteHeldAsyncCalls.ShouldBe(1, "FR-007: the already-registered path must promote too");
        deadLetters.PromoteHeldAsyncCallArgs.ShouldHaveSingleItem().ShouldBe((Berlin, NobodyDeclaredThis));
    }

    /// <summary>Promotion is scoped to the registering fab, never every fab.</summary>
    [Fact]
    public async Task Promotion_is_scoped_to_the_fab_being_registered_in()
    {
        InMemoryRegisteredEventTypeRepository eventTypes = new();
        InMemoryDeadLetterRepository deadLetters = new();
        RegisterEventTypeCommandHandler handler = new(
            eventTypes, deadLetters, new FakeClock(Now), NullLogger<RegisterEventTypeCommandHandler>.Instance);

        await handler.HandleAsync(
            new RegisterEventTypeCommand(Berlin, NobodyDeclaredThis, OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        deadLetters.PromoteHeldAsyncCallArgs.ShouldHaveSingleItem().Fab.ShouldBe(Berlin);
    }
}
