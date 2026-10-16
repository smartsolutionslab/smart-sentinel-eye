using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 389 / issue #2999, ADR-0144), captured green before <see cref="RtspUrl.From"/> learns to
/// refuse a raw '@' after the authority. What it accepts today it must keep accepting, and what it already
/// refuses it must keep refusing with the message it refuses with now. Neither set may be edited by the change.
/// </summary>
public class RtspUrlAcceptSetCharacterisationTests
{
    private const string UserInfoRefusal = "value: must not contain a user:password@ segment (Parameter 'value')";

    [Theory]
    [InlineData("rtsp://10.0.5.12/h264")]
    [InlineData("rtsp://cam:554/path?x=y")]
    [InlineData("RTSP://CAM/s")]
    [InlineData("rtsp://[::1]:554/s")]
    [InlineData("rtsp://[fe80::1]/s")]
    [InlineData("rtsp://cam/s;x")]
    [InlineData("rtsp://cam/%40x")]
    [InlineData("rtsp://cam/s?mail=a%40b")]
    public void From_keeps_accepting_an_address_with_no_at_sign_after_the_authority(string address)
    {
        RtspUrl.From(address).Value.ShouldBe(address);
    }

    [Theory]
    [InlineData("rtsp://admin:pass@cam/s")]
    [InlineData("rtsp://admin:12345%23@cam/s")]
    [InlineData("rtsp://cam@")]
    public void From_keeps_refusing_a_userinfo_segment_with_the_userinfo_message(string address)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(UserInfoRefusal);
    }

    [Theory]
    [InlineData("http://10.0.5.12/h264", "value must start with 'rtsp://'. (Parameter 'value')")]
    [InlineData("", "value must not be null or whitespace. (Parameter 'value')")]
    [InlineData("   ", "value must not be null or whitespace. (Parameter 'value')")]
    public void From_keeps_refusing_a_non_rtsp_or_blank_address_with_its_message(string address, string message)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(message);
    }

    [Fact]
    public void From_keeps_refusing_an_over_long_address_with_its_message()
    {
        var overLong = "rtsp://" + new string('x', RtspUrl.MaximumLength);

        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(overLong));

        refused.Message.ShouldBe("value must be no more than 2048 character(s). (Parameter 'value')");
    }
}
