// Aspire composition root for Smart Sentinel Eye (ADR-0024).
//
// Resources wired here:
// - postgres: per-context database "camera-catalog-db" + Keycloak's own "keycloak-db"
// - rabbitmq: shared broker with management plugin in dev
// - keycloak: realm "smart-sentinel-eye" imported from Realms/ folder
// - migrations: MigrationRunner that runs once on startup
// - one project per bounded context, each WithReference()'d to the resources it consumes
//
// Test mode (E2ETests=true) makes the containers ephemeral; dev mode pins
// persistent lifetimes + data volumes so the stack survives restarts.

var builder = DistributedApplication.CreateBuilder(args);
bool isRunMode = builder.ExecutionContext.IsRunMode;
bool isE2ETests = bool.TryParse(builder.Configuration["E2ETests"], out bool e2e) && e2e;

var postgresUser = builder.AddParameter("PostgresUser", "postgres");
var postgresPassword = builder.AddParameter("PostgresPassword", "dev-only-postgres-password", secret: true);
var keycloakPassword = builder.AddParameter("KeycloakPassword", "dev-only-keycloak-admin", secret: true);
// Mirrors the `identity-admin` client secret seeded in
// Realms/smart-sentinel-eye-realm.json. The Identity API reads it as
// `Keycloak:AdminClientSecret` to mint the realm-management
// service-account token (spec 008 ADR-0100).
var identityAdminClientSecret = builder.AddParameter("IdentityAdminClientSecret", "dev-only-identity-admin-secret", secret: true);
var rabbitPassword = builder.AddParameter("RabbitMqPassword", "dev-only-rabbit-password", secret: true);
// Mirrors the dev-only `scenario-simulator` confidential client seeded in
// Realms/smart-sentinel-eye-realm.json. The Scenario Simulator worker
// (ADR-0111) reads it as `ScenarioSimulator:Runtime:ClientSecret` to mint a
// client_credentials token (scope sse.cameras.write) for seeding the catalog.
var scenarioSimulatorClientSecret = builder.AddParameter("ScenarioSimulatorClientSecret", "dev-only-scenario-simulator-secret", secret: true);

// Spec 009 ADR-0101: the postgres image carries the timescaledb
// extension so the audit-observability hypertable + compression
// work. We use the single-node `timescale/timescaledb` community
// image rather than the `-ha` (Spilo/Patroni) variant: it is far
// lighter for dev/CI and still provides hypertables AND compression
// (a TSL feature the audit migration requires). The `-oss` tags
// drop compression, so the community (non-oss) tag is required.
// Every other context's database remains plain Postgres tables on
// the same server.
var postgres = builder
    .AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithImage("timescale/timescaledb")
    .WithImageTag("2.27.1-pg17");

if (isRunMode && !isE2ETests)
{
    postgres
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume()
        .WithPgAdmin();
}

var cameraCatalogDb = postgres.AddDatabase("camera-catalog-db");
var streamDistributionDb = postgres.AddDatabase("stream-distribution-db");
var layoutCompositionDb = postgres.AddDatabase("layout-composition-db");
var overlayDesignerDb = postgres.AddDatabase("overlay-designer-db");
var systemVariablesDb = postgres.AddDatabase("system-variables-db");
var eventIngestionDb = postgres.AddDatabase("event-ingestion-db");
var automationDb = postgres.AddDatabase("automation-db");
var identityDb = postgres.AddDatabase("identity-db");
var auditDb = postgres.AddDatabase("audit-db");

var rabbitmq = builder
    .AddRabbitMQ("rabbitmq", password: rabbitPassword)
    .WithImageTag("4-management-alpine")
    .WithManagementPlugin();

if (isRunMode && !isE2ETests)
{
    rabbitmq
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithRealmImport("../AppHost/Realms");

if (isRunMode && !isE2ETests)
{
    keycloak
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

// MediaMTX SFU brings RTSP ingest + WHEP playback (spec 002 T003, ADR-0011).
// MediaMTX is the runtime source of truth for live paths; the stream-distribution
// service is the durable source of truth and reconciles paths on startup.
var mediamtx = builder
    .AddContainer("mediamtx", "bluenviron/mediamtx", "latest-ffmpeg")
    .WithBindMount("Resources/mediamtx.yml", "/mediamtx.yml")
    .WithHttpEndpoint(targetPort: 9997, name: "api")
    .WithHttpEndpoint(targetPort: 8889, name: "whep")
    .WithEndpoint(targetPort: 8554, name: "rtsp", scheme: "tcp");

if (isRunMode && !isE2ETests)
{
    // WebRTC media (ICE) must reach the host browser, which can't route to the
    // container IP MediaMTX advertises by default. Aspire's DCP proxies container
    // endpoints on random TCP host ports, which ICE can't use (the advertised
    // candidate port wouldn't match and UDP wouldn't traverse), so publish the
    // ICE mux directly with a raw docker port map (host 8189 -> container 8189,
    // UDP + TCP) and advertise the host loopback. Dev-only; prod browsers share
    // an L2 with the SFU (spec 002), so local candidates suffice.
    mediamtx
        .WithLifetime(ContainerLifetime.Persistent)
        .WithContainerRuntimeArgs("--publish", "8189:8189/udp", "--publish", "8189:8189/tcp")
        .WithEnvironment("MTX_WEBRTCADDITIONALHOSTS", "127.0.0.1");
}


// Mosquitto MQTT broker for spec 006 EventIngestion (ADR-0095). Each
// PLC and inference device publishes on a per-device topic; the
// event-ingestion service subscribes with a fab-scoped wildcard.
//
// Spec 008 ADR-0100: the image is built from `mosquitto/Dockerfile`,
// which adds a custom Go auth plugin (`mosquitto/plugin/`) that
// validates Keycloak-minted RS256 JWTs against the realm JWKS with no
// per-CONNECT round-trip. The upstream iegomez/mosquitto-go-auth
// plugin can't do this (HMAC-only signatures, no JWKS) — see the ADR.
// Non-JWT passwords fall through to the password_file, so the spec 006
// EventIngestion subscriber + station-4 / camera-12 seeds keep working.
//
// The plugin needs the realm JWKS, so AppHost injects the
// container-reachable Keycloak URL as SSE_JWT_JWKS_URI and waits for
// Keycloak so the set is fetchable on first connect (the plugin also
// retries until it is).
var mosquitto = builder
    .AddDockerfile("mosquitto", "mosquitto")
    .WithBindMount("mosquitto/mosquitto.conf", "/mosquitto/config/mosquitto.conf")
    .WithBindMount("mosquitto/passwords.txt", "/mosquitto/config/passwords.txt")
    .WithBindMount("mosquitto/acl.txt", "/mosquitto/config/acl.txt")
    .WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "tcp")
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["SSE_JWT_JWKS_URI"] = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/smart-sentinel-eye/protocol/openid-connect/certs");
    })
    .WaitFor(keycloak);

if (isRunMode && !isE2ETests)
{
    mosquitto
        .WithLifetime(ContainerLifetime.Persistent)
        .WithVolume("mosquitto-data", "/mosquitto/data");
}

// MinIO object storage (ADR-0009) — used by AuditObservability
// (spec 009 ADR-0101) for the per-chunk cold archive once a
// hypertable chunk crosses the 90-day boundary. The
// CommunityToolkit.Aspire.Hosting.Minio integration injects a
// `ConnectionStrings:minio` value into every consumer that
// `WithReference`s it; the Infrastructure project resolves an
// `IMinioClient` from that via `AddMinioClient("minio")`.
var minio = builder.AddMinioContainer("minio");

if (isRunMode && !isE2ETests)
{
    minio
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

// MigrationRunner orchestrates all per-context migrations and exits (ADR-0067).
var migrations = builder
    .AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations")
    .WithReference(cameraCatalogDb)
    .WithReference(streamDistributionDb)
    .WithReference(layoutCompositionDb)
    .WithReference(overlayDesignerDb)
    .WithReference(systemVariablesDb)
    .WithReference(eventIngestionDb)
    .WithReference(automationDb)
    .WithReference(identityDb)
    .WithReference(auditDb)
    .WaitFor(cameraCatalogDb)
    .WaitFor(streamDistributionDb)
    .WaitFor(layoutCompositionDb)
    .WaitFor(overlayDesignerDb)
    .WaitFor(systemVariablesDb)
    .WaitFor(eventIngestionDb)
    .WaitFor(automationDb)
    .WaitFor(identityDb)
    .WaitFor(auditDb)
    // Dashboard grouping: nest the one-shot migration runner under postgres (the
    // data tier) — it migrates every per-context database, then exits.
    .WithParentRelationship(postgres);

var cameraCatalog = builder
    .AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog")
    .WithHttpEndpoint()
    .WithReference(cameraCatalogDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);

var streamDistribution = builder
    .AddProject<Projects.SmartSentinelEye_StreamDistribution_Api>("stream-distribution")
    .WithHttpEndpoint()
    .WithReference(streamDistributionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(mediamtx.GetEndpoint("api"))
    .WithReference(mediamtx.GetEndpoint("whep"))
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(mediamtx);

// In run mode the stream-distribution service runs as a host process, so the
// mediamtx container can't reach it via the `stream-distribution` service name
// baked into mediamtx.yml's authHTTPAddress (that DNS name resolves only among
// containers on the AppHost network). Override the WHEP auth hook with the
// Aspire-resolved, container-reachable endpoint so every WHEP open can be
// authorized; without it MediaMTX's auth callback fails with "no such host" and
// returns 401 to the browser. Publish mode containerizes stream-distribution, so
// the baked-in service name is correct there and this override is run-only.
if (isRunMode)
{
    mediamtx.WithEnvironment(context =>
    {
        context.EnvironmentVariables["MTX_AUTHHTTPADDRESS"] =
            ReferenceExpression.Create($"{streamDistribution.GetEndpoint("http")}/streams/authorize");
    });
}

var layoutComposition = builder
    .AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition")
    .WithHttpEndpoint()
    .WithReference(layoutCompositionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var eventIngestion = builder
    .AddProject<Projects.SmartSentinelEye_EventIngestion_Api>("event-ingestion")
    .WithHttpEndpoint()
    .WithReference(eventIngestionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(mosquitto.GetEndpoint("mqtt"))
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(mosquitto);
var overlayDesigner = builder
    .AddProject<Projects.SmartSentinelEye_OverlayDesigner_Api>("overlay-designer")
    .WithHttpEndpoint()
    .WithReference(overlayDesignerDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var systemVariables = builder
    .AddProject<Projects.SmartSentinelEye_SystemVariables_Api>("system-variables")
    .WithHttpEndpoint()
    .WithReference(systemVariablesDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(overlayDesigner)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(overlayDesigner);
var automation = builder
    .AddProject<Projects.SmartSentinelEye_Automation_Api>("automation")
    .WithHttpEndpoint()
    .WithReference(automationDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var identity = builder
    .AddProject<Projects.SmartSentinelEye_Identity_Api>("identity")
    .WithHttpEndpoint()
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__AdminClientSecret", identityAdminClientSecret)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var auditObservability = builder
    .AddProject<Projects.SmartSentinelEye_AuditObservability_Api>("audit-observability")
    .WithHttpEndpoint()
    .WithReference(auditDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(minio)
    .WithEnvironment("Minio__Bucket", "audit-archive")
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(minio);

if (isE2ETests)
{
    // Sweep retention every few seconds in the integration suite so the
    // round-trip test isn't waiting on the production daily timer.
    auditObservability.WithEnvironment("AuditObservability__Retention__TickInterval", "00:00:03");
}

// ADR-0106: single YARP API gateway at the edge — fronts all nine context REST
// APIs via service discovery (#1002). CORS/TLS (#1003) and rate limiting (#1004)
// follow. Realtime WebSocket (ADR-0076) and WebRTC media stay direct, off the
// gateway, so the latency budget (constitution §IV) is untouched.
var apiGateway = builder
    .AddProject<Projects.SmartSentinelEye_ApiGateway>("api-gateway")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(cameraCatalog)
    .WithReference(streamDistribution)
    .WithReference(layoutComposition)
    .WithReference(eventIngestion)
    .WithReference(overlayDesigner)
    .WithReference(systemVariables)
    .WithReference(auditObservability)
    .WithReference(automation)
    .WithReference(identity);

// HA (#1005): run >= 2 gateway replicas so the single REST front door is not a
// single point of failure (ADR-0106). Kept to one instance under E2E tests so
// the gateway routing/rate-limit integration tests resolve a single endpoint.
if (!isE2ETests)
{
    apiGateway.WithReplicas(2);
}

// React apps per ADR-0074: two pnpm-workspace apps under apps/. Skipped in
// test mode so the integration suite doesn't start two Node dev servers.
// Endpoints are proxyless (isProxied: false): the Vite dev server binds the
// fixed PORT (5173/5174) directly, so DCP must NOT also proxy that port — a
// shared port makes Vite fail to start with "Port is already in use".
if (isRunMode && !isE2ETests)
{
    // REST goes through the gateway (#1005): each app gets VITE_API_GATEWAY_URL
    // and calls `${gateway}/<context>/...` cross-origin (the gateway's CORS
    // policy allows the app origins, #1003). Auth is direct to Keycloak (OIDC):
    // VITE_KEYCLOAK_URL is the same Aspire endpoint the services validate tokens
    // against, so the issuer matches (ServiceDefaults.AddBearerAuthentication).
    // The realtime WebSocket hub (ADR-0076, LayoutComposition) and WebRTC media
    // stay direct — a direct reference to layout-composition keeps that URL
    // resolvable, off the gateway.
    //
    // WaitFor(layoutComposition) is load-bearing, not just ordering polish: each
    // app's vite.config.ts reads `services__layout-composition__http__0` ONCE, at
    // config-evaluation time, to build the `/hubs` proxy. If Vite boots before the
    // endpoint resolves, the proxy is silently omitted for the life of the process
    // and every `/hubs/layouts/negotiate` 404s forever — the kiosk then sits on a
    // permanently-stuck "live updates degraded" badge (spec 011 FR-006/FR-010).
    builder.AddNpmApp("management-web", "../../apps/management-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
        .WithReference(apiGateway)
        .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
        .WithReference(keycloak)
        .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
        .WithReference(layoutComposition)
        .WaitFor(layoutComposition)
        .WithExternalHttpEndpoints()
        // Dashboard grouping: nest the SPAs under the gateway they call.
        .WithParentRelationship(apiGateway);

    builder.AddNpmApp("kiosk-web", "../../apps/kiosk-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5174, isProxied: false)
        .WithReference(apiGateway)
        .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
        .WithReference(keycloak)
        .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
        .WithReference(layoutComposition)
        .WaitFor(layoutComposition)
        .WithExternalHttpEndpoints()
        .WithParentRelationship(apiGateway);
}

// Scenario Simulator (ADR-0111 M1) — dev-only, gated `isRunMode && !isE2ETests`
// so CI/E2E/prod never see it and the main `mediamtx.yml` stays clean.
//
// - camera-sim: a second, config-clean MediaMTX. Holds NO static paths; the
//   worker provisions a `runOnDemand` loop path per catalog camera via the HTTP
//   API (9997). The loop clip is bind-mounted at /media.
// - scenario-simulator worker: seeds the camera catalog over HTTP
//   (client_credentials via Keycloak), then — driven by CameraRegisteredV1 over
//   RabbitMQ — provisions the looping video on camera-sim. Waits for the things
//   it calls on startup.
if (isRunMode && !isE2ETests)
{
    var cameraSim = builder
        .AddContainer("camera-sim", "bluenviron/mediamtx", "latest-ffmpeg")
        .WithBindMount("Resources/camera-sim.yml", "/mediamtx.yml")
        .WithBindMount("Resources/sim-loop.mp4", "/media/sim-loop.mp4")
        .WithHttpEndpoint(targetPort: 9997, name: "api")
        .WithEndpoint(targetPort: 8554, name: "rtsp", scheme: "tcp")
        .WithLifetime(ContainerLifetime.Persistent);

    var scenarioSimulator = builder
        .AddProject<Projects.SmartSentinelEye_ScenarioSimulator>("scenario-simulator")
        .WithReference(cameraCatalog)
        .WithReference(cameraSim.GetEndpoint("api"))
        .WithReference(overlayDesigner)
        .WithReference(automation)
        .WithReference(layoutComposition)
        .WithReference(mosquitto.GetEndpoint("mqtt"))
        .WithReference(rabbitmq)
        .WithReference(keycloak)
        .WithEnvironment("ScenarioSimulator__Runtime__ClientSecret", scenarioSimulatorClientSecret)
        .WaitFor(cameraCatalog)
        .WaitFor(cameraSim)
        .WaitFor(overlayDesigner)
        .WaitFor(automation)
        .WaitFor(layoutComposition)
        .WaitFor(mosquitto)
        .WaitFor(rabbitmq)
        .WaitFor(keycloak);

    // Dashboard grouping: nest the dev-only camera-sim MediaMTX under the
    // simulator worker that provisions + drives its loop paths.
    cameraSim.WithParentRelationship(scenarioSimulator);
}

await builder.Build().RunAsync();
