using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class SourceTests
{
    [Fact]
    public void Exposes_the_four_singletons_with_lowercase_wire_strings()
    {
        Source.Plc.Value.ShouldBe("plc");
        Source.Inference.Value.ShouldBe("inference");
        Source.Manual.Value.ShouldBe("manual");
        Source.Webhook.Value.ShouldBe("webhook");
    }

    [Theory]
    [InlineData("plc")]
    [InlineData("inference")]
    [InlineData("manual")]
    [InlineData("webhook")]
    public void From_round_trips_each_known_value(string raw)
    {
        Source.From(raw).Value.ShouldBe(raw);
    }

    [Fact]
    public void From_returns_the_singleton_for_each_known_value()
    {
        Source.From("plc").ShouldBeSameAs(Source.Plc);
        Source.From("inference").ShouldBeSameAs(Source.Inference);
        Source.From("manual").ShouldBeSameAs(Source.Manual);
        Source.From("webhook").ShouldBeSameAs(Source.Webhook);
    }

    [Theory]
    [InlineData("PLC")]
    [InlineData("")]
    [InlineData("mqtt")]
    public void From_rejects_unknown_strings(string raw)
    {
        Action act = () => Source.From(raw);
        act.ShouldThrow<ArgumentException>();
    }
}
