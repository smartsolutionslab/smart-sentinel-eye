using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

namespace SmartSentinelEye.ScenarioSimulator.Timeline;

/// <summary>
/// Registers the M2 billet timeline (ADR-0111): the five sensor-behaviour
/// strategies, the MQTT sample mapper, the MQTT publisher (singleton), and the
/// timeline hosted service. <c>Program.cs</c> calls <see cref="AddBilletTimeline"/>.
/// </summary>
public static class BilletTimelineExtensions
{
    public static IHostApplicationBuilder AddBilletTimeline(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISensorBehaviour, RampBehaviour>();
        builder.Services.AddSingleton<ISensorBehaviour, BurstBehaviour>();
        builder.Services.AddSingleton<ISensorBehaviour, SteadyBehaviour>();
        builder.Services.AddSingleton<ISensorBehaviour, DecayBehaviour>();
        builder.Services.AddSingleton<ISensorBehaviour, StepBehaviour>();
        builder.Services.AddSingleton<MqttSampleMapper>();
        builder.Services.AddSingleton<MqttPublisher>();
        builder.Services.AddHostedService<BilletTimelineHostedService>();
        return builder;
    }
}
