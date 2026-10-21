using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (#2789 FR-004), green on arrival because the detach
/// already exists. A race-path disable whose save fails with a
/// <see cref="DbUpdateException"/> leaves the entity tracked as Modified; the
/// caller's commit on the same context would replay the failing UPDATE. A
/// hand-built exception in an in-memory fake carries no entries, so only a
/// real <see cref="DbContext"/> can observe the detach.
///
/// <para>
/// Counterfactual: replacing <c>Detach(updateException)</c> in
/// <c>RotateWebhookClientCommandHandler</c> with a discard turns this red
/// (the entry stays <see cref="EntityState.Modified"/>).
/// </para>
/// </summary>
public class IdentityRaceDisableDetachCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_save_failure_that_is_not_a_conflict_detaches_the_failed_entry_from_the_context()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        await using IdentityDbContext context = new(options);
        var fab = FabIdentifier.From("munich");
        var operatorIdentifier = OperatorIdentifier.From(Guid.CreateVersion7());
        var client = RegisteredClientAggregate.Register(
            ClientId.From("webhook-qa"), ClientKind.WebhookIntegration, fab, operatorIdentifier, new Clock(Now));
        var entry = context.Attach(client);
        entry.State = EntityState.Modified;

        var clients = new Mock<IRegisteredClientRepository>();
        clients.Setup(repository => repository.GetWithinFabAsync(fab, client.ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.None);
        var keycloak = new Mock<IKeycloakAdminClient>();
        keycloak.Setup(admin => admin.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeycloakClientCredentials("secret"));
        var status = new Mock<IWebhookIntegrationStatusLookup>();
        status.SetupSequence(lookup => lookup.GetStatusAsync(fab, "qa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookIntegrationStatus.Active)
            .ReturnsAsync(WebhookIntegrationStatus.Revoked);
        var disable = new Mock<ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>>();
        disable.Setup(handler => handler.HandleAsync(It.IsAny<DisableWebhookClientCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("x", innerException: null, [UpdateEntryOf(entry)]));
        RotateWebhookClientCommandHandler handler = new(
            clients.Object,
            keycloak.Object,
            status.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<ITransactionalCommit>(),
            new Clock(Now),
            disable.Object,
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new RotateWebhookClientCommand("qa", fab, operatorIdentifier, Option<int>.None),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("the create committed, so the caller still gets its secret");
        entry.State.ShouldBe(EntityState.Detached);
    }

    // The internal entry behind the public one, reached by reflection: naming InternalEntityEntry (GetInfrastructure) trips EF1001.
    private static IUpdateEntry UpdateEntryOf(EntityEntry entry) =>
        (IUpdateEntry)entry.GetType().GetInterfaces()
            .Single(contract => contract.Name == "IInfrastructure`1")
            .GetProperty("Instance")!.GetValue(entry)!;

    private sealed class Clock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
