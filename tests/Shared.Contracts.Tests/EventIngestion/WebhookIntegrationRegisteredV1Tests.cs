using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.Shared.Contracts.Tests.EventIngestion;

/// <summary>
/// #3110. Wire-shape characterisation: these tests arrive green with the
/// record, which is the point of a shape pin. The last one is the guard for
/// spec 445 FR-002 — the event must not grow a member that could carry the
/// bearer token or its hash (ADR-0185 decision 4).
/// </summary>
public class WebhookIntegrationRegisteredV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-24T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly Guid Integration = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-10-24T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        Guid.Parse("00000000-0000-0000-0000-0000000000cc"));

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        WebhookIntegrationRegisteredV1 evt = new(Integration, "w", "WebhookAlarm", Moment, TestMetadata);
        evt.Integration.ShouldBe(Integration);
        evt.IntegrationName.ShouldBe("w");
        evt.DefaultKind.ShouldBe("WebhookAlarm");
        evt.RegisteredAt.ShouldBe(Moment);
        evt.Metadata.ShouldBe(TestMetadata);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it() =>
        new WebhookIntegrationRegisteredV1(Integration, "w", "WebhookAlarm", Moment, TestMetadata)
            .ShouldBeAssignableTo<IIntegrationEvent>();

    [Fact]
    public void Records_with_the_same_payload_are_equal() =>
        new WebhookIntegrationRegisteredV1(Integration, "w", "WebhookAlarm", Moment, TestMetadata)
            .ShouldBe(new WebhookIntegrationRegisteredV1(Integration, "w", "WebhookAlarm", Moment, TestMetadata));

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        WebhookIntegrationRegisteredV1 original = new(Integration, "w", "WebhookAlarm", Moment, TestMetadata);
        var json = JsonSerializer.Serialize(original);
        JsonSerializer.Deserialize<WebhookIntegrationRegisteredV1>(json).ShouldBe(original);
    }

    [Fact]
    public void Has_exactly_the_five_wire_members_and_none_that_could_carry_the_token()
    {
        var members = typeof(WebhookIntegrationRegisteredV1)
            .GetProperties()
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        members.ShouldBe(
            ["DefaultKind", "Integration", "IntegrationName", "Metadata", "RegisteredAt"]);
        members.ShouldNotContain(name =>
            name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hash", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }
}
