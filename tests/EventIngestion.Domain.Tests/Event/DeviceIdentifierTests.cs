using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class DeviceIdentifierTests
{
    [Theory]
    [InlineData("station-4")]
    [InlineData("camera-12")]
    [InlineData("camera.front.left")]
    [InlineData("PLC_99")]
    [InlineData("a")]
    public void Accepts_well_formed_identifiers(string raw)
    {
        DeviceIdentifier device = DeviceIdentifier.From(raw);
        device.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading-dash")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public void Rejects_malformed_identifiers(string raw)
    {
        Action act = () => DeviceIdentifier.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_overlong_identifier()
    {
        Action act = () => DeviceIdentifier.From(new string('a', DeviceIdentifier.MaximumLength + 1));
        act.ShouldThrow<ArgumentException>();
    }
}
