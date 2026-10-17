using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Characterisation (spec 392 / issues #3012 and #3017, US2a), captured green on <c>de9802b1</c> and required to
/// stay green after: a row stored before the empty-host and text-after-bracket rules is read back by
/// <see cref="RtspUrl.FromStored"/> exactly as stored. <see cref="RtspUrl.From"/> is about to refuse every one
/// of these; the read path must not, or one such row takes the whole fab's camera list down.
/// </summary>
public class RtspUrlFromStoredAuthorityHostTests
{
    public static TheoryData<string> EmptyHostAddresses =>
    [
        "rtsp://",
        "rtsp:///",
        "rtsp:///cam",
        "RTSP:///cam",
        "rtsp:///admin:4242@cam/s",
        "rtsp:////admin:4242@cam/s",
        "rtsp://?x",
        "rtsp://#x",
        "rtsp:///?x",
    ];

    public static TheoryData<string> BracketTailAddresses =>
    [
        "rtsp://[fe80::1]%25eth0/x",
        "rtsp://[fe80::1]%eth0/x",
        "rtsp://[fe80::1]garbage.example/x",
        "rtsp://[fe80::1]garbage.example",
        "rtsp://[fe80::1]x:554/s",
        "rtsp://[fe80::1]]/x",
        "RTSP://[FE80::1]GARBAGE/x",
        "rtsp://[::ffff:10.0.0.1]x/s",
        "rtsp://[fe80::1/64]x/s",
        "rtsp://[fe80::1]%20/s",
    ];

    [Theory]
    [MemberData(nameof(EmptyHostAddresses))]
    public void FromStored_reads_back_an_address_with_an_empty_host(string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(BracketTailAddresses))]
    public void FromStored_reads_back_an_address_with_text_after_the_closing_bracket(string stored)
    {
        RtspUrl.FromStored(stored).Value.ShouldBe(stored);
    }
}
