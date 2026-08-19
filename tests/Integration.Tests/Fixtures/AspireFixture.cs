using System.Collections.Concurrent;
using System.Text;
using Aspire.Hosting;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

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

    /// <summary>
    /// Services whose console output is tailed for diagnostics. A resource can
    /// be <c>Running</c> and still fault every request, and the client-side
    /// <see cref="HttpRequestException"/> a test sees carries only "500" — the
    /// server's exception lives here.
    /// </summary>
    private static readonly string[] TailedResources = ["camera-catalog", "automation", "identity", "event-ingestion"];

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _logTails = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _logTailFailures = new(StringComparer.Ordinal);
    private CancellationTokenSource? _logCts;
    private Task[]? _logTailTasks;

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

    public HttpClient AuditObservability { get; private set; } = null!;

    public HttpClient EventIngestion { get; private set; } = null!;

    public HttpClient SystemVariables { get; private set; } = null!;

    public HttpClient Automation { get; private set; } = null!;

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

        // The startup budget covers the whole bring-up, not just StartAsync.
        // A hung CreateAsync or BuildAsync previously had no token at all and
        // would block the run indefinitely.
        using CancellationTokenSource cts = new(StartupTimeout);

        IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(parameters, cts.Token)
                .ConfigureAwait(false);

        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await builder.BuildAsync(cts.Token).ConfigureAwait(false);

        _logCts = new CancellationTokenSource();

        try
        {
            await _app.StartAsync(cts.Token).ConfigureAwait(false);

            // Subscribe only once the resources exist. Started before
            // StartAsync, WatchAsync has nothing to watch and completes
            // immediately — which is why the first attempt at this captured
            // nothing and reported it as "the service said nothing".
            foreach (string resource in TailedResources)
            {
                _logTails.TryAdd(resource, new ConcurrentQueue<string>());
            }

            _logTailTasks = TailedResources
                .Select(resource => Task.Run(() => TailResourceLogsAsync(resource, _logCts.Token), _logCts.Token))
                .ToArray();

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

            await _app.ResourceNotifications
                .WaitForResourceAsync("audit-observability", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("event-ingestion", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("system-variables", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await _app.ResourceNotifications
                .WaitForResourceAsync("automation", KnownResourceStates.Running, cts.Token)
                .ConfigureAwait(false);

            await WaitForKeycloakRealmAsync(cts.Token).ConfigureAwait(false);
            await WaitForMediaMtxAsync(cts.Token).ConfigureAwait(false);

            // Running only means the process launched — it does not mean Kestrel
            // has bound its listener, so the waits above can return while the
            // first request would still be refused. OverlayDesigner is the one
            // that lost this race on the Linux runner, timing out ~9 tests while
            // passing on Windows dev boxes.
            await WaitForServiceHealthAsync("overlay-designer", cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            string logTail = RecentLogs("camera-catalog");
            Dictionary<string, string> states = await CaptureResourceStateMapAsync().ConfigureAwait(false);
            string failedLogs = await CaptureFailedResourceLogsAsync(states).ConfigureAwait(false);
            throw new TimeoutException(
                $"Aspire AppHost did not start within {StartupTimeout.TotalMinutes} minutes.\n" +
                $"Resource states:\n{FormatResourceStates(states)}\n" +
                $"Failed-resource logs:\n{failedLogs}\n" +
                $"Last camera-catalog logs:\n{logTail}",
                ex);
        }

        // Name the endpoint explicitly. Every service declares WithHttpEndpoint()
        // in AppHost and none declares an https one, but an ASP.NET project also
        // carries an https launch profile — so leaving the choice to
        // CreateHttpClient's default made these clients depend on which endpoint
        // that default happened to prefer. Aspire 13.4.6 changed that preference
        // to https, and CI's integration job has no dev cert (only the e2e job
        // runs `dotnet dev-certs https`), so every request failed with
        // UntrustedRoot — 54 of 75 tests, while passing on a developer machine
        // where the cert is trusted. Naming the endpoint removes the ambient
        // dependency entirely (#1133).
        CameraCatalog = App.CreateHttpClient("camera-catalog", "http");
        StreamDistribution = App.CreateHttpClient("stream-distribution", "http");
        LayoutComposition = App.CreateHttpClient("layout-composition", "http");
        OverlayDesigner = App.CreateHttpClient("overlay-designer", "http");
        AuditObservability = App.CreateHttpClient("audit-observability", "http");
        EventIngestion = App.CreateHttpClient("event-ingestion", "http");
        SystemVariables = App.CreateHttpClient("system-variables", "http");
        Automation = App.CreateHttpClient("automation", "http");
    }

    public async Task DisposeAsync()
    {
        CameraCatalog?.Dispose();
        StreamDistribution?.Dispose();
        LayoutComposition?.Dispose();
        OverlayDesigner?.Dispose();
        AuditObservability?.Dispose();
        EventIngestion?.Dispose();

        if (_logCts is not null)
        {
            await _logCts.CancelAsync().ConfigureAwait(false);
            if (_logTailTasks is not null)
            {
                try { await Task.WhenAll(_logTailTasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
            }
            _logCts.Dispose();
            _logCts = null;
        }

        if (_app is not null)
        {
            // No token by design: teardown runs after _logCts is disposed and
            // must release the stack even if the run is being torn down.
            await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await ((IAsyncDisposable)_app).DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, string>> CaptureResourceStateMapAsync()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal);
        if (_app is null)
        {
            return states;
        }

        using CancellationTokenSource snapshot = new(TimeSpan.FromSeconds(3));
        try
        {
            await foreach (ResourceEvent evt in _app.ResourceNotifications.WatchAsync(snapshot.Token))
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
        if (_app is null)
        {
            return "(app not built)";
        }

        string[] failed = states
            .Where(kv => kv.Value is not ("Running" or "Finished"))
            .Select(kv => kv.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (failed.Length == 0)
        {
            return "(no failed resources)";
        }

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

    /// <summary>
    /// The last few hundred lines a tailed service wrote. Use it when a test
    /// gets a status it cannot explain — a 500 tells you nothing on its own,
    /// and CI has no other route to the service's stack trace.
    /// </summary>
    public string RecentLogs(string resourceName, int lines = 120)
    {
        if (!_logTails.TryGetValue(resourceName, out ConcurrentQueue<string>? tail))
        {
            return $"(not tailed — add '{resourceName}' to AspireFixture.TailedResources)";
        }

        string[] recent = tail.TakeLast(lines).ToArray();

        if (recent.Length > 0)
        {
            return string.Join(Environment.NewLine, recent);
        }

        return _logTailFailures.TryGetValue(resourceName, out string? failure)
            ? $"(log tail failed: {failure})"
            : "(tail subscribed but the resource emitted nothing)";
    }

    private async Task TailResourceLogsAsync(string resourceName, CancellationToken cancellationToken)
    {
        if (_app is null)
        {
            return;
        }

        ConcurrentQueue<string> tail = _logTails.GetOrAdd(resourceName, _ => new ConcurrentQueue<string>());

        try
        {
            Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers =
                _app.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.ResourceLoggerService>();

            await foreach (IReadOnlyList<LogLine> batch in
                loggers.WatchAsync(resourceName).WithCancellation(cancellationToken))
            {
                foreach (LogLine line in batch)
                {
                    tail.Enqueue(line.Content);
                    while (tail.Count > 400)
                    {
                        tail.TryDequeue(out _);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            // Still must not block startup — but record why, so an empty tail
            // is distinguishable from a broken one.
            _logTailFailures[resourceName] = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task WaitForKeycloakRealmAsync(CancellationToken cancellationToken)
    {
        using HttpClient probe = CreateKeycloakClient();
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

    private async Task WaitForServiceHealthAsync(string resourceName, CancellationToken cancellationToken)
    {
        // Name the endpoint for the same reason the per-API clients do: an
        // unnamed one resolves to whichever endpoint Aspire happens to prefer.
        using HttpClient probe = App.CreateHttpClient(resourceName, "http");
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                HttpResponseMessage response = await probe.GetAsync(
                    "/health", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // listener not bound yet
            }
            catch (TimeoutRejectedException)
            {
                // Slow to start rather than not started. Aspire's client carries
                // the standard resilience handler, and its timeout surfaces as
                // this rather than as HttpRequestException — so without this the
                // first slow probe escapes a loop written to retry sixty times,
                // faults InitializeAsync, and fails every test in the collection
                // with an error naming none of them.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"{resourceName} /health was not reachable after 60 attempts.");
    }
}
