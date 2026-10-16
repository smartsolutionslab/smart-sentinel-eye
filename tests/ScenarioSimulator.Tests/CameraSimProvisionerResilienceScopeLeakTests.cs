using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// FINDING (not committed). Polly's retry strategy logs "Execution attempt" at Information for
/// every camera-sim request, success included, under category "Polly", which appsettings.json does
/// not filter. The record is written inside IHttpClientFactory's "HTTP {HttpMethod} {Uri}" scope,
/// and ServiceDefaults exports scopes (IncludeScopes = true), so it carries
/// Uri = http://camera-sim:9997/v3/config/paths/add/&lt;operator URL path&gt;.
/// </summary>
public sealed class CameraSimProvisionerResilienceScopeLeakTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task No_exported_record_carries_the_camera_sim_path_in_its_scope(HttpStatusCode status)
    {
        await using var rig = CameraSimProvisionerLogRedactionAdversarialTests.Rig.Build(
            new StatusHandler(status),
            new Uri("http://camera-sim:9997"));

        await Should.NotThrowAsync(() => rig.ProvisionAsync().ContinueWith(_ => { }, TaskScheduler.Default));

        rig.Log.Dump.ShouldNotContain("SECRET-token", customMessage: rig.Log.Dump);
    }

    [Fact]
    public async Task A_camera_sim_attempt_timeout_exports_no_record_whose_scope_carries_the_path()
    {
        await using var rig = CameraSimProvisionerLogRedactionAdversarialTests.Rig.Build(
            new SlowHandler(),
            new Uri("http://camera-sim:9997"),
            configure: services => services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(200)));

        await Should.ThrowAsync<Exception>(rig.ProvisionAsync);

        rig.Log.Dump.ShouldNotContain("SECRET-token", customMessage: rig.Log.Dump);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
