using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// The registration's audit record is published before the write commits, so a
/// publish that fails must fail the registration: no row, and no bearer token
/// handed back for a credential nothing recorded. Swallowing the failure in the
/// handler would turn that into a credential issued with no audit trail.
/// </summary>
public class WebhookRegistrationPublishFailureTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-24T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_failing_publish_propagates_before_the_commit_and_discloses_no_token()
    {
        List<string> order = [];
        await using var database = new EventIngestionDbContext(
            new DbContextOptionsBuilder<EventIngestionDbContext>()
                .UseNpgsql("Host=not-connected;Database=none;Username=none;Password=none")
                .Options);
        WebhookIntegrationRepository repository = new(
            database,
            new RecordingCommit(order),
            new RegisteredEventDispatcher(new WebhookIntegrationRegisteredDomainEventHandler(
                new ThrowingEventBus(),
                NullLogger<WebhookIntegrationRegisteredDomainEventHandler>.Instance)));
        RegisterWebhookIntegrationCommandHandler handler = new(
            new UnclaimedNameRepository(repository),
            new FixedClock(),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);
        RegisterWebhookIntegrationCommand command = new(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("munich"),
            Kind.From("QaResult"),
            new OperatorIdentifier(Guid.CreateVersion7()));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(command, CancellationToken.None));

        thrown.Message.ShouldBe("publish failed");
        order.ShouldBeEmpty("the registration committed although its audit record was not published");
    }

    private sealed class ThrowingEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull =>
            throw new InvalidOperationException("publish failed");
    }

    private sealed class RegisteredEventDispatcher(WebhookIntegrationRegisteredDomainEventHandler handler)
        : IDomainEventDispatcher
    {
        public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
        {
            foreach (var registered in domainEvents.OfType<WebhookIntegrationRegisteredDomainEvent>())
            {
                await handler.Handle(registered, cancellationToken);
            }
        }
    }

    private sealed class RecordingCommit(List<string> order) : ITransactionalCommit
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            order.Add("commit");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The real repository's name lookup needs a database; the save path under
    /// test does not.
    /// </summary>
    private sealed class UnclaimedNameRepository(WebhookIntegrationRepository inner) : IWebhookIntegrationRepository
    {
        public Task<Option<WebhookIntegration>> GetByNameAsync(
            WebhookIntegrationName name, CancellationToken cancellationToken) =>
            Task.FromResult(Option<WebhookIntegration>.None);

        public Task<Option<WebhookIntegration>> GetWithinFabAsync(
            FabIdentifier fab, WebhookIntegrationName name, CancellationToken cancellationToken) =>
            inner.GetWithinFabAsync(fab, name, cancellationToken);

        public Task<bool> IsRevokedByKeycloakClientIdAsync(
            KeycloakClientIdentifier keycloakClientId, CancellationToken cancellationToken) =>
            inner.IsRevokedByKeycloakClientIdAsync(keycloakClientId, cancellationToken);

        public void Add(WebhookIntegration integration) => inner.Add(integration);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
