namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// <c>Ui:Mode=Static</c> serves the built kiosk and wall bundles at <c>/kiosk/</c> and <c>/wall/</c>: each prefix falls back to its own
/// <c>index.html</c> (never the shell's, never the other's), its files bypass the rate limiter, and a missing root stops the gateway at start.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayKioskUiStaticTests
{
    [Theory]
    [InlineData("/kiosk/", "kiosk-index")]
    [InlineData("/kiosk/layouts/abc", "kiosk-index")]
    [InlineData("/wall/", "wall-index")]
    [InlineData("/wall/walls/x", "wall-index")]
    public async Task Gateway_falls_back_to_the_prefixs_own_index_for_a_deep_link_without_an_extension(string path, string expected)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();

        var body = await stack.Gateway.Client.GetStringAsync(path);

        body.ShouldContain(expected);
        body.ShouldNotContain("shell-index");
    }

    [Theory]
    [InlineData("/kiosk/", "kiosk-index")]
    [InlineData("/kiosk/layouts/abc", "kiosk-index")]
    [InlineData("/kiosk/layouts/cam.01", "kiosk-index")]
    [InlineData("/kiosk/index.html", "kiosk-index")]
    [InlineData("/wall/walls/x", "wall-index")]
    [InlineData("/wall/walls/x.y", "wall-index")]
    public async Task Gateway_makes_a_browser_revalidate_the_kiosk_and_wall_entry_point(string path, string expected)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain(expected);
        response.Headers.CacheControl.ShouldNotBeNull().NoCache.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task Gateway_serves_a_present_kiosk_asset_with_nosniff_and_answers_a_missing_one_with_404()
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync(
            kioskFiles: [("assets/x.js", "kiosk-asset")]);

        var asset = await stack.Gateway.Client.GetAsync("/kiosk/assets/x.js");
        var missing = await stack.Gateway.Client.GetAsync("/kiosk/assets/nope.js");

        asset.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await asset.Content.ReadAsStringAsync()).ShouldBe("kiosk-asset");
        asset.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/kiosk", "/kiosk/")]
    [InlineData("/wall", "/wall/")]
    public async Task Gateway_redirects_the_bare_prefix_to_the_slashed_path(string path, string expectedLocation)
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        ((int)response.StatusCode).ShouldBeInRange(300, 399);
        response.Headers.Location!.OriginalString.ShouldBe(expectedLocation);
    }

    [Fact]
    public async Task Gateway_does_not_rate_limit_kiosk_and_wall_pages()
    {
        await using var stack = await GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides["RateLimiting:PermitLimit"] = "100");

        List<HttpStatusCode> statuses = [];
        for (var attempt = 0; attempt < 150; attempt++)
        {
            statuses.Add((await stack.Gateway.Client.GetAsync("/kiosk/layouts/abc")).StatusCode);
            statuses.Add((await stack.Gateway.Client.GetAsync("/wall/walls/x")).StatusCode);
        }

        statuses.Count(status => status == HttpStatusCode.TooManyRequests).ShouldBe(0);
        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Ui:Static:KioskRoot")]
    [InlineData("Ui:Static:WallRoot")]
    public async Task Gateway_refuses_to_start_when_a_root_is_not_configured(string configurationKey)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides.Remove(configurationKey)));

        exception.Message.ShouldContain(configurationKey);
    }

    [Theory]
    [InlineData("Ui:Static:KioskRoot")]
    [InlineData("Ui:Static:WallRoot")]
    public async Task Gateway_refuses_to_start_when_a_root_is_not_a_directory(string configurationKey)
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            GatewayUiStaticTests.StaticUiStack.StartAsync(overrides => overrides[configurationKey] = missing));

        exception.Message.ShouldContain(configurationKey);
    }

    [Theory]
    [InlineData("kiosk", "Ui:Static:KioskRoot")]
    [InlineData("wall", "Ui:Static:WallRoot")]
    public async Task Gateway_refuses_to_start_when_a_root_has_no_index_html(string root, string configurationKey)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            GatewayUiStaticTests.StaticUiStack.StartAsync(removeEntryFile: (root, "index.html")));

        exception.Message.ShouldContain(configurationKey);
        exception.Message.ShouldContain("index.html");
    }
}
