using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

public class WebhookIntegrationRotatedV1HandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T10:00:00Z", CultureInfo.InvariantCulture);
    // Spec 182 US2 (#2280), declared in tasks.md T005: corrected from
    // Fab: null to the integration's own fab. No production publisher ever
    // sends Fab: null — RotateWebhookClientCommandHandler always passes
    // fab.Value — so the previous value did not match what production sends.
    // A setup-data correction, not a new test; no assertion below changes.
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    [Fact]
    public async Task Flips_a_registered_integration_to_JWT_validation()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("munich"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(1)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("qa", "webhook-qa", Now.AddMinutes(1), Metadata: TestMetadata),
            CancellationToken.None);

        integration.ValidationMode.ShouldBe(BearerValidationMode.Jwt);
        integration.KeycloakClientId!.Value.ShouldBe("webhook-qa");
    }

    [Fact]
    public async Task Unknown_integration_is_a_no_op()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("not-here", "webhook-not-here", Now, Metadata: TestMetadata),
            CancellationToken.None);

        repo.Integrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replay_against_already_rotated_integration_is_idempotent()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            FabIdentifier.From("munich"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-qa"), new FakeClock(Now.AddMinutes(1)));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(5)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("qa", "webhook-qa", Now.AddMinutes(5), Metadata: TestMetadata),
            CancellationToken.None);

        integration.RotatedAt!.Value.ShouldBe(Now.AddMinutes(1)); // unchanged
    }

    /// <summary>
    /// Spec 182 US2 (#2280), AS-6 — the defect. The integration is registered
    /// in dresden; the announcement's <c>Metadata.Fab</c> is munich, the
    /// fab a first-rotation attacker actually holds. Today the handler
    /// resolves by name alone and flips it anyway.
    /// </summary>
    [Fact]
    public async Task A_rotation_naming_another_fab_does_not_flip_the_integration_to_JWT_validation()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("dresden-line-3"),
            FabIdentifier.From("dresden"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(1)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        // TestMetadata.Fab is "munich"; the integration above is dresden's.
        await handler.Handle(
            new WebhookIntegrationRotatedV1(
                "dresden-line-3", "webhook-dresden-line-3", Now.AddMinutes(1), Metadata: TestMetadata),
            CancellationToken.None);

        integration.ValidationMode.ShouldBe(
            BearerValidationMode.StaticHash,
            "a rotation announcement carrying fab 'munich' must never flip an integration registered "
            + $"in 'dresden'; got {integration.ValidationMode} — that is the cross-fab takeover this "
            + "spec exists to close");
        integration.KeycloakClientId.ShouldBeNull(
            "the attacker's Keycloak client must never be recorded against Dresden's integration");
    }

    /// <summary>
    /// Spec 182 US2 (#2280), AS-6's second Gherkin block. A message with no
    /// fab at all is indistinguishable from one whose fab was dropped, and
    /// the handler mutates a security-relevant validation mode — so absent
    /// is refused, not waved through.
    /// </summary>
    [Fact]
    public async Task A_rotation_with_no_fab_at_all_does_not_flip_the_integration_to_JWT_validation()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("dresden-line-3"),
            FabIdentifier.From("dresden"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(1)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1(
                "dresden-line-3", "webhook-dresden-line-3", Now.AddMinutes(1),
                Metadata: TestMetadata with { Fab = null }),
            CancellationToken.None);

        integration.ValidationMode.ShouldBe(
            BearerValidationMode.StaticHash,
            $"an event with no fab at all must be refused, not waved through; got {integration.ValidationMode}");
        integration.KeycloakClientId.ShouldBeNull();
    }
}
