using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976, adversarial pass (T011). The camera-sim client as the simulator runs it:
/// ServiceDefaults (standard resilience handler, OpenTelemetry logging with scopes) and the
/// simulator's real appsettings.json. A failed provision throws into Wolverine, which logs the
/// exception, so the exception text is checked as well as every record and every open scope.
/// </summary>
public sealed class CameraSimProvisionerLogRedactionAdversarialTests
{
    private const string Secret = "SECRET-token";

    private const string Path = Secret + "/stream";

    private const string FactoryCategory = "System.Net.Http.HttpClient.CameraSimProvisioner";

    public enum Failure
    {
        ServerError,
        RefusedWithAnotherReason,
        ReplaceFails,
    }

    [Theory]
    [InlineData(Failure.ServerError)]
    [InlineData(Failure.RefusedWithAnotherReason)]
    [InlineData(Failure.ReplaceFails)]
    public async Task A_camera_sim_error_answer_never_carries_the_path_into_a_log_or_the_exception(Failure failure)
    {
        await using var rig = Rig.Build(Respond(failure));

        var exception = await Should.ThrowAsync<HttpRequestException>(rig.ProvisionAsync);

        exception.ToString().ShouldNotContain(Secret, customMessage: exception.ToString());
        rig.UnscopedDump.ShouldNotContain(Secret, customMessage: rig.Log.Dump);
    }

    [Fact]
    public async Task A_refused_connection_never_carries_the_path_into_a_log_or_the_exception()
    {
        await using var rig = Rig.Build(primary: null, baseAddress: new Uri($"http://127.0.0.1:{ClosedPort()}"));

        var exception = await Should.ThrowAsync<HttpRequestException>(rig.ProvisionAsync);

        exception.ToString().ShouldNotContain(Secret, customMessage: exception.ToString());
        rig.UnscopedDump.ShouldNotContain(Secret, customMessage: rig.Log.Dump);
    }

    [Fact]
    public async Task A_provision_cancelled_mid_request_never_carries_the_path_into_a_log_or_the_exception()
    {
        using CancellationTokenSource cancellation = new();
        await using var rig = Rig.Build(new HangingHandler(cancellation));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => rig.ProvisionAsync(cancellation.Token));

        exception.ToString().ShouldNotContain(Secret, customMessage: exception.ToString());
        rig.UnscopedDump.ShouldNotContain(Secret, customMessage: rig.Log.Dump);
    }

    [Fact]
    public async Task The_recorder_sees_the_request_uri_scope_when_a_line_is_written_inside_it()
    {
        await using var rig = Rig.Build(
            Respond(Failure.ServerError),
            overrides: new Dictionary<string, string?> { [$"Logging:LogLevel:{FactoryCategory}"] = "Information" },
            keepFactoryLoggers: true);

        await Should.ThrowAsync<HttpRequestException>(rig.ProvisionAsync);

        rig.Log.Entries
            .Where(entry => entry.Category == $"{FactoryCategory}.ClientHandler")
            .ShouldContain(entry => entry.Scopes.Contains(Secret, StringComparison.Ordinal), rig.Log.Dump);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(Failure failure) =>
        request =>
        {
            var isAdd = request.RequestUri!.AbsolutePath.StartsWith("/v3/config/paths/add/", StringComparison.Ordinal);

            return failure switch
            {
                Failure.ServerError => Text(HttpStatusCode.InternalServerError, $"boom at {request.RequestUri}"),
                Failure.RefusedWithAnotherReason => Text(HttpStatusCode.BadRequest, "invalid path name"),
                _ when isAdd => Text(HttpStatusCode.BadRequest, "path already exists"),
                _ => Text(HttpStatusCode.InternalServerError, "replace failed"),
            };
        };

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8) };

    private static int ClosedPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal sealed class Rig(IHost host, ScopedRecordingLoggerProvider log) : IAsyncDisposable
    {
        public ScopedRecordingLoggerProvider Log { get; } = log;

        /// <summary>
        /// Every record without its open scopes. IHttpClientFactory's request scope carries the path,
        /// which is a separate finding; these tests pin what the records themselves say.
        /// </summary>
        public string UnscopedDump =>
            string.Join(
                Environment.NewLine,
                Log.Entries.Select(entry => $"[{entry.Category}] {entry.Level} {entry.Message} | {entry.Structured} | {entry.Exception}"));

        public static Rig Build(
            Func<HttpRequestMessage, HttpResponseMessage>? respond,
            Dictionary<string, string?>? overrides = null,
            bool keepFactoryLoggers = false) =>
            Build(respond is null ? null : new StubHandler(respond), new Uri("http://camera-sim:9997"), overrides, keepFactoryLoggers: keepFactoryLoggers);

        public static Rig Build(HttpMessageHandler primary) =>
            Build(primary, new Uri("http://camera-sim:9997"), overrides: null);

        public static Rig Build(
            HttpMessageHandler? primary,
            Uri baseAddress,
            Dictionary<string, string?>? overrides = null,
            Action<IServiceCollection>? configure = null,
            bool keepFactoryLoggers = false)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production,
                Args = [],
            });
            builder.Configuration.AddInMemoryCollection(overrides ?? []);
            builder.AddServiceDefaults();

            ScopedRecordingLoggerProvider log = new();
            builder.Logging.AddProvider(log);

            builder.Services.Configure<SimulatorOptions>(options => options.CameraSimApiUrl = baseAddress.ToString());
            var client = keepFactoryLoggers
                ? builder.Services.AddHttpClient<CameraSimProvisioner>(http => http.BaseAddress = baseAddress)
                : builder.Services.AddCameraSimProvisionerClient();

            if (primary is not null)
            {
                client.ConfigurePrimaryHttpMessageHandler(() => primary);
            }

            configure?.Invoke(builder.Services);

            return new Rig(builder.Build(), log);
        }

        public Task ProvisionAsync() => ProvisionAsync(CancellationToken.None);

        public Task ProvisionAsync(CancellationToken cancellationToken) =>
            host.Services
                .GetRequiredService<CameraSimProvisioner>()
                .ProvisionLabelledPathAsync(Path, "Line 7 Spare", Guid.CreateVersion7(), cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
                return;
            }

            host.Dispose();
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class HangingHandler(CancellationTokenSource caller) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await caller.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
