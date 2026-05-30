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

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = WebhookIntegrationIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        WebhookIntegrationIdentifier earlier = WebhookIntegrationIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        WebhookIntegrationIdentifier later = WebhookIntegrationIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
