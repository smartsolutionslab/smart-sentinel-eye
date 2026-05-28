using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Domain.Tests.Rule;

public class RuleIdentifierTests
{
    [Fact]
    public void New_mints_a_Guid_v7_identifier()
    {
        RuleIdentifier id = RuleIdentifier.New();
        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void From_rejects_the_empty_guid()
    {
        Action act = () => RuleIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_valid_guid()
    {
        Guid guid = Guid.CreateVersion7();
        RuleIdentifier.From(guid).Value.ShouldBe(guid);
    }

    [Fact]
    public void Identifiers_with_the_same_guid_are_equal()
    {
        Guid guid = Guid.CreateVersion7();
        RuleIdentifier.From(guid).ShouldBe(RuleIdentifier.From(guid));
    }
}
