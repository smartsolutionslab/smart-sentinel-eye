using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.Shared.Contracts.Tests.EventIngestion;

public class WebhookIntegrationRevokedV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        WebhookIntegrationRevokedV1 evt = new("w", Moment, Metadata: TestMetadata);
        evt.IntegrationName.ShouldBe("w");
        evt.RevokedAt.ShouldBe(Moment);
        evt.Metadata.ShouldBe(TestMetadata);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new WebhookIntegrationRevokedV1("w", Moment, Metadata: TestMetadata)
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal() =>
        new WebhookIntegrationRevokedV1("w", Moment, Metadata: TestMetadata)
            .ShouldBe(new WebhookIntegrationRevokedV1("w", Moment, Metadata: TestMetadata));

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        WebhookIntegrationRevokedV1 original = new("w", Moment, Metadata: TestMetadata);
        string json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<WebhookIntegrationRevokedV1>(json).ShouldBe(original);
    }
}
