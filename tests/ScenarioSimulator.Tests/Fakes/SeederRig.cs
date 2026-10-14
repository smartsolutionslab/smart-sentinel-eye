using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>One request a <see cref="SeederRig"/> collaborator received, captured at send time.</summary>
internal sealed record RigRequest(string Method, string Path, string PathAndQuery, string Body)
{
    /// <summary>A top-level string property of the JSON body, or <c>null</c> when absent.</summary>
    public string? Property(string name) =>
        JsonDocument.Parse(Body).RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
}

/// <summary>
/// Records every request and answers with <paramref name="respond"/>, falling back to
/// <paramref name="fallback"/> when it returns <c>null</c>. The body is read inside the
/// handler because the clients dispose each request message after sending.
/// </summary>
internal sealed class RigHandler(
    Func<RigRequest, HttpResponseMessage?>? respond,
    Func<RigRequest, HttpResponseMessage> fallback) : HttpMessageHandler
{
    private readonly List<RigRequest> requests = [];

    public IReadOnlyList<RigRequest> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        RigRequest captured = new(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.PathAndQuery, body);

        lock (requests)
        {
            requests.Add(captured);
        }

        return respond?.Invoke(captured) ?? fallback(captured);
    }
}

/// <summary>
/// A <see cref="ScenarioSeeder"/> wired to recording stub servers for each collaborator,
/// run to completion deterministically (<c>ExecuteTask</c>, not a polling delay). Every
/// server succeeds by default; a test overrides the one whose behaviour it is about.
/// </summary>
internal sealed class SeederRig(
    Func<RigRequest, HttpResponseMessage?>? cameras = null,
    Func<RigRequest, HttpResponseMessage?>? rules = null,
    IOptions<ScenarioOptions>? wallOptions = null)
{
    public static readonly Guid Overlay = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private static readonly ConcurrentDictionary<string, Guid> CameraIdentifiers = new(StringComparer.Ordinal);

    public RigHandler Cameras { get; } = new(cameras, DefaultCameraAnswer);

    public RigHandler Overlays { get; } = new(
        null,
        request => request.Path == "/overlays"
            ? Json(HttpStatusCode.OK, $"\"{Overlay}\"")
            : new HttpResponseMessage(HttpStatusCode.OK));

    public RigHandler Rules { get; } = new(
        rules,
        request => request.Path switch
        {
            "/rules" when request.Method == "POST" => new HttpResponseMessage(HttpStatusCode.Created),
            var path when path.EndsWith("/publish", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

    public RigHandler Variables { get; } = new(
        null,
        request => request.Path == "/system-variables"
            ? new HttpResponseMessage(HttpStatusCode.Created)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

    public RecordingLogger<ScenarioSeeder> Log { get; } = new();

    public AssetCorrelationTable Correlation { get; } = new();

    /// <summary>The identifier the default camera server hands out for <paramref name="cameraName"/>.</summary>
    public static Guid CameraIdentifier(string cameraName) =>
        CameraIdentifiers.GetOrAdd(cameraName, _ => Guid.CreateVersion7());

    public IReadOnlyList<string> RegisteredCameras =>
        [.. Cameras.Requests
            .Where(request => request is { Method: "POST", Path: "/cameras" })
            .Select(request => request.Property("name")!)];

    public IReadOnlyList<RigRequest> RuleCreates =>
        [.. Rules.Requests.Where(request => request is { Method: "POST", Path: "/rules" })];

    public async Task RunAsync(ScenarioOptions options)
    {
        var wrapped = Options.Create(options);

        ScenarioSeeder seeder = new(
            new CameraCatalogClient(
                new HttpClient(Cameras) { BaseAddress = new Uri("https://cameras.test") },
                Tokens(),
                Simulator(),
                NullLogger<CameraCatalogClient>.Instance),
            new OverlayDesignerClient(
                new HttpClient(Overlays) { BaseAddress = new Uri("https://overlays.test") },
                Tokens(),
                NullLogger<OverlayDesignerClient>.Instance),
            new AutomationRulesClient(
                new HttpClient(Rules) { BaseAddress = new Uri("https://automation.test") },
                Tokens(),
                NullLogger<AutomationRulesClient>.Instance),
            new SystemVariablesClient(
                new HttpClient(Variables) { BaseAddress = new Uri("https://system-variables.test") },
                Tokens(),
                NullLogger<SystemVariablesClient>.Instance),
            Correlation,
            wrapped,
            new WallSeeder(
                new LayoutCompositionClient(
                    new HttpClient(new RigHandler(null, _ => throw new HttpRequestException("layouts unreachable")))
                    {
                        BaseAddress = new Uri("https://layouts.test"),
                    },
                    Tokens(),
                    NullLogger<LayoutCompositionClient>.Instance),
                Correlation,
                wallOptions ?? wrapped,
                NullLogger<WallSeeder>.Instance),
            Log);

        await seeder.StartAsync(CancellationToken.None);
        await seeder.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        await seeder.StopAsync(CancellationToken.None);
    }

    private static HttpResponseMessage DefaultCameraAnswer(RigRequest request)
    {
        if (request is { Method: "POST", Path: "/cameras" })
        {
            var camera = CameraIdentifier(request.Property("name")!);
            return Json(HttpStatusCode.Created, $"\"{camera}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static IOptions<SimulatorOptions> Simulator() =>
        Options.Create(new SimulatorOptions
        {
            KeycloakUrl = "https://keycloak.test",
            Realm = "smart-sentinel-eye",
            ClientId = "scenario-simulator",
            ClientSecret = "stub-secret",
            RtspHost = "rtsp.test",
        });

    private static KeycloakTokenProvider Tokens() =>
        new(
            new FakeHttpClientFactory(new HttpClient(new RigHandler(
                null,
                _ => Json(HttpStatusCode.OK, """{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""")))),
            Simulator(),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);
}
