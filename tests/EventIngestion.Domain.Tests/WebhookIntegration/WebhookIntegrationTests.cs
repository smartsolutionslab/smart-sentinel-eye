using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.Events;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.WebhookIntegration;

public class WebhookIntegrationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Register_returns_an_integration_with_a_freshly_generated_token_hash()
    {
        (Domain.WebhookIntegration.WebhookIntegration integration, string token) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                Kind.From("QaResult"),
                new FakeClock(Now));

        integration.Name.Value.ShouldBe("qa");
        integration.DefaultKind.Value.ShouldBe("QaResult");
        integration.RegisteredAt.ShouldBe(Now);
        integration.IsRevoked.ShouldBeFalse();

        token.ShouldNotBeEmpty();
        integration.TokenHash.Matches(token).ShouldBeTrue();
    }

    [Fact]
    public void Register_raises_a_WebhookIntegrationRegisteredDomainEvent()
    {
        (Domain.WebhookIntegration.WebhookIntegration integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                Kind.From("QaResult"),
                new FakeClock(Now));

        integration.PendingEvents.OfType<WebhookIntegrationRegisteredDomainEvent>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Revoke_flips_the_state_and_raises_a_domain_event()
    {
        (Domain.WebhookIntegration.WebhookIntegration integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                Kind.From("QaResult"),
                new FakeClock(Now));
        integration.ClearPendingEvents();

        integration.Revoke(new FakeClock(Now.AddHours(1)));

        integration.IsRevoked.ShouldBeTrue();
        integration.RevokedAt.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.OfType<WebhookIntegrationRevokedDomainEvent>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Revoke_is_idempotent_on_an_already_revoked_integration()
    {
        (Domain.WebhookIntegration.WebhookIntegration integration, _) =
            Domain.WebhookIntegration.WebhookIntegration.Register(
                WebhookIntegrationName.From("qa"),
                Kind.From("QaResult"),
                new FakeClock(Now));
        integration.Revoke(new FakeClock(Now.AddHours(1)));
        integration.ClearPendingEvents();

        integration.Revoke(new FakeClock(Now.AddHours(2)));

        integration.RevokedAt.ShouldBe(Now.AddHours(1));
        integration.PendingEvents.ShouldBeEmpty();
    }
}
