using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

public class CameraStatusTests
{
    [Fact]
    public void Registered_singleton_carries_the_expected_value()
    {
        CameraStatus.Registered.Value.ShouldBe("Registered");
    }

    [Fact]
    public void Decommissioned_singleton_carries_the_expected_value()
    {
        CameraStatus.Decommissioned.Value.ShouldBe("Decommissioned");
    }

    [Fact]
    public void From_known_value_returns_the_matching_singleton()
    {
        CameraStatus.From("Registered").ShouldBeSameAs(CameraStatus.Registered);
        CameraStatus.From("Decommissioned").ShouldBeSameAs(CameraStatus.Decommissioned);
    }

    [Theory]
    [InlineData("registered")]
    [InlineData("Active")]
    [InlineData("")]
    public void From_unknown_value_throws(string value)
    {
        Action act = () => CameraStatus.From(value);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_canonical_value()
    {
        CameraStatus.Registered.ToString().ShouldBe("Registered");
    }
}
