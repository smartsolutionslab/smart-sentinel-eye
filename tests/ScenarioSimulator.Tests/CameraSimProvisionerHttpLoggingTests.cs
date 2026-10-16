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
/// <para>
/// The counterfactual that once lived here (the recorder sees the factory's request log when its
/// level allows) is deleted: the camera-sim client no longer registers the factory's loggers at
/// all, so it would fail for the wrong reason. It only proved the test below is not vacuous; that
/// job now belongs to <see cref="CameraSimProvisionerResilienceScopeLeakTests"/> and the
/// request-URI-scope counterfactual in <see cref="CameraSimProvisionerLogRedactionAdversarialTests"/>.
/// Both tests were added in this PR, so nothing on develop is weakened.
/// </para>
/// </summary>
public sealed class CameraSimProvisionerHttpLoggingTests
{
    [Fact]
    public async Task The_camera_sim_client_request_log_does_not_print_the_path()
    {
        var log = await ProvisionAsync(overrides: []);

        log.Dump.ShouldNotContain("SECRET-token", customMessage: log.Dump);
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
