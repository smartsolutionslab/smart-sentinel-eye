using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Domain.Tests.Stream;

public class MediaMtxActionTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("publish")]
    [InlineData("playback")]
    public void From_returns_the_matching_instance_for_a_modelled_action(string wire)
    {
        MediaMtxAction action = MediaMtxAction.From(wire);

        action.Value.ShouldBe(wire);
    }

    [Fact]
    public void From_rejects_an_action_the_hook_never_sends()
    {
        Action act = () => MediaMtxAction.From("api");

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("publish")]
    [InlineData("playback")]
    public void TryFrom_returns_Some_for_a_modelled_action(string wire)
    {
        Option<MediaMtxAction> parsed = MediaMtxAction.TryFrom(wire);

        parsed.HasValue.ShouldBeTrue();
        parsed.Value.Value.ShouldBe(wire);
    }

    /// <summary>
    /// <c>api</c>, <c>metrics</c> and <c>pprof</c> are excluded from the hook by
    /// <c>mediamtx.yml:46-49</c>, so they never arrive today. They parse as
    /// absent rather than as anything admissible, which is what keeps the day an
    /// exclusion is deleted a refusal instead of a silent grant.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("api")]
    [InlineData("metrics")]
    [InlineData("pprof")]
    [InlineData("sideload")]
    public void TryFrom_returns_None_for_anything_not_modelled(string? wire)
    {
        Option<MediaMtxAction> parsed = MediaMtxAction.TryFrom(wire);

        parsed.HasValue.ShouldBeFalse();
    }

    /// <summary>
    /// MediaMTX sends these lowercase. The match is ordinal and case-sensitive,
    /// asserted here rather than assumed: a case-insensitive parse would admit a
    /// spelling no MediaMTX build actually posts.
    /// </summary>
    [Theory]
    [InlineData("Read")]
    [InlineData("PUBLISH")]
    public void TryFrom_returns_None_for_a_differently_cased_action(string wire)
    {
        Option<MediaMtxAction> parsed = MediaMtxAction.TryFrom(wire);

        parsed.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToString_returns_the_wire_value()
    {
        MediaMtxAction.Read.ToString().ShouldBe("read");
        MediaMtxAction.Publish.ToString().ShouldBe("publish");
        MediaMtxAction.Playback.ToString().ShouldBe("playback");
    }
}
