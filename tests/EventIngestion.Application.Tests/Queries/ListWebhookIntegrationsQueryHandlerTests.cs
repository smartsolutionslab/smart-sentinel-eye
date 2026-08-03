using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

public class ListWebhookIntegrationsQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static WebhookIntegration BuildActive(string name)
    {
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From(name),
            Kind.From("WebhookResult"),
            new FakeClock(Now));
        return integration;
    }

    [Fact]
    public async Task Returns_every_active_integration_when_IncludeRevoked_is_true()
    {
        WebhookIntegration[] seed = [BuildActive("alpha"), BuildActive("beta")];
        ListWebhookIntegrationsQueryHandler handler = new(
            new TestWebhookIntegrationQuerySource(seed));

        Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError> result =
            await handler.HandleAsync(
                new ListWebhookIntegrationsQuery(IncludeRevoked: true),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        // In-memory ordering by name (the handler sorts after pulling).
        result.Value.Select(d => d.Name).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public async Task Projects_the_aggregate_version_so_a_caller_has_something_to_send_in_If_Match()
    {
        // The list is the only read path in this context, so without the
        // version on the row a caller has nothing to put in If-Match and the
        // cross-request check degrades to no check (ADR-0113).
        //
        // Version 5, not 0: at 0 this assertion held even if the projection
        // emitted a constant or read the wrong field, since default(int) is
        // also 0. That is what it used to do.
        WebhookIntegration integration = BuildActive("alpha");
        AggregateVersions.SetTo(integration, 5);

        ListWebhookIntegrationsQueryHandler handler = new(
            new TestWebhookIntegrationQuerySource([integration]));

        Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError> result =
            await handler.HandleAsync(
                new ListWebhookIntegrationsQuery(IncludeRevoked: true), CancellationToken.None);

        result.Value.ShouldHaveSingleItem().Version.ShouldBe(5);
    }
}
