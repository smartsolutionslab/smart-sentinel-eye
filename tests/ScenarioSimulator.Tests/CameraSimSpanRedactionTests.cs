using SmartSentinelEye.ScenarioSimulator.CameraSim;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 393 / #3019. <c>CameraSimSpanRedaction.Redact</c> rewrites a url.full whose path begins
/// /v3/config/paths/{verb}/ and returns every other input unchanged./// </summary>
public sealed class CameraSimSpanRedactionTests
{
    [Theory]
    [InlineData("http://camera-sim:9997/v3/config/paths/add/SECRET-token/stream", "http://camera-sim:9997/v3/config/paths/add/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/replace/SECRET-token/stream", "http://camera-sim:9997/v3/config/paths/replace/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/patch/SECRET-token/stream", "http://camera-sim:9997/v3/config/paths/patch/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/delete/SECRET-token/stream", "http://camera-sim:9997/v3/config/paths/delete/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/get/SECRET-token/stream", "http://camera-sim:9997/v3/config/paths/get/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/add/SECRET-token?token=x", "http://camera-sim:9997/v3/config/paths/add/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/add/SECRET-token#frag", "http://camera-sim:9997/v3/config/paths/add/Redacted")]
    [InlineData("http://camera-sim:9997/v3/config/paths/add/%53ECRET/stream", "http://camera-sim:9997/v3/config/paths/add/Redacted")]
    [InlineData("http://[::1]:9997/v3/config/paths/add/SECRET-token/stream", "http://[::1]:9997/v3/config/paths/add/Redacted")]
    [InlineData("http://camera-sim/v3/config/paths/add/SECRET-token/stream", "http://camera-sim/v3/config/paths/add/Redacted")]
    public void A_config_paths_url_keeps_scheme_host_and_verb_and_loses_everything_after(string urlFull, string expected) =>
        Redact(urlFull).ShouldBe(expected);

    [Theory]
    [InlineData("http://camera-sim:9997/v3/paths/get/station-4-roughing")]
    [InlineData("http://camera-sim:9997/v3/config/paths/list")]
    [InlineData("http://camera-sim:9997/v3/config/paths/add")]
    [InlineData("http://camera-sim:9997/v3/config/paths/add/")]
    [InlineData("http://camera-sim:9997/cameras")]
    [InlineData("")]
    [InlineData("not a url")]
    public void Any_other_url_is_returned_unchanged(string urlFull) =>
        Redact(urlFull).ShouldBe(urlFull);

    private static string Redact(string urlFull) => CameraSimSpanRedaction.Redact(urlFull);
}
