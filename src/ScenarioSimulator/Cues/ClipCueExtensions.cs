using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>
/// Registers spec 289's clip-cue dependencies (plan.md §4): the
/// unauthenticated <see cref="CameraSimPathClient"/> and the
/// <see cref="ClipCueHostedService"/> itself. <c>Program.cs</c> calls
/// <see cref="AddClipCues"/> after <c>AddScenarioSeeding</c>.
/// </summary>
public static class ClipCueExtensions
{
    public static IHostApplicationBuilder AddClipCues(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<CameraSimPathClient>((sp, client) =>
            client.BaseAddress = new Uri(Resolve(sp).CameraSimApiUrl));

        builder.Services.AddHostedService<ClipCueHostedService>();

        return builder;
    }

    private static SimulatorOptions Resolve(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<SimulatorOptions>>().Value;
}
