using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Spec 491 (#3245), T015, User Story 1, Q1 option A (also spike (c)). The shell has pages at <c>/system-variables</c> and
/// <c>/event-ingestion/...</c>, the same first segment as two bare REST routes. A bare route therefore answers only requests that are
/// not page navigations: a browser reload or typed address sends <c>Sec-Fetch-Dest: document</c> and falls through to the shell;
/// <c>fetch</c>, scripts and test clients (no such value, or another one) still reach the REST route. The <c>/api</c> routes carry
/// no such condition.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayNavigationRuleTests
{
    public static TheoryData<string> Contexts => [.. GatewayTestHost.Contexts];

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task Gateway_sends_a_request_with_no_fetch_destination_to_the_rest_service(string context)
    {
        await using var stack = await UiProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync($"/{context}/things");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stack.Rest.Requests.Single().Path.ShouldBe("/things");
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("script")]
    [InlineData("iframe")]
    public async Task Gateway_sends_a_request_whose_fetch_destination_is_not_a_document_to_the_rest_service(string destination)
    {
        await using var stack = await UiProxyStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/system-variables/things");
        request.Headers.Add("Sec-Fetch-Dest", destination);

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stack.Rest.Requests.Single().Path.ShouldBe("/things");
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task Gateway_answers_a_page_navigation_to_a_bare_context_path_from_the_shell(string context)
    {
        await using var stack = await UiProxyStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{context}/things");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var echo = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        echo.RootElement.GetProperty("name").GetString().ShouldBe("shell");
        echo.RootElement.GetProperty("path").GetString().ShouldBe($"/{context}/things");
        stack.Rest.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/system-variables")]
    [InlineData("/event-ingestion/types")]
    public async Task Gateway_shows_the_shell_page_when_a_browser_reloads_a_path_the_shell_shares_with_a_rest_route(string path)
    {
        await using var stack = await UiProxyStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stack.Shell.Requests.Single().Path.ShouldBe(path);
        stack.Rest.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_does_not_apply_the_navigation_rule_to_the_api_routes()
    {
        await using var stack = await UiProxyStack.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system-variables/things");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await stack.Gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        stack.Rest.Requests.Single().Path.ShouldBe("/things");
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_still_answers_a_page_navigation_to_a_bare_path_from_the_rest_service_when_no_ui_is_hosted()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/system-variables/things");
        request.Headers.Add("Sec-Fetch-Dest", "document");

        var response = await gateway.Client.SendAsync(request);

        // No UI hosted means no page to fall through to, so the rule is not applied and a person opening a REST path in a
        // browser (for example a /health check) gets today's answer.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        destination.Requests.Single().Path.ShouldBe("/things");
    }
}
