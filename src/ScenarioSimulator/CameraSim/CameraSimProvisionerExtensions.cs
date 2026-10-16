using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Registers the camera-sim MediaMTX v3 control-plane client. <c>Program.cs</c>
/// calls <see cref="AddCameraSimProvisionerClient"/>; it is its own method so a
/// test can build the same registration the worker runs.
/// </summary>
internal static class CameraSimProvisionerExtensions
{
    internal static IHttpClientBuilder AddCameraSimProvisionerClient(this IServiceCollection services) =>
        services.AddHttpClient<CameraSimProvisioner>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SimulatorOptions>>().Value;
                client.BaseAddress = new Uri(options.CameraSimApiUrl);
            })
            // The factory's logging handlers scope every request as `HTTP {Method} {Uri}`, and
            // Polly's records inherit that scope; the Uri is a camera-sim path an operator typed.
            .RemoveAllLoggers();
}
