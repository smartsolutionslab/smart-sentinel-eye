using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Domain.Tests.Camera;

/// <summary>
/// Spec 389 / issue #2999, red. A raw '@' after the first '/', '?', '#' or '\' past the scheme is read by the
/// parser as the end of the host and port, so what precedes it (often a camera password) is stored, published
/// and shown in clear. <see cref="RtspUrl"/>.From refuses it with a message that says what to fix and carries no
/// part of the address.
/// </summary>
public class RtspUrlAtSignAfterAuthorityTests
{
    private const string Refusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    [Theory]
    [InlineData("rtsp://admin:12345#@cam/s", "12345")]
    [InlineData("rtsp://admin:4711?@cam/s", "4711")]
    [InlineData("rtsp://admin:8080/x@cam/s", "admin")]
    [InlineData("RTSP://ADMIN:12345#@CAM", "12345")]
    [InlineData("rtsp://3232235777:12345#@cam", "3232235777")]
    [InlineData("rtsp://12345#@cam/s", "12345")]
    [InlineData("rtsp://admin:12345#@[fd00::5]:8554/x", "12345")]
    [InlineData("rtsp://cam/s?mail=a@b", "mail")]
    [InlineData("rtsp://cam/a@b", "a@b")]
    [InlineData("rtsp://cam/s#frag@x", "frag")]
    public void From_refuses_an_at_sign_after_the_authority_and_does_not_echo_the_address(string address, string secret)
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(address));

        refused.Message.ShouldBe(Refusal);
        refused.Message.ShouldNotContain(secret, Case.Insensitive);
    }
}
