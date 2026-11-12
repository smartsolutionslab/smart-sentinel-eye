using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Every REST context is also reachable at <c>/api/&lt;context&gt;/...</c> (ADR-0192), with the prefix removed, the same CORS and
/// rate-limit policy, <c>Authorization</c> untouched and <c>X-Fab</c> removed. The <c>streams/authorize</c> carve-out holds under the
/// prefix, and an unknown path under <c>/api</c> is the gateway's 404, never the shell's HTML.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayApiPrefixTests
{
    public static TheoryData<string> Contexts => [.. GatewayTestHost.Contexts];

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task Gateway_forwards_an_api_route_with_the_prefix_removed_and_the_credential_untouched(string context)
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/{context}/things?sort=name");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "abc");
        request.Headers.Add("X-Fab", "munich");

        var response = await gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var seen = destination.Requests.Single();
        seen.Path.ShouldBe("/things");
        seen.Query.ShouldBe("?sort=name");
        seen.Headers["Authorization"].ShouldBe("Bearer abc");
        seen.Headers.ContainsKey("X-Fab").ShouldBeFalse();
    }

    [Fact]
    public async Task Gateway_applies_the_same_rate_limit_to_an_api_route()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides =>
        {
            GatewayTestHost.PointContextsAt(overrides, destination.Address);
            overrides["RateLimiting:PermitLimit"] = "2";
        });

        var first = await gateway.Client.GetAsync("/api/camera-catalog/a");
        var second = await gateway.Client.GetAsync("/api/camera-catalog/b");
        var third = await gateway.Client.GetAsync("/api/camera-catalog/c");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Gateway_does_not_forward_the_anonymous_stream_authorize_hook_under_the_api_prefix()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));

        var response = await gateway.Client.PostAsync("/api/stream-distribution/streams/authorize", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        destination.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "/stream-distribution/streams/authorize")]
    [InlineData("PUT", "/stream-distribution/streams/authorize")]
    [InlineData("PATCH", "/stream-distribution/streams/authorize")]
    [InlineData("DELETE", "/stream-distribution/streams/authorize")]
    [InlineData("HEAD", "/stream-distribution/streams/authorize")]
    [InlineData("GET", "/api/stream-distribution/streams/authorize")]
    [InlineData("PUT", "/api/stream-distribution/streams/authorize")]
    [InlineData("PATCH", "/api/stream-distribution/streams/authorize")]
    [InlineData("DELETE", "/api/stream-distribution/streams/authorize")]
    [InlineData("HEAD", "/api/stream-distribution/streams/authorize")]
    public async Task Gateway_does_not_forward_the_stream_authorize_hook_for_any_method(string method, string path)
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        destination.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/stream-distribution/streams/authorize")]
    [InlineData("/api/stream-distribution/streams/authorize")]
    public async Task Gateway_answers_a_cors_preflight_to_the_stream_authorize_hook_with_404_and_no_allowance(string path)
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await gateway.Client.SendAsync(preflight);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        destination.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_still_forwards_a_sibling_stream_path_to_the_destination()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));

        var response = await gateway.Client.GetAsync("/stream-distribution/streams/abc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        destination.Requests.Single().Path.ShouldBe("/streams/abc");
    }

    [Fact]
    public async Task Gateway_answers_an_unknown_api_path_with_404_rather_than_the_shell_when_the_ui_is_hosted()
    {
        await using var stack = await UiProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/api/not-a-context/things");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        stack.Shell.Requests.ShouldBeEmpty();
        stack.Rest.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_answers_an_unknown_api_path_with_404_when_no_ui_is_hosted()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));

        var response = await gateway.Client.GetAsync("/api/not-a-context/things");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        destination.Requests.ShouldBeEmpty();
    }

    /// <summary>
    /// The two route lists are written out in <c>appsettings.json</c> and must not drift: every bare route has an <c>api-</c> twin
    /// with the same cluster and policies, and no twin exists without a bare route.
    /// </summary>
    [Fact]
    public void Every_bare_route_has_an_api_twin_with_the_same_policies_and_no_twin_is_an_orphan()
    {
        var path = System.IO.Path.Combine(GatewayTestHost.GatewayProjectDirectory(), "appsettings.json");
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        var bare = routes.EnumerateObject().Where(route => !route.Name.StartsWith("api-", StringComparison.Ordinal)).Select(route => route.Name).Order().ToList();
        var twins = routes.EnumerateObject().Where(route => route.Name.StartsWith("api-", StringComparison.Ordinal)).Select(route => route.Name["api-".Length..]).Order().ToList();

        bare.ShouldBe(GatewayTestHost.Contexts.Order().ToList(), "the bare routes are exactly the nine contexts");
        twins.ShouldBe(bare, "every bare route has an api- twin and no twin is an orphan");

        foreach (var context in bare)
        {
            var original = routes.GetProperty(context);
            var twin = routes.GetProperty($"api-{context}");

            twin.GetProperty("ClusterId").GetString().ShouldBe(original.GetProperty("ClusterId").GetString(), context);
            twin.GetProperty("CorsPolicy").GetString().ShouldBe(original.GetProperty("CorsPolicy").GetString(), context);
            twin.GetProperty("RateLimiterPolicy").GetString().ShouldBe(original.GetProperty("RateLimiterPolicy").GetString(), context);
            twin.GetProperty("Match").GetProperty("Path").GetString().ShouldBe($"/api/{context}/{{**catch-all}}", context);

            var transforms = twin.GetProperty("Transforms").EnumerateArray().ToList();
            transforms.Any(transform => TransformValue(transform, "PathRemovePrefix") == $"/api/{context}").ShouldBeTrue($"{context}: prefix removal");
            transforms.Any(transform => TransformValue(transform, "RequestHeaderRemove") == "X-Fab").ShouldBeTrue($"{context}: X-Fab removal");
        }
    }

    private static string? TransformValue(JsonElement transform, string name) =>
        transform.TryGetProperty(name, out var value) ? value.GetString() : null;
}
