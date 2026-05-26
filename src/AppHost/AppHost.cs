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

IResourceBuilder<ParameterResource> postgresUser =
    builder.AddParameter("PostgresUser", "postgres");
IResourceBuilder<ParameterResource> postgresPassword =
    builder.AddParameter("PostgresPassword", "dev-only-postgres-password", secret: true);
IResourceBuilder<ParameterResource> keycloakPassword =
    builder.AddParameter("KeycloakPassword", "dev-only-keycloak-admin", secret: true);
IResourceBuilder<ParameterResource> rabbitPassword =
    builder.AddParameter("RabbitMqPassword", "dev-only-rabbit-password", secret: true);

IResourceBuilder<PostgresServerResource> postgres = builder
    .AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithImageTag("17-alpine");

if (isRunMode && !isE2ETests)
{
    postgres
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume()
        .WithPgAdmin();
}

IResourceBuilder<PostgresDatabaseResource> cameraCatalogDb =
    postgres.AddDatabase("camera-catalog-db");

IResourceBuilder<RabbitMQServerResource> rabbitmq = builder
    .AddRabbitMQ("rabbitmq", password: rabbitPassword)
    .WithImageTag("4-management-alpine")
    .WithManagementPlugin();

if (isRunMode && !isE2ETests)
{
    rabbitmq
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

IResourceBuilder<KeycloakResource> keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithRealmImport("../AppHost/Realms");

if (isRunMode && !isE2ETests)
{
    keycloak
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

// MigrationRunner orchestrates all per-context migrations and exits (ADR-0067).
IResourceBuilder<ProjectResource> migrations = builder
    .AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations")
    .WithReference(cameraCatalogDb)
    .WaitFor(cameraCatalogDb);

IResourceBuilder<ProjectResource> cameraCatalog = builder
    .AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog")
    .WithHttpEndpoint()
    .WithReference(cameraCatalogDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);

builder.AddProject<Projects.SmartSentinelEye_StreamDistribution_Api>("stream-distribution").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_SystemVariables_Api>("system-variables").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_EventIngestion_Api>("event-ingestion").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_OverlayDesigner_Api>("overlay-designer").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_Automation_Api>("automation").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_Identity_Api>("identity").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_AuditObservability_Api>("audit-observability").WaitForCompletion(migrations);

// React apps per ADR-0074: two pnpm-workspace apps under apps/. Skipped in
// test mode so the integration suite doesn't start two Node dev servers.
if (isRunMode && !isE2ETests)
{
    builder.AddNpmApp("management-web", "../../apps/management-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5173)
        .WithReference(cameraCatalog)
        .WithExternalHttpEndpoints();

    builder.AddNpmApp("kiosk-web", "../../apps/kiosk-web", "dev")
        .WithHttpEndpoint(env: "PORT", port: 5174)
        .WithExternalHttpEndpoints();
}

await builder.Build().RunAsync();
