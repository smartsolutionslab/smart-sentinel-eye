using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.WebhookIntegration;

public class WebhookIntegrationNameTests
{
    [Theory]
    [InlineData("qa")]
    [InlineData("erp-shop-floor")]
    [InlineData("a")]
    public void Accepts_kebab_lowercase_names(string raw) =>
        WebhookIntegrationName.From(raw).Value.ShouldBe(raw);

    [Theory]
    [InlineData("")]
    [InlineData("Qa")]
    [InlineData("1bad")]
    [InlineData("with space")]
    [InlineData("under_score")]
    public void Rejects_malformed_names(string raw)
    {
        Action act = () => WebhookIntegrationName.From(raw);
        act.ShouldThrow<ArgumentException>();
    }
}
