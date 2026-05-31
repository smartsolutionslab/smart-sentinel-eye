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

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

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

// Spec 009 ADR-0101: the postgres image carries the timescaledb
// extension so the audit-observability hypertable + compression
// work. We use the single-node `timescale/timescaledb` community
// image rather than the `-ha` (Spilo/Patroni) variant: it is far
// lighter for dev/CI and still provides hypertables AND compression
// (a TSL feature the audit migration requires). The `-oss` tags
// drop compression, so the community (non-oss) tag is required.
// Every other context's database remains plain Postgres tables on
// the same server.
IResourceBuilder<PostgresServerResource> postgres = builder
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
    mediamtx.WithLifetime(ContainerLifetime.Persistent);
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
        context.EnvironmentVariables["SSE_JWT_JWKS_URI"] = ReferenceExpression.Create(
            $"{keycloak.GetEndpoint("http")}/realms/smart-sentinel-eye/protocol/openid-connect/certs");
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
    .WaitFor(auditDb);

var cameraCatalog = builder
    .AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog")
    .WithHttpEndpoint()
    .WithReference(cameraCatalogDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);

IResourceBuilder<ProjectResource> streamDistribution = builder
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
var layoutComposition = builder
    .AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition")
    .WithHttpEndpoint()
    .WithReference(layoutCompositionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
IResourceBuilder<ProjectResource> eventIngestion = builder
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
builder
    .AddProject<Projects.SmartSentinelEye_Automation_Api>("automation")
    .WithHttpEndpoint()
    .WithReference(automationDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
builder
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
    auditObservability.WithEnvironment(
        "AuditObservability__Retention__TickInterval", "00:00:03");
}

// React apps per ADR-0074: two pnpm-workspace apps under apps/. Skipped in
// test mode so the integration suite doesn't start two Node dev servers.
if (isRunMode && !isE2ETests)
{
    builder.AddNpmApp("management-web", "../../apps/management-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5173)
        .WithReference(cameraCatalog)
        .WithReference(layoutComposition)
        .WithReference(overlayDesigner)
        .WithReference(systemVariables)
        .WithReference(auditObservability)
        .WithExternalHttpEndpoints();

    builder.AddNpmApp("kiosk-web", "../../apps/kiosk-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5174)
        .WithReference(cameraCatalog)
        .WithReference(streamDistribution)
        .WithReference(layoutComposition)
        .WithReference(overlayDesigner)
        .WithReference(systemVariables)
        .WithReference(eventIngestion)
        .WithReference(keycloak)
        .WithExternalHttpEndpoints();
}

await builder.Build().RunAsync();
