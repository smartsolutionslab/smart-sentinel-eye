using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Architecture.Tests.Persistence;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Routing;

namespace SmartSentinelEye.Architecture.Tests.Messaging;

/// <summary>
/// Spec 442 (ADR-0185), plan C2 and test design notes. Composes one bounded context's own
/// <c>Add{Context}Infrastructure</c> (through <see cref="PostgresComposition"/>) and starts only the
/// Wolverine runtime, with every external transport stubbed and message persistence disabled, so the
/// sender Wolverine would use for a published type can be read off the real module wiring without
/// Docker. No other hosted service of the module is started, so nothing dials Postgres, RabbitMQ,
/// Keycloak or Mosquitto.
/// </summary>
internal sealed class DurableOutboxComposition : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly IHostedService runtimeService;

    private DurableOutboxComposition(ServiceProvider provider, IHostedService runtimeService)
    {
        this.provider = provider;
        this.runtimeService = runtimeService;
    }

    public static async Task<DurableOutboxComposition> StartAsync(string contextName)
    {
        var provider = PostgresComposition.Compose(
            contextName,
            builder =>
            {
                builder.Services.ConfigureWolverine(opts => opts.StubAllExternalTransports());
                builder.Services.DisableAllWolverineMessagePersistence();
            });

        var runtimeService = provider.GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "WolverineRuntime");
        await runtimeService.StartAsync(CancellationToken.None);

        return new DurableOutboxComposition(provider, runtimeService);
    }

    /// <summary>
    /// Whether the RabbitMQ sender the module routes <paramref name="messageType"/> through is durable.
    /// </summary>
    public bool IsDurable(Type messageType)
    {
        var routes = provider.GetRequiredService<IWolverineRuntime>()
            .RoutingFor(messageType).Routes.OfType<MessageRoute>()
            .Where(route => route.Sender.Destination.Scheme == "rabbitmq")
            .ToList();

        routes.Count.ShouldBe(1, $"{messageType.Name} should have exactly one RabbitMQ route");
        return routes[0].Sender.IsDurable;
    }

    public async ValueTask DisposeAsync()
    {
        await runtimeService.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();
    }
}
