using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Registers the camera-sim MediaMTX v3 control-plane client. <c>Program.cs</c>
/// calls <see cref="AddCameraSimProvisionerClient"/>; it is its own method so a
/// test can build the same registration the worker runs.
/// </summary>
internal static class CameraSimProvisionerExtensions
{
    internal static IHttpClientBuilder AddCameraSimProvisionerClient(this IServiceCollection services)
    {
        // The span's url.full carries the camera-sim path an operator typed. The callback is
        // process-wide, so it redacts by the MediaMTX config-paths prefix and leaves other URLs alone.
        services.Configure<HttpClientTraceInstrumentationOptions>(options =>
        {
            var previous = options.EnrichWithHttpRequestMessage;
            options.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                previous?.Invoke(activity, request);
                CameraSimSpanRedaction.Apply(activity);
            };
        });

        return services.AddHttpClient<CameraSimProvisioner>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SimulatorOptions>>().Value;
                client.BaseAddress = new Uri(options.CameraSimApiUrl);
            })
            // The factory's logging handlers scope every request as `HTTP {Method} {Uri}`, and
            // Polly's records inherit that scope; the Uri is a camera-sim path an operator typed.
            .RemoveAllLoggers();
    }
}
