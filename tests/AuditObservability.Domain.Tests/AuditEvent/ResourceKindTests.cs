using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class ResourceKindTests
{
    [Theory]
    [InlineData("camera")]
    [InlineData("stream")]
    [InlineData("layout")]
    [InlineData("overlay")]
    [InlineData("variable")]
    [InlineData("rule")]
    [InlineData("event")]
    [InlineData("webhook")]
    [InlineData("device")]
    [InlineData("kiosk")]
    [InlineData("webhook-integration")]
    public void Accepts_every_member_of_the_v1_vocabulary(string member)
    {
        ResourceKind kind = ResourceKind.From(member);
        kind.Value.ShouldBe(member);
    }

    [Fact]
    public void Rejects_an_unknown_string()
    {
        ArgumentException ex = Should.Throw<ArgumentException>(
            () => ResourceKind.From("audit"));
        ex.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Rejects_empty()
    {
        Should.Throw<ArgumentException>(() => ResourceKind.From(""));
    }

    [Fact]
    public void All_returns_the_full_vocabulary()
    {
        ResourceKind.All.Count.ShouldBe(11);
        ResourceKind.All.ShouldContain(ResourceKind.Camera);
        ResourceKind.All.ShouldContain(ResourceKind.WebhookIntegration);
    }

    [Fact]
    public void Static_singletons_round_trip()
    {
        ResourceKind.From("rule").ShouldBe(ResourceKind.Rule);
    }
}
