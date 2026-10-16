using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 381 / issue #2993, changed by spec 389 / issue #2999. An address whose userinfo the parser would read as
/// host and port used to be accepted, logged as the placeholder and stored in full. It is now refused where the
/// value object is built, so it never reaches the handler: the refusal carries the fix text and no part of the
/// address. That the camera keeps the address it had is proved at the endpoint, where a handler exists to be
/// skipped: <c>CameraUrlAtSignAfterAuthorityEndpointTests</c>.
/// </summary>
public class ChangeCameraAddressUserInfoLogRedactionTests
{
    private const string Credential = "operator:2468";
    private const string CutShortAddress = "rtsp://operator:2468#!@10.0.0.5:554/h264";

    private const string Refusal =
        "value: must not contain a raw '@' after the first '/', '?', '#' or '\\': the text before it would be read as the host and port. Percent-encode '#', '?' and '/' in a password (%23, %3F, %2F) and '@' in a path or query (%40); a user:password@ segment is not accepted either (Parameter 'value')";

    [Fact]
    public void Changing_the_address_to_a_cut_short_userinfo_is_refused_without_echoing_the_credential()
    {
        var refused = Should.Throw<ArgumentException>(() => RtspUrl.From(CutShortAddress));

        refused.Message.ShouldBe(Refusal);
        refused.Message.ShouldNotContain(Credential, Case.Insensitive);
    }
}
