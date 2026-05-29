using System.Collections.Concurrent;
using System.Text;
using Aspire.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that boots the Aspire AppHost in E2ETests mode
/// (ephemeral containers, no React dev servers) and exposes per-API
/// HttpClients + DbContext factories + auth helpers (ADR-0068).
///
/// Tests join the collection via [Collection(AspireCollection.Name)] so the
/// containers are spun up once per xUnit assembly run.
/// </summary>
public sealed partial class AspireFixture : IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(8);

    private DistributedApplication? _app;
    private readonly ConcurrentQueue<string> _cameraCatalogLogTail = new();
    private CancellationTokenSource? _logCts;
    private Task? _logTailTask;

    // xUnit invokes DisposeAsync; this IDisposable.Dispose only exists to
    // satisfy CA1001 (the type owns _logCts). Resource disposal happens in
    // DisposeAsync above.
    public void Dispose() => _logCts?.Dispose();

    public DistributedApplication App =>
        _app ?? throw new InvalidOperationException("Aspire AppHost has not been started.");

    public HttpClient CameraCatalog { get; private set; } = null!;

    public HttpClient StreamDistribution { get; private set; } = null!;

    public HttpClient LayoutComposition { get; private set; } = null!;

    public HttpClient OverlayDesigner { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        string[] parameters =
        [
            "Parameters:PostgresUser=postgres",
            "Parameters:PostgresPassword=testpassword",
            "Parameters:KeycloakPassword=testkeycloak",
            "Parameters:RabbitMqPassword=testmessaging",
            "E2ETests=true",
        ];

        IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(parameters)
                .ConfigureAwait(false);

        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await builder.BuildAsync().ConfigureAwait(false);

        _logCts = new CancellationTokenSource();
        _logTailTask = Task.Run(() => TailCameraCatalogLogsAsync(_logCts.Token));

        using CancellationTokenSource cts = new(StartupTimeout);

        try
        {
            await _app.StartAsync(cts.Token).ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("keycloak", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("migrations", KnownResourceStates.Finished, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("camera-catalog", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("mediamtx", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("stream-distribution", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("layout-composition", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("overlay-designer", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await WaitForKeycloakRealmAsync(cts.Token).ConfigureAwait(false);
            await WaitForMediaMtxAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            string logTail = string.Join('\n', _cameraCatalogLogTail.TakeLast(120));
            Dictionary<string, string> states = await CaptureResourceStateMapAsync().ConfigureAwait(false);
            string failedLogs = await CaptureFailedResourceLogsAsync(states).ConfigureAwait(false);
            throw new TimeoutException(
                $"Aspire AppHost did not start within {StartupTimeout.TotalMinutes} minutes.\n" +
                $"Resource states:\n{FormatResourceStates(states)}\n" +
                $"Failed-resource logs:\n{failedLogs}\n" +
                $"Last camera-catalog logs:\n{logTail}",
                ex);
        }

        CameraCatalog = App.CreateHttpClient("camera-catalog");
        StreamDistribution = App.CreateHttpClient("stream-distribution");
        LayoutComposition = App.CreateHttpClient("layout-composition");
        OverlayDesigner = App.CreateHttpClient("overlay-designer");
    }

    public async Task DisposeAsync()
    {
        CameraCatalog?.Dispose();
        StreamDistribution?.Dispose();
        LayoutComposition?.Dispose();
        OverlayDesigner?.Dispose();

        if (_logCts is not null)
        {
            await _logCts.CancelAsync().ConfigureAwait(false);
            if (_logTailTask is not null)
            {
                try { await _logTailTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
            }
            _logCts.Dispose();
            _logCts = null;
        }

        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await ((IAsyncDisposable)_app).DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, string>> CaptureResourceStateMapAsync()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal);
        if (_app is null) return states;

        using CancellationTokenSource snapshot = new(TimeSpan.FromSeconds(3));
        try
        {
            await foreach (var evt in _app.ResourceNotifications.WatchAsync(snapshot.Token))
            {
                states[evt.Resource.Name] = evt.Snapshot.State?.Text ?? "(unknown)";
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        return states;
    }

    private static string FormatResourceStates(Dictionary<string, string> states) =>
        states.Count == 0
            ? "  (app not built)"
            : string.Join('\n', states.OrderBy(kv => kv.Key).Select(kv => $"  {kv.Key}: {kv.Value}"));

    /// <summary>
    /// On a startup timeout, dump recent stdout for every resource that
    /// hasn't reached Running/Finished. The CI Linux boot failures
    /// (#423) don't repro on Windows dev boxes, so a crashed service's
    /// own log is the only window into *why* — the fixture otherwise
    /// tails camera-catalog alone.
    /// </summary>
    private async Task<string> CaptureFailedResourceLogsAsync(Dictionary<string, string> states)
    {
        if (_app is null) return "(app not built)";

        string[] failed = states
            .Where(kv => kv.Value is not ("Running" or "Finished"))
            .Select(kv => kv.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (failed.Length == 0) return "(no failed resources)";

        Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers =
            _app.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();

        StringBuilder report = new();
        foreach (string name in failed)
        {
            report.Append("---- ").Append(name).AppendLine(" ----");
            report.AppendLine(await CaptureOneResourceLogAsync(loggers, name).ConfigureAwait(false));
        }

        return report.ToString();
    }

    private static async Task<string> CaptureOneResourceLogAsync(
        Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers, string name)
    {
        List<string> lines = [];
        using CancellationTokenSource perResource = new(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (IReadOnlyList<LogLine> batch in
                loggers.WatchAsync(name).WithCancellation(perResource.Token))
            {
                foreach (LogLine line in batch)
                {
                    lines.Add(line.Content);
                }
            }
        }
        catch (OperationCanceledException) { /* bounded read */ }
        catch (Exception logEx) when (logEx is not OperationCanceledException)
        {
            lines.Add($"(log capture failed: {logEx.GetType().Name})");
        }

        return lines.Count == 0 ? "(no logs captured)" : string.Join('\n', lines.TakeLast(60));
    }

    private async Task TailCameraCatalogLogsAsync(CancellationToken cancellationToken)
    {
        if (_app is null) return;

        try
        {
            Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers =
                _app.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();

            await foreach (IReadOnlyList<LogLine> batch in
                loggers.WatchAsync("camera-catalog").WithCancellation(cancellationToken))
            {
                foreach (LogLine line in batch)
                {
                    _cameraCatalogLogTail.Enqueue(line.Content);
                    while (_cameraCatalogLogTail.Count > 200)
                    {
                        _cameraCatalogLogTail.TryDequeue(out _);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception)
        {
            // diagnostic-only path; never block startup
        }
    }

    private async Task WaitForKeycloakRealmAsync(CancellationToken cancellationToken)
    {
        using HttpClient probe = App.CreateHttpClient("keycloak");
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                HttpResponseMessage response = await probe.GetAsync(
                    "/realms/smart-sentinel-eye/.well-known/openid-configuration",
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // realm import still in progress
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Keycloak realm 'smart-sentinel-eye' was not reachable after 60 attempts. " +
            "Check the realm-import logs in the Aspire dashboard.");
    }

    private async Task WaitForMediaMtxAsync(CancellationToken cancellationToken)
    {
        using HttpClient probe = App.CreateHttpClient("mediamtx", "api");
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                HttpResponseMessage response = await probe.GetAsync(
                    "/v3/paths/list", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // MediaMTX still booting
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MediaMTX /v3/paths/list was not reachable after 60 attempts.");
    }
}
