// MigrationRunner orchestrates all bounded-context database migrations and exits (ADR-0067).
// This is a placeholder host; real migration orchestration lands when the first context's
// persistence is wired (Camera Catalog walking skeleton).

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHostedService<SmartSentinelEye.MigrationRunner.MigrationRunnerHostedService>();

await builder.Build().RunAsync();
