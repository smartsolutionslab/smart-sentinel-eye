using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.WebhookIntegration;

public class WebhookIntegrationIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_identifier()
    {
        WebhookIntegrationIdentifier id = WebhookIntegrationIdentifier.New();
        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => WebhookIntegrationIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        Guid guid = Guid.CreateVersion7();
        WebhookIntegrationIdentifier.From(guid).Value.ShouldBe(guid);
    }
}
