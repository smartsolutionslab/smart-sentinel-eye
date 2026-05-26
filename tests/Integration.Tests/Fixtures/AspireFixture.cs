using System.Collections.Concurrent;
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

            await WaitForKeycloakRealmAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            string logTail = string.Join('\n', _cameraCatalogLogTail.TakeLast(120));
            string resourceStates = await CaptureResourceStatesAsync().ConfigureAwait(false);
            throw new TimeoutException(
                $"Aspire AppHost did not start within {StartupTimeout.TotalMinutes} minutes.\n" +
                $"Resource states:\n{resourceStates}\n" +
                $"Last camera-catalog logs:\n{logTail}",
                ex);
        }

        CameraCatalog = App.CreateHttpClient("camera-catalog");
    }

    public async Task DisposeAsync()
    {
        CameraCatalog?.Dispose();

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

    private async Task<string> CaptureResourceStatesAsync()
    {
        if (_app is null) return "(app not built)";

        using CancellationTokenSource snapshot = new(TimeSpan.FromSeconds(3));
        Dictionary<string, string> states = new(StringComparer.Ordinal);

        try
        {
            await foreach (var evt in _app.ResourceNotifications.WatchAsync(snapshot.Token))
            {
                states[evt.Resource.Name] = evt.Snapshot.State?.Text ?? "(unknown)";
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        return string.Join('\n', states.OrderBy(kv => kv.Key).Select(kv => $"  {kv.Key}: {kv.Value}"));
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
}
