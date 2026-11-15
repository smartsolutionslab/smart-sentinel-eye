namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// In <c>Ui:Mode=Static</c> every HTML page the gateway serves carries a Content-Security-Policy and four companion headers; files and
/// REST answers carry none of them, and a missing or malformed origin stops the gateway at start.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewaySecurityHeadersStaticTests
{
    private const string ProductionPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self' http://identity.test http://hub.test:5000 ws://hub.test:5000 http://media.test:8889; "
        + "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";

    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()";

    private static readonly string[] Keys = ["IdentityProvider", "LayoutHub", "Media"];

    public static IEnumerable<object[]> EveryKey() => Keys.Select(key => new object[] { key });

    public static IEnumerable<object[]> EveryKeyWithABadValue()
    {
        string[] badValues =
        [
            string.Empty,
            "relative/path",
            "ftp://x",
            "http://a.test;script-src *",
            "http://a.test 'x'",
        ];

        return Keys.SelectMany(key => badValues.Select(value => new object[] { key, value }));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/layouts/123")]
    [InlineData("/kiosk/")]
    [InlineData("/wall/")]
    [InlineData("/remotes/cameras/index.html")]
    public async Task Gateway_stamps_the_production_policy_and_the_four_companions_on_every_html_page(string path)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides["Ui:Static:CamerasRoot"] = WithAnIndexPage(overrides));

        var response = await stack.Gateway.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ProductionPolicy]);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["SAMEORIGIN"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["same-origin"]);
        response.Headers.GetValues("Permissions-Policy").ShouldBe([PermissionsPolicy]);
    }

    [Fact]
    public async Task Gateway_stamps_the_policy_on_a_head_request_for_the_shell()
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ProductionPolicy]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["SAMEORIGIN"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["same-origin"]);
        response.Headers.GetValues("Permissions-Policy").ShouldBe([PermissionsPolicy]);
    }

    [Theory]
    [InlineData("/assets/app.js")]
    [InlineData("/api/camera-catalog/x")]
    public async Task Gateway_leaves_a_script_and_a_rest_answer_without_a_policy(string path)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets/app.js")]
    [InlineData("/api/camera-catalog/x")]
    public async Task Gateway_never_sends_strict_transport_security(string path)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Gateway_reduces_an_origin_given_with_a_path_to_its_scheme_host_and_port()
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync(overrides =>
            overrides["Ui:ContentSecurityPolicy:LayoutHub"] = "http://hub.test:5000/hubs/layouts/");

        var response = await stack.Gateway.Client.GetAsync("/");

        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ProductionPolicy]);
    }

    [Theory]
    [MemberData(nameof(EveryKey))]
    public async Task Gateway_refuses_to_start_when_an_origin_is_not_configured(string key)
    {
        var configurationKey = $"Ui:ContentSecurityPolicy:{key}";

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides.Remove(configurationKey)));

        exception.Message.ShouldContain(configurationKey);
    }

    [Theory]
    [MemberData(nameof(EveryKeyWithABadValue))]
    public async Task Gateway_refuses_to_start_when_an_origin_is_empty_relative_or_carries_policy_syntax(string key, string value)
    {
        var configurationKey = $"Ui:ContentSecurityPolicy:{key}";

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides[configurationKey] = value));

        exception.Message.ShouldContain(configurationKey);
    }

    // A cameras root with a page in it, so the remote's index.html has something to serve.
    private static string WithAnIndexPage(Dictionary<string, string?> overrides)
    {
        var root = overrides["Ui:Static:CamerasRoot"]!;
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "index.html"), "<html>cameras-index</html>");
        return root;
    }
}
