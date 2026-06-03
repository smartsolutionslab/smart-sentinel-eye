using System.Reflection;
using SmartSentinelEye.ScenarioSimulator;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.EventHandlers;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ServiceDefaults;
using Wolverine;
using Wolverine.RabbitMQ;

// Dev-only Scenario Simulator (ADR-0111). Seeds the camera catalog from a
// realistic scenario and, driven by CameraRegisteredV1, provisions looping
// video on the config-clean camera-sim MediaMTX. Gated off in CI/E2E/prod by
// the AppHost; this host is simply never started there.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Scenario files (Scenarios/*.json) are copied next to the app; merge each so
// the IOptions<ScenarioOptions> binding sees the scenario definitions.
LoadScenarioFiles(builder);

builder.Services
    .AddOptions<ScenarioOptions>()
    .Bind(builder.Configuration.GetSection(ScenarioOptions.SectionName));

builder.Services
    .AddOptions<SimulatorOptions>()
    .Configure(options => BindRuntime(builder.Configuration, options));

builder.Services.AddSingleton(TimeProvider.System);

// Keycloak client_credentials token provider (scope sse.cameras.write).
builder.Services.AddHttpClient<KeycloakTokenProvider>();

// Camera-catalog REST client (POST /cameras), bearer-authenticated.
builder.Services.AddHttpClient<CameraCatalogClient>((sp, client) =>
    {
        SimulatorOptions options = Resolve(sp);
        client.BaseAddress = new Uri(options.CameraCatalogUrl);
    })
    .AddStandardResilienceHandler();

// camera-sim MediaMTX v3 control-plane client (provisions loop paths).
builder.Services.AddHttpClient<CameraSimProvisioner>((sp, client) =>
    {
        SimulatorOptions options = Resolve(sp);
        client.BaseAddress = new Uri(options.CameraSimApiUrl);
    })
    .AddStandardResilienceHandler();

builder.Services.AddHostedService<ScenarioSeeder>();

// Wolverine consumer of CameraRegisteredV1 (ADR-0088 per-module queue
// isolation). No Postgres outbox: a dev-only simulator does not need durable
// inbox/outbox — re-provisioning camera-sim on redelivery is idempotent.
builder.UseWolverine(opts =>
{
    string rabbitConnection =
        builder.Configuration.GetConnectionString("rabbitmq")
        ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required for the scenario simulator.");

    opts.UseRabbitMq(new Uri(rabbitConnection))
        .AutoProvision()
        .UseConventionalRouting(routing =>
            routing.QueueNameForListener(eventType => $"scenario-simulator.{eventType.FullName}"));

    opts.Discovery.IncludeAssembly(Assembly.GetExecutingAssembly());
});

builder.Services.AddScoped<CameraRegisteredSimHandler>();

IHost host = builder.Build();
await host.RunAsync();

static void LoadScenarioFiles(HostApplicationBuilder builder)
{
    string scenariosDir = Path.Combine(AppContext.BaseDirectory, "Scenarios");
    if (!Directory.Exists(scenariosDir))
    {
        return;
    }

    foreach (string file in Directory.EnumerateFiles(scenariosDir, "*.json"))
    {
        builder.Configuration.AddJsonFile(file, optional: true, reloadOnChange: false);
    }
}

// Resolve the simulator endpoints + credentials from Aspire-injected config.
// Aspire surfaces a referenced project's endpoint as `services:<name>:http:0`
// and a container's as `services:<name>:<endpoint>:0`; secrets/URLs the
// AppHost passes explicitly arrive as plain configuration keys.
static void BindRuntime(IConfiguration config, SimulatorOptions options)
{
    options.CameraCatalogUrl =
        config["services:camera-catalog:http:0"]
        ?? config["ScenarioSimulator:Runtime:CameraCatalogUrl"]
        ?? throw new InvalidOperationException("camera-catalog URL not configured (services:camera-catalog:http:0).");

    options.CameraSimApiUrl =
        config["services:camera-sim:api:0"]
        ?? config["ScenarioSimulator:Runtime:CameraSimApiUrl"]
        ?? throw new InvalidOperationException("camera-sim API URL not configured (services:camera-sim:api:0).");

    options.KeycloakUrl =
        config.GetConnectionString("keycloak")
        ?? config["services:keycloak:http:0"]
        ?? config["services:keycloak:https:0"]
        ?? throw new InvalidOperationException("Keycloak URL not configured for the scenario simulator.");

    options.Realm = config["ScenarioSimulator:Runtime:Realm"] ?? "smart-sentinel-eye";
    options.ClientId = config["ScenarioSimulator:Runtime:ClientId"] ?? "scenario-simulator";
    options.ClientSecret =
        config["ScenarioSimulator:Runtime:ClientSecret"]
        ?? throw new InvalidOperationException("scenario-simulator client secret not configured.");
    options.RtspHost = config["ScenarioSimulator:Runtime:RtspHost"] ?? "camera-sim:8554";
}

static SimulatorOptions Resolve(IServiceProvider sp) =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SimulatorOptions>>().Value;
