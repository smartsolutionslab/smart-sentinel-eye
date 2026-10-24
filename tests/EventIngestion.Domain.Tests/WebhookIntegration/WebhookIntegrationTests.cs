using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.WebhookIntegration;

public class WebhookIntegrationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Register_returns_an_integration_with_a_freshly_generated_token_hash()
    {
        (var integration, var token) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));

        integration.Name.Value.ShouldBe("qa");
        integration.DefaultKind.Value.ShouldBe("QaResult");
        integration.RegisteredAt.Value.ShouldBe(Now);
        integration.IsRevoked.ShouldBeFalse();

        // #1545. Without this the integration has no plant to compare a
        // delivery's ?fabId= against, which is how a token issued for one plant
        // could file events into another.
        integration.Fab.ShouldBe(FabIdentifier.From("munich"));

        token.ShouldNotBeEmpty();
        integration.TokenHash.Matches(token).ShouldBeTrue();
    }

    [Fact]
    public void Register_raises_a_WebhookIntegrationRegisteredDomainEvent()
    {
        var registeredBy = new OperatorIdentifier(Guid.CreateVersion7());

        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                registeredBy,
                new FakeClock(Now));

        var raised = integration.PendingEvents.OfType<WebhookIntegrationRegisteredDomainEvent>()
            .ShouldHaveSingleItem();
        raised.Identifier.ShouldBe(integration.Id);
        raised.Name.ShouldBe(WebhookIntegrationName.From("qa"));
        raised.Fab.ShouldBe(FabIdentifier.From("munich"));
        raised.DefaultKind.ShouldBe(Kind.From("QaResult"));
        raised.RegisteredAt.ShouldBe(Now);
        raised.RegisteredBy.ShouldBe(registeredBy);
    }

    [Fact]
    public void The_WebhookIntegrationRegisteredDomainEvent_has_no_member_that_could_carry_the_token()
    {
        var members = typeof(WebhookIntegrationRegisteredDomainEvent).GetProperties();

        members.ShouldNotContain(property => property.PropertyType == typeof(BearerTokenHash));
        members.ShouldNotContain(property => property.PropertyType == typeof(string));
        members.ShouldNotContain(property =>
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Revoke_flips_the_state_and_raises_a_domain_event()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));
        integration.ClearPendingEvents();

        integration.Revoke(new FakeClock(Now.AddHours(1)));

        integration.IsRevoked.ShouldBeTrue();
        integration.RevokedAt!.Value.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.OfType<WebhookIntegrationRevokedDomainEvent>()
            .ShouldHaveSingleItem();
    }

    /// <summary>
    /// Spec 264 (#2206). The domain event needs the integration's fab so the
    /// EventIngestion → Identity crossing can announce which fab's Keycloak
    /// client to disable — see plan.md §3. Red: compile, until
    /// <c>WebhookIntegrationRevokedDomainEvent</c> gains <c>Fab</c>.
    /// </summary>
    [Fact]
    public void Revoke_raises_a_domain_event_carrying_the_integrations_fab()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));
        integration.ClearPendingEvents();

        integration.Revoke(new FakeClock(Now.AddHours(1)));

        var raised = integration.PendingEvents
            .OfType<WebhookIntegrationRevokedDomainEvent>()
            .ShouldHaveSingleItem();
        raised.Fab.ShouldBe(FabIdentifier.From("munich"));
        raised.Name.ShouldBe(integration.Name);
        raised.RevokedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Revoke_is_idempotent_on_an_already_revoked_integration()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));
        integration.Revoke(new FakeClock(Now.AddHours(1)));
        integration.ClearPendingEvents();

        integration.Revoke(new FakeClock(Now.AddHours(2)));

        integration.RevokedAt!.Value.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Register_starts_in_StaticHash_validation_mode()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));

        integration.ValidationMode.ShouldBe(BearerValidationMode.StaticHash);
        integration.KeycloakClientId.ShouldBeNull();
        integration.RotatedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkAsRotated_flips_validation_mode_and_raises_event()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));
        integration.ClearPendingEvents();

        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-qa"), new FakeClock(Now.AddHours(1)));

        integration.ValidationMode.ShouldBe(BearerValidationMode.Jwt);
        integration.KeycloakClientId!.Value.ShouldBe("webhook-qa");
        integration.RotatedAt!.Value.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.OfType<WebhookIntegrationRotatedDomainEvent>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void MarkAsRotated_is_idempotent_on_the_same_clientId()
    {
        (var integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                FabIdentifier.From("munich"),
                Kind.From("QaResult"),
                new OperatorIdentifier(Guid.CreateVersion7()),
                new FakeClock(Now));
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-qa"), new FakeClock(Now.AddHours(1)));
        integration.ClearPendingEvents();

        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-qa"), new FakeClock(Now.AddHours(2)));

        integration.RotatedAt!.Value.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.ShouldBeEmpty();
    }
}
