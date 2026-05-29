using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class ClientKindTests
{
    [Fact]
    public void Exposes_three_singletons_with_their_wire_strings()
    {
        ClientKind.Device.Value.ShouldBe("Device");
        ClientKind.Kiosk.Value.ShouldBe("Kiosk");
        ClientKind.WebhookIntegration.Value.ShouldBe("WebhookIntegration");
    }

    [Theory]
    [InlineData("Device")]
    [InlineData("Kiosk")]
    [InlineData("WebhookIntegration")]
    public void From_round_trips_each_known_value(string raw) =>
        ClientKind.From(raw).Value.ShouldBe(raw);

    [Fact]
    public void From_returns_the_singleton_for_each_known_value()
    {
        ClientKind.From("Device").ShouldBeSameAs(ClientKind.Device);
        ClientKind.From("Kiosk").ShouldBeSameAs(ClientKind.Kiosk);
        ClientKind.From("WebhookIntegration").ShouldBeSameAs(ClientKind.WebhookIntegration);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("")]
    [InlineData("Service")]
    public void From_rejects_unknown_strings(string raw)
    {
        Action act = () => ClientKind.From(raw);
        act.ShouldThrow<ArgumentException>();
    }
}
