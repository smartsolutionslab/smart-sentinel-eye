using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

public class WebhookIntegrationCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Fact]
    public async Task Register_persists_the_integration_and_returns_the_plain_token_once()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        RegisterWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(
                    WebhookIntegrationName.From("qa"),
                    FabIdentifier.From("munich"),
                    Kind.From("QaResult"),
                    new OperatorIdentifier(Guid.CreateVersion7())),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlainToken.ShouldNotBeEmpty();
        repo.Integrations.ShouldHaveSingleItem().Name.Value.ShouldBe("qa");
        repo.Integrations[0].TokenHash.Matches(result.Value.PlainToken).ShouldBeTrue();
    }

    [Fact]
    public async Task Register_raises_the_registered_event_carrying_the_registering_operator()
    {
        EventCapturingRepository repo = new();
        RegisterWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);
        var registeredBy = new OperatorIdentifier(Guid.CreateVersion7());

        var result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(
                    WebhookIntegrationName.From("qa"),
                    FabIdentifier.From("munich"),
                    Kind.From("QaResult"),
                    registeredBy),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var raised = repo.EventsAtSave.OfType<WebhookIntegrationRegisteredDomainEvent>().ShouldHaveSingleItem();
        raised.RegisteredBy.ShouldBe(registeredBy);
        raised.Identifier.ShouldBe(result.Value.Identifier);
        raised.Fab.ShouldBe(FabIdentifier.From("munich"));
        raised.Name.ShouldBe(WebhookIntegrationName.From("qa"));
        raised.DefaultKind.ShouldBe(Kind.From("QaResult"));
    }

    [Fact]
    public async Task Register_with_a_taken_name_returns_WebhookIntegrationNameTaken_and_raises_no_event()
    {
        EventCapturingRepository repo = new();
        (var seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("munich"),
            Kind.From("QaResult"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Seed(seeded);

        RegisterWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(
                    WebhookIntegrationName.From("qa"),
                    FabIdentifier.From("munich"),
                    Kind.From("QaResult"),
                    new OperatorIdentifier(Guid.CreateVersion7())),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterWebhookIntegrationError.WebhookIntegrationNameTaken>();
        repo.EventsAtSave.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoke_flips_the_integration_state_and_returns_the_identifier()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (var seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("munich"),
            Kind.From("QaResult"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Add(seeded);

        RevokeWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand([Munich], WebhookIntegrationName.From("qa"), 0),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeded.Id);
        repo.Integrations[0].IsRevoked.ShouldBeTrue();
    }

    /// <summary>
    /// #1545 — and reported as not-found rather than forbidden, so the answer
    /// cannot be used to discover another plant's integration names.
    /// </summary>
    [Fact]
    public async Task Revoke_of_an_integration_in_another_fab_returns_WebhookIntegrationNotFound()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (var seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("dresden"),
            Kind.From("QaResult"),
            new OperatorIdentifier(Guid.CreateVersion7()),
            new FakeClock(Now));
        repo.Add(seeded);

        RevokeWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand([Munich], WebhookIntegrationName.From("qa"), 0),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RevokeWebhookIntegrationError.WebhookIntegrationNotFound>();
        repo.Integrations[0].IsRevoked.ShouldBeFalse("a refused revoke revoked it anyway");
    }

    [Fact]
    public async Task Revoke_unknown_integration_returns_WebhookIntegrationNotFound()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        RevokeWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

        var result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand([Munich], WebhookIntegrationName.From("ghost"), 0),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RevokeWebhookIntegrationError.WebhookIntegrationNotFound>();
    }

    /// <summary>
    /// <see cref="InMemoryWebhookIntegrationRepository.SaveAsync"/> clears the
    /// pending events, as the real repository does after dispatching them, so
    /// the events a registration raised are read at the moment of the save.
    /// </summary>
    private sealed class EventCapturingRepository : IWebhookIntegrationRepository
    {
        private readonly InMemoryWebhookIntegrationRepository inner = new();
        private readonly List<WebhookIntegration> added = [];
        private readonly List<IDomainEvent> eventsAtSave = [];

        public IReadOnlyList<IDomainEvent> EventsAtSave => eventsAtSave;

        public void Seed(WebhookIntegration integration) => inner.Seed(integration);

        public Task<Option<WebhookIntegration>> GetByNameAsync(
            WebhookIntegrationName name, CancellationToken cancellationToken) =>
            inner.GetByNameAsync(name, cancellationToken);

        public Task<Option<WebhookIntegration>> GetWithinFabAsync(
            FabIdentifier fab, WebhookIntegrationName name, CancellationToken cancellationToken) =>
            inner.GetWithinFabAsync(fab, name, cancellationToken);

        public Task<bool> IsRevokedByKeycloakClientIdAsync(
            KeycloakClientIdentifier keycloakClientId, CancellationToken cancellationToken) =>
            inner.IsRevokedByKeycloakClientIdAsync(keycloakClientId, cancellationToken);

        public void Add(WebhookIntegration integration)
        {
            added.Add(integration);
            inner.Add(integration);
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            eventsAtSave.AddRange(added.SelectMany(integration => integration.PendingEvents));
            return inner.SaveAsync(cancellationToken);
        }
    }
}
