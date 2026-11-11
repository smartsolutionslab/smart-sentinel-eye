using SmartSentinelEye.ApiGateway;

// The gateway's composition lives in GatewayHost so a test can start the same host the process runs. The health
// endpoints are mapped here, in Program.cs, where ServedHealthHostCompletenessTests looks for them.
var app = GatewayHost.Build(args);

app.MapDefaultEndpoints();

await app.RunAsync();
