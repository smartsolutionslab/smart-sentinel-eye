var builder = DistributedApplication.CreateBuilder(args);

// MigrationRunner orchestrates all per-context migrations, runs once, exits.
// Every Api waits for it to complete before starting (ADR-0067).
var migrations = builder.AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations");

builder.AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_StreamDistribution_Api>("stream-distribution").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_SystemVariables_Api>("system-variables").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_EventIngestion_Api>("event-ingestion").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_OverlayDesigner_Api>("overlay-designer").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_Automation_Api>("automation").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_Identity_Api>("identity").WaitForCompletion(migrations);
builder.AddProject<Projects.SmartSentinelEye_AuditObservability_Api>("audit-observability").WaitForCompletion(migrations);

// React apps per ADR-0074: two pnpm-workspace apps under apps/.
// Per-feature wiring (WithReference to specific APIs) lands as feature work needs it.
builder.AddNpmApp("management-web", "../../apps/management-web", "dev")
    .WithHttpEndpoint(env: "PORT", port: 5173)
    .WithExternalHttpEndpoints();

builder.AddNpmApp("kiosk-web", "../../apps/kiosk-web", "dev")
    .WithHttpEndpoint(env: "PORT", port: 5174)
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
