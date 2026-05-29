using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.Shared.Contracts.Tests.Identity;

public class WebhookIntegrationRotatedV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        WebhookIntegrationRotatedV1 evt = new("qa", "webhook-qa", Moment, Metadata: TestMetadata);
        evt.IntegrationName.ShouldBe("qa");
        evt.ClientId.ShouldBe("webhook-qa");
        evt.RotatedAt.ShouldBe(Moment);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new WebhookIntegrationRotatedV1("qa", "webhook-qa", Moment, Metadata: TestMetadata)
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal() =>
        new WebhookIntegrationRotatedV1("qa", "webhook-qa", Moment, Metadata: TestMetadata)
            .ShouldBe(new WebhookIntegrationRotatedV1("qa", "webhook-qa", Moment, Metadata: TestMetadata));

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        WebhookIntegrationRotatedV1 original = new("qa", "webhook-qa", Moment, Metadata: TestMetadata);
        string json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<WebhookIntegrationRotatedV1>(json).ShouldBe(original);
    }
}
