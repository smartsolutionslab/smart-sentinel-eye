using System.Net.Http.Headers;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Spec 491 (#3245), T005. The in-process gateway (<see cref="GatewayTestHost"/>) must behave like the gateway the rest of the stack
/// already exercises through Aspire. These tests pin today's behaviour on the harness, green before any routing change, so a later
/// failure is the change and not the harness.
/// </summary>
public class GatewayHostCharacterisationTests
{
    [Fact]
    public async Task Gateway_forwards_a_bare_context_route_with_the_prefix_removed_and_the_credential_untouched()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/camera-catalog/cameras?sort=name");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "abc");
        request.Headers.Add("X-Fab", "munich");

        var response = await gateway.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var seen = destination.Requests.Single();
        seen.Path.ShouldBe("/cameras");
        seen.Query.ShouldBe("?sort=name");
        seen.Headers["Authorization"].ShouldBe("Bearer abc");
        seen.Headers.ContainsKey("X-Fab").ShouldBeFalse();
    }

    [Fact]
    public async Task Gateway_returns_404_for_a_path_with_no_configured_route()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));

        var response = await gateway.Client.GetAsync("/not-a-context/health");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        destination.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_does_not_forward_the_anonymous_stream_authorize_hook()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, destination.Address));

        var response = await gateway.Client.PostAsync("/stream-distribution/streams/authorize", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        destination.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_refuses_a_source_that_exceeds_the_limit_within_the_window()
    {
        await using var destination = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides =>
        {
            GatewayTestHost.PointContextsAt(overrides, destination.Address);
            overrides["RateLimiting:PermitLimit"] = "2";
        });

        var first = await gateway.Client.GetAsync("/camera-catalog/a");
        var second = await gateway.Client.GetAsync("/camera-catalog/b");
        var third = await gateway.Client.GetAsync("/camera-catalog/c");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
