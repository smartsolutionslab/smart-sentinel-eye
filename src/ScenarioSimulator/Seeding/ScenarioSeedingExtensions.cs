using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.Seeding;

/// <summary>
/// Registers the M2 seeding dependencies (ADR-0111): the overlay/rule/layout
/// REST clients (bearer-authenticated, base addresses from
/// <see cref="SimulatorOptions"/>) and the asset correlation table.
/// <c>Program.cs</c> calls <see cref="AddScenarioSeeding"/>. The seeder hosted
/// service + the camera-registered handler are registered separately in
/// <c>Program.cs</c>.
/// </summary>
public static class ScenarioSeedingExtensions
{
    public static IHostApplicationBuilder AddScenarioSeeding(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AssetCorrelationTable>();

        builder.Services.AddHttpClient<OverlayDesignerClient>((sp, client) =>
                client.BaseAddress = new Uri(Resolve(sp).OverlayDesignerUrl));

        builder.Services.AddHttpClient<AutomationRulesClient>((sp, client) =>
                client.BaseAddress = new Uri(Resolve(sp).AutomationUrl));

        builder.Services.AddHttpClient<LayoutCompositionClient>((sp, client) =>
                client.BaseAddress = new Uri(Resolve(sp).LayoutCompositionUrl));

        // Shared by the seeder (restart path) and the CameraRegisteredV1
        // handler (live path); the correlation table's one-shot claim keeps
        // the wall created exactly once regardless of which gets there first.
        builder.Services.AddSingleton<WallSeeder>();

        return builder;
    }

    private static SimulatorOptions Resolve(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<SimulatorOptions>>().Value;
}
