using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 390 / #2976, US4. IHttpClientFactory logs every request URI at Information, and the
/// camera-sim request URI carries the operator's URL path. The simulator's real appsettings.json
/// is loaded, not a copy, so editing the filter away fails here.
/// </summary>
public sealed class CameraSimProvisionerHttpLoggingTests
{
    private const string FactoryCategory = "System.Net.Http.HttpClient.CameraSimProvisioner";

    [Fact]
    public async Task The_camera_sim_client_request_log_does_not_print_the_path()
    {
        var log = await ProvisionAsync(overrides: []);

        log.Dump.ShouldNotContain("SECRET-token", customMessage: log.Dump);
    }

    [Fact]
    public async Task The_recorder_sees_the_camera_sim_client_request_log_when_its_level_allows()
    {
        var log = await ProvisionAsync(overrides: new Dictionary<string, string?>
        {
            [$"Logging:LogLevel:{FactoryCategory}"] = "Information",
        });

        log.Entries
            .Where(entry => entry.Category.StartsWith(FactoryCategory, StringComparison.Ordinal))
            .ShouldContain(entry => entry.Message.Contains("SECRET-token", StringComparison.Ordinal), log.Dump);
    }

    private static async Task<RecordingLoggerProvider> ProvisionAsync(Dictionary<string, string?> overrides)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(overrides)
            .Build();

        RecordingLoggerProvider log = new();
        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddProvider(log));
        services
            .AddHttpClient<CameraSimProvisioner>(client => client.BaseAddress = new Uri("http://camera-sim:9997"))
            .ConfigurePrimaryHttpMessageHandler(() => new RigHandler(null, _ => new HttpResponseMessage(HttpStatusCode.OK)));

        await using var provider = services.BuildServiceProvider();

        await provider
            .GetRequiredService<CameraSimProvisioner>()
            .ProvisionLabelledPathAsync("SECRET-token/stream", "Line 7 Spare", Guid.CreateVersion7(), CancellationToken.None);

        return log;
    }
}
