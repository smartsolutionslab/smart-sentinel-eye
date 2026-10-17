using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 393 / #3019. The camera-sim client as the simulator runs it (ServiceDefaults tracing,
/// the real client registration) against a real loopback socket, with every exported span
/// captured. The runtime's System.Net.Http client span carries the request URL as url.full, and
/// for a non-asset camera the URL path is operator-typed text.
/// </summary>
public sealed class CameraSimProvisionerSpanTests
{
    private const string Secret = "SECRET-token";

    private const string Path = Secret + "/stream";

    [Fact]
    public async Task The_add_span_exports_the_verb_not_the_path()
    {
        await using var server = await CameraSimLoopbackServer.StartAsync(_ => (200, string.Empty));
        await using var rig = await SpanRig.StartAsync(server.BaseAddress);

        await rig.ProvisionAsync();

        var spans = rig.ClientSpans("POST");
        spans.Count.ShouldBe(1, rig.Dump);
        spans[0].Tag("url.full").ShouldBe($"{server.BaseAddress}v3/config/paths/add/Redacted", rig.Dump);
        rig.Dump.ShouldNotContain(Secret, customMessage: rig.Dump);
        server.Requests.ShouldBe([$"POST /v3/config/paths/add/{Path}"]);
    }

    [Fact]
    public async Task The_replace_fallback_span_exports_the_verb_not_the_path()
    {
        await using var server = await CameraSimLoopbackServer.StartAsync(
            position => position == 0 ? (400, "path already exists") : (200, string.Empty));
        await using var rig = await SpanRig.StartAsync(server.BaseAddress);

        await rig.ProvisionAsync();

        var spans = rig.ClientSpans("POST");
        spans.Count.ShouldBe(2, rig.Dump);
        spans[0].Tag("url.full").ShouldBe($"{server.BaseAddress}v3/config/paths/add/Redacted", rig.Dump);
        spans[1].Tag("url.full").ShouldBe($"{server.BaseAddress}v3/config/paths/replace/Redacted", rig.Dump);
        rig.Dump.ShouldNotContain(Secret, customMessage: rig.Dump);
        server.Requests.ShouldBe([$"POST /v3/config/paths/add/{Path}", $"POST /v3/config/paths/replace/{Path}"]);
    }

    [Fact]
    public async Task A_failing_add_span_exports_the_verb_not_the_path()
    {
        await using var server = await CameraSimLoopbackServer.StartAsync(_ => (500, "boom"));
        await using var rig = await SpanRig.StartAsync(server.BaseAddress);

        await Should.ThrowAsync<HttpRequestException>(rig.ProvisionAsync);

        var spans = rig.ClientSpans("POST");
        spans.Count.ShouldBe(1, rig.Dump);
        spans[0].Tag("url.full").ShouldBe($"{server.BaseAddress}v3/config/paths/add/Redacted", rig.Dump);
        rig.Dump.ShouldNotContain(Secret, customMessage: rig.Dump);
    }

    [Fact]
    public async Task A_span_outside_the_config_paths_api_is_unchanged()
    {
        await using var server = await CameraSimLoopbackServer.StartAsync(_ => (200, "{}"));
        await using var rig = await SpanRig.StartAsync(server.BaseAddress);

        using var client = rig.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        using var response = await client.GetAsync($"{server.BaseAddress}v3/paths/get/station-4-roughing");

        var spans = rig.ClientSpans("GET");
        spans.Count.ShouldBe(1, rig.Dump);
        spans[0].Tag("url.full").ShouldBe($"{server.BaseAddress}v3/paths/get/station-4-roughing", rig.Dump);
    }

    private sealed class SpanRig : IAsyncDisposable
    {
        private readonly IHost host;

        private readonly CollectingExporter exporter;

        private SpanRig(IHost host, CollectingExporter exporter)
        {
            this.host = host;
            this.exporter = exporter;
        }

        public IServiceProvider Services => host.Services;

        public static async Task<SpanRig> StartAsync(Uri baseAddress)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production,
                Args = [],
            });
            builder.AddServiceDefaults();
            builder.Services.Configure<SimulatorOptions>(options => options.CameraSimApiUrl = baseAddress.ToString());
            builder.Services.AddCameraSimProvisionerClient();

            CollectingExporter exporter = new();
            builder.Services.ConfigureOpenTelemetryTracerProvider(
                (_, tracing) => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));

            var host = builder.Build();
            await host.StartAsync();
            host.Services.GetRequiredService<TracerProvider>();

            return new SpanRig(host, exporter);
        }

        public Task ProvisionAsync() =>
            host.Services
                .GetRequiredService<CameraSimProvisioner>()
                .ProvisionLabelledPathAsync(Path, "Line 7 Spare", Guid.CreateVersion7(), CancellationToken.None);

        public List<Activity> ClientSpans(string method)
        {
            host.Services.GetRequiredService<TracerProvider>().ForceFlush();

            return exporter.Captured
                .Where(span => span.Kind == ActivityKind.Client && span.Tag("http.request.method") == method)
                .ToList();
        }

        /// <summary>Every tag of every client span plus its display name (the in-process loopback server legitimately sees the path, so its spans are not the simulator's export).</summary>
        public string Dump =>
            string.Join(
                Environment.NewLine,
                exporter.Captured.Where(span => span.Kind == ActivityKind.Client).Select(span =>
                    $"{span.Kind} '{span.DisplayName}' {string.Join(", ", span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))}"));

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class CollectingExporter : BaseExporter<Activity>
    {
        private readonly List<Activity> captured = [];

        public IReadOnlyList<Activity> Captured
        {
            get
            {
                lock (captured)
                {
                    return [.. captured];
                }
            }
        }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (captured)
            {
                foreach (var activity in batch)
                {
                    captured.Add(activity);
                }
            }

            return ExportResult.Success;
        }
    }
}

internal static class ActivityTagExtensions
{
    public static string? Tag(this Activity activity, string key) => activity.GetTagItem(key) as string;
}
