using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;

public class ClientIdTests
{
    [Theory]
    [InlineData("plc-station-4")]
    [InlineData("inference-camera-12")]
    [InlineData("kiosk-3")]
    [InlineData("webhook-qa")]
    [InlineData("a")]
    public void Accepts_well_formed_client_ids(string raw) =>
        ClientId.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading-dash")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public void Rejects_malformed_client_ids(string raw)
    {
        Action act = () => ClientId.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_client_id()
    {
        Action act = () => ClientId.From(new string('a', ClientId.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }
}
