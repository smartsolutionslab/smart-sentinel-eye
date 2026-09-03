using System.Collections.Concurrent;
using System.Globalization;
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
    ///
    /// A resource earns its place by having a <c>RecentLogs</c> call site, and
    /// <c>LogTailCoverageTests</c> fails the build when the two disagree — the
    /// list is hand-maintained, which is how four names stayed missing while
    /// seven call sites returned a placeholder instead of a log (issue #2053).
    /// </summary>
    private static readonly string[] TailedResources =
    [
        "camera-catalog",
        "automation",
        "identity",
        "event-ingestion",
        "stream-distribution",
        "overlay-designer",
        "layout-composition",
        "system-variables",
    ];

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

    /// <summary>
    /// One resource's snapshot: state, exit code, and every health report with
    /// its description.
    ///
    /// <para>
    /// <b>The console tail is the wrong instrument for a resource that failed to
    /// start</b> (#2038). A process that never launched has no stdout, so an
    /// empty tail is indistinguishable from a silent crash. The snapshot is
    /// where the orchestrator records what it observed, and the exit code is the
    /// datum that separates the two: present means the process ran and died,
    /// absent means it never ran at all.
    /// </para>
    /// </summary>
    public async Task<string> ResourceDiagnosticsAsync(string resourceName)
    {
        if (_app is null)
        {
            return "(no application)";
        }

        List<string> lines = [];
        using CancellationTokenSource snapshot = new(TimeSpan.FromSeconds(3));

        try
        {
            await foreach (ResourceEvent evt in _app.ResourceNotifications.WatchAsync(snapshot.Token))
            {
                if (!string.Equals(evt.Resource.Name, resourceName, StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add(
                    $"state={evt.Snapshot.State?.Text ?? "(unknown)"} "
                    + $"exitCode={(evt.Snapshot.ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "(none)")} "
                    + $"started={evt.Snapshot.StartTimeStamp?.ToString("O", CultureInfo.InvariantCulture) ?? "(never)"} "
                    + $"stopped={evt.Snapshot.StopTimeStamp?.ToString("O", CultureInfo.InvariantCulture) ?? "(none)"}");

                foreach (HealthReportSnapshot report in evt.Snapshot.HealthReports)
                {
                    lines.Add(
                        $"  health {report.Name}={report.Status}: "
                        + $"{report.Description ?? "(no description)"} {report.ExceptionText ?? string.Empty}".TrimEnd());
                }
            }
        }
        catch (OperationCanceledException) { /* expected — the watch is bounded above */ }

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(no snapshot observed)";
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
                _exitCodes[evt.Resource.Name] = evt.Snapshot.ExitCode;
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        return states;
    }

    // `Finished` alone does not say whether a service shut down cleanly or
    // died: both land there. The exit code is what separates them, and not
    // having it is why the one occurrence of #1918 could not be diagnosed
    // after the fact. Captured alongside the state so the next report answers
    // the question instead of raising it.
    private readonly Dictionary<string, int?> _exitCodes = new(StringComparer.Ordinal);

    /// <summary>
    /// Which resources are worth dumping logs for after a startup timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `Finished` used to count as healthy for everything, which hid exactly
    /// the failure it needed to explain: a long-running service that exits
    /// during startup lands in `Finished`, not `Failed`. When `automation`
    /// did that (#1918) the report dumped eleven idle rebuilders and nothing
    /// for the one resource the exception named.
    /// </para>
    /// <para>
    /// So `Finished` is success only for the one-shot resources it is success
    /// for, and `NotStarted` rebuilders — dev-time helpers that never run on
    /// their own — are dropped rather than reported as failures with no logs.
    /// </para>
    /// </remarks>
    internal static string[] SelectResourcesToReport(Dictionary<string, string> states) =>
        states
            .Where(kv => !IsHealthy(kv.Key, kv.Value))
            .Select(kv => kv.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static bool IsHealthy(string name, string state) =>
        state is "Running"
        || (state is "Finished" && IsOneShot(name))
        || (state is "NotStarted" && name.EndsWith("-rebuilder", StringComparison.Ordinal));

    // Resources that run once and stop; finishing is how they succeed.
    private static bool IsOneShot(string name) =>
        name is "migrations" || name.StartsWith("migrations-", StringComparison.Ordinal);

    private string FormatResourceStates(Dictionary<string, string> states) =>
        FormatResourceStates(states, _exitCodes);

    /// <summary>
    /// One line per resource. A resource that exited carries its exit code,
    /// because `Finished` alone does not distinguish a clean shutdown from a
    /// death — which is exactly what #1918's single occurrence could not answer.
    /// </summary>
    internal static string FormatResourceStates(
        Dictionary<string, string> states,
        Dictionary<string, int?> exitCodes) =>
        states.Count == 0
            ? "  (app not built)"
            : string.Join(
                '\n',
                states.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
                    exitCodes.TryGetValue(kv.Key, out int? exit) && exit is not null
                        ? $"  {kv.Key}: {kv.Value} (exit code {exit})"
                        : $"  {kv.Key}: {kv.Value}"));

    /// <summary>
    /// On a startup timeout, dump recent stdout for every resource that did
    /// not end up healthy. The CI Linux boot failures (#423) don't repro on
    /// Windows dev boxes, so a crashed service's own log is the only window
    /// into *why* — the fixture otherwise tails camera-catalog alone.
    /// </summary>
    private async Task<string> CaptureFailedResourceLogsAsync(Dictionary<string, string> states)
    {
        if (_app is null)
        {
            return "(app not built)";
        }

        string[] failed = SelectResourcesToReport(states);

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

    private async Task<string> CaptureOneResourceLogAsync(
        Aspire.Hosting.ApplicationModel.ResourceLoggerService loggers, string name)
    {
        if (!TryResolveResourceId(name, out string resourceId))
        {
            return "(no DCP instance for this resource — it has published no snapshot)";
        }

        List<string> lines = [];
        using CancellationTokenSource perResource = new(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (IReadOnlyList<LogLine> batch in
                loggers.WatchAsync(resourceId).WithCancellation(perResource.Token))
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

    /// <summary>
    /// The DCP instance id behind an app-model name — <c>camera-catalog</c>
    /// resolves to something like <c>camera-catalog-thwaubpm</c>.
    ///
    /// <para>
    /// <c>ResourceLoggerService.WatchAsync</c> keys on that id, not on the name.
    /// Passing the name yields a stream that carries nothing and throws nothing,
    /// which is why every tail in this fixture had been silently empty since it
    /// was written, and why the seven <c>RecentLogs</c> call sites of #2053
    /// printed "(tail subscribed but the resource emitted nothing)" for services
    /// that had plainly emitted something (#2054).
    /// </para>
    ///
    /// <para>
    /// <b>One resolver, both capture paths.</b> <c>CaptureOneResourceLogAsync</c>
    /// runs only on a startup timeout, which no test can provoke, so its
    /// correctness rests entirely on sharing this code with the tail loop that
    /// <c>LogTailDeliversIntegrationTests</c> does observe. A second copy would
    /// make that claim false.
    /// </para>
    ///
    /// <para>
    /// Returns <see langword="false"/> when the resource has published no
    /// snapshot yet. That is a wait, not a fault — callers must not record it.
    /// </para>
    /// </summary>
    private bool TryResolveResourceId(string resourceName, out string resourceId)
    {
        resourceId = string.Empty;

        if (_app is null)
        {
            return false;
        }

        if (!_app.ResourceNotifications.TryGetCurrentState(resourceName, out ResourceEvent? snapshot))
        {
            return false;
        }

        resourceId = snapshot.ResourceId;

        return !string.IsNullOrEmpty(resourceId);
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

            // **Re-subscribed in a loop, because a watch ends when its process
            // does.** A restarted resource is a new process, and the first
            // subscription completes the moment the old one exits — so a single
            // `await foreach` stops tailing at exactly the event worth tailing.
            // #2038's failure reported "the resource emitted nothing" for a
            // service that had plainly emitted something; what it had really
            // lost was the subscription.
            while (!cancellationToken.IsCancellationRequested)
            {
                // **Resolved on every turn of the loop, and never hoisted out of
                // it.** The instance id changes on every restart as well as on
                // every boot, so a resolve lifted above the loop re-subscribes to
                // the process that just died and the resource goes permanently
                // quiet — #2038's symptom, reintroduced by the fix for #2054.
                // The position of these three lines is the whole correctness
                // argument, and it looks exactly like something to hoist.
                if (!TryResolveResourceId(resourceName, out string resourceId))
                {
                    // Not a failure: the tails are launched before the
                    // WaitForResourceAsync calls, so a resource may simply not
                    // have published a snapshot yet. A queue that is still
                    // filling must not be reported as broken — that is the same
                    // misleading diagnostic this whole change exists to remove.
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    continue;
                }

                await foreach (IReadOnlyList<LogLine> batch in
                    loggers.WatchAsync(resourceId).WithCancellation(cancellationToken))
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

                // The stream ended without cancellation: the process went away,
                // or the id we resolved was already stale when we watched it.
                // Wait, then go round and re-resolve — the delay is what bounds
                // the spin, so it has to stay on the re-resolving path.
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
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
