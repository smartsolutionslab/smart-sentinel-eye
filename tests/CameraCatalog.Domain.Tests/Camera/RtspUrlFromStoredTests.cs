using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 389 / issue #2999. FromStored is the persistence read path: the write rules without the raw-@ rule, so a row
/// stored before the rule stays readable. It must keep refusing everything From refused before the rule, with the same messages, except a userinfo segment (spec 450 / #3118: now readable).
/// </summary>
public class RtspUrlFromStoredTests
{
    [Theory]
    [InlineData("rtsp://10.0.5.12/h264")]
    [InlineData("RTSP://CAM/s")]
    [InlineData("rtsp://[::1]:554/s")]
    [InlineData("rtsp://cam/s?mail=a%40b")]
    [InlineData("rtsp://admin:12345#@cam/s")]
    [InlineData("rtsp://admin:4711?@cam/s")]
    [InlineData("rtsp://admin:8080/x@cam/s")]
    [InlineData("RTSP://ADMIN:12345#@CAM")]
    [InlineData("rtsp://3232235777:12345#@cam")]
    [InlineData("rtsp://12345#@cam/s")]
    [InlineData("rtsp://admin:12345#@[fd00::5]:8554/x")]
    [InlineData("rtsp://cam/s?mail=a@b")]
    [InlineData("rtsp://cam/a@b")]
    [InlineData("rtsp://cam/s#frag@x")]
    public void FromStored_accepts_what_was_stored_including_a_raw_at_sign_after_the_authority(string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Theory]
    [InlineData("http://10.0.5.12/h264", "value must start with 'rtsp://'. (Parameter 'value')")]
    [InlineData("", "value must not be null or whitespace. (Parameter 'value')")]
    [InlineData("   ", "value must not be null or whitespace. (Parameter 'value')")]
    public void FromStored_keeps_refusing_what_From_refused_before_the_raw_at_rule(string stored, string message)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.FromStored(stored));

        refused.Message.ShouldBe(message);
    }

    // Spec 450 / issue #3118: DELIBERATE BEHAVIOUR CHANGE. These userinfo rows were once refused by FromStored (they sat in
    // the refusal theory above); a stored row holding a userinfo segment must now be readable, so the read path can mask it.
    [Theory]
    [InlineData("rtsp://u:USERPWMARK@10.0.5.98/x")]
    [InlineData("rtsp://admin:pass@cam/s")]
    [InlineData("rtsp://admin:12345%23@cam/s")]
    [InlineData("rtsp://user@[fd00::5]:8554/x")]
    public void FromStored_accepts_a_stored_row_holding_a_userinfo_segment(string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Fact]
    public void FromStored_keeps_refusing_an_over_long_value()
    {
        var overLong = "rtsp://" + new string('x', RtspUrl.MaximumLength);

        var refused = Should.Throw<ArgumentException>(() => RtspUrl.FromStored(overLong));

        refused.Message.ShouldBe("value must be no more than 2048 character(s). (Parameter 'value')");
    }
}
