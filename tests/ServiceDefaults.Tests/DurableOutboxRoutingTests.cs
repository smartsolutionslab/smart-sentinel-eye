using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;
using Wolverine.Runtime.Routing;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// ADR-0185 decision 1, spec 442 T001. <c>AddWolverineForContext</c> takes a list of message types
/// whose RabbitMQ sender is durable; every other type keeps the Inline sender, and the in-process
/// local queue is untouched.
///
/// <para>
/// <b>How this runs without Docker</b> (plan R1): <see cref="WolverineOptions.StubAllExternalTransports"/>
/// plus <c>DisableAllWolverineMessagePersistence</c>, then the sender Wolverine routes each probe type
/// to is read off <see cref="IWolverineRuntime.RoutingFor"/>. The host is started so conventional
/// routing runs, but no broker or database is dialled.
/// </para>
///
/// <para>
/// <b>Phase 4a (ADR-0144), red first.</b> The facts below that say a type is durable fail until
/// phase 4b wires <c>durableOutboxMessageTypes</c>; the Inline and local-queue facts are
/// characterisation of today and stay green.
/// </para>
/// </summary>
public sealed class DurableOutboxRoutingTests
{
    public sealed record DurableProbeV1(Guid Identifier);

    public sealed record InlineProbeV1(Guid Identifier);

    public sealed record LocallyHandledProbeV1(Guid Identifier);

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    public static class LocallyHandledProbeHandler
    {
        public static Task Handle(LocallyHandledProbeV1 message) => Task.CompletedTask;
    }

    private static async Task<IHost> StartAsync(
        IReadOnlyCollection<Type>? durableOutboxMessageTypes,
        Action<WolverineOptions>? configureMore = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:probe-db"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@127.0.0.1:5672",
            });

        builder.AddWolverineForContext<ProbeDbContext>(
            moduleQueuePrefix: "probe",
            outboxSchema: "wolverine_probe",
            postgresConnectionName: "probe-db",
            durableOutboxMessageTypes: durableOutboxMessageTypes,
            configureMore: opts =>
            {
                opts.Discovery.DisableConventionalDiscovery();
                opts.Discovery.IncludeType(typeof(LocallyHandledProbeHandler));
                opts.StubAllExternalTransports();
                configureMore?.Invoke(opts);
            });

        builder.Services.DisableAllWolverineMessagePersistence();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static List<MessageRoute> RoutesFor(IHost host, Type messageType) =>
        host.Services.GetRequiredService<IWolverineRuntime>().RoutingFor(messageType).Routes.OfType<MessageRoute>().ToList();

    private static IReadOnlyList<MessageRoute> BrokerRoutesFor(IHost host, Type messageType) =>
        [.. RoutesFor(host, messageType).Where(route => route.Sender.Destination.Scheme == "rabbitmq")];

    [Fact]
    public async Task A_declared_type_is_sent_through_a_durable_sender()
    {
        using var host = await StartAsync([typeof(DurableProbeV1)]);

        var route = BrokerRoutesFor(host, typeof(DurableProbeV1)).ShouldHaveSingleItem();

        route.Sender.IsDurable.ShouldBeTrue("a declared type must be written to the outgoing table in the caller's transaction");
    }

    [Fact]
    public async Task A_declared_types_exchange_endpoint_is_in_durable_mode()
    {
        using var host = await StartAsync([typeof(DurableProbeV1)]);

        var route = BrokerRoutesFor(host, typeof(DurableProbeV1)).ShouldHaveSingleItem();

        host.Services.GetRequiredService<IWolverineRuntime>()
            .Endpoints.EndpointFor(route.Sender.Destination)!.Mode
            .ShouldBe(EndpointMode.Durable);
    }

    [Fact]
    public async Task An_undeclared_type_is_sent_through_an_inline_sender()
    {
        using var host = await StartAsync([typeof(DurableProbeV1)]);

        var route = BrokerRoutesFor(host, typeof(InlineProbeV1)).ShouldHaveSingleItem();

        route.Sender.IsDurable.ShouldBeFalse();
        host.Services.GetRequiredService<IWolverineRuntime>()
            .Endpoints.EndpointFor(route.Sender.Destination)!.Mode
            .ShouldBe(EndpointMode.Inline);
    }

    [Fact]
    public async Task Declaring_one_type_does_not_make_another_type_durable()
    {
        using var host = await StartAsync([typeof(DurableProbeV1)]);

        BrokerRoutesFor(host, typeof(DurableProbeV1)).ShouldHaveSingleItem().Sender.IsDurable.ShouldBeTrue();
        BrokerRoutesFor(host, typeof(InlineProbeV1)).ShouldHaveSingleItem().Sender.IsDurable.ShouldBeFalse();
    }

    [Fact]
    public async Task No_declaration_keeps_every_type_inline()
    {
        using var host = await StartAsync(durableOutboxMessageTypes: null);

        BrokerRoutesFor(host, typeof(DurableProbeV1)).ShouldHaveSingleItem().Sender.IsDurable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_declared_type_with_a_same_context_handler_is_durable_on_the_broker_only()
    {
        using var host = await StartAsync([typeof(LocallyHandledProbeV1)]);

        var routes = RoutesFor(host, typeof(LocallyHandledProbeV1));

        routes.Where(route => route.Sender.Destination.Scheme == "rabbitmq").ShouldHaveSingleItem()
            .Sender.IsDurable.ShouldBeTrue("the broker copy of a declared type is durable");
        routes.Where(route => route.Sender.Destination.Scheme == "local").ShouldHaveSingleItem()
            .Sender.IsDurable.ShouldBeFalse("the in-process copy stays in memory");
    }

    [Fact]
    public async Task A_same_context_handlers_local_queue_stays_buffered_in_memory()
    {
        using var host = await StartAsync([typeof(LocallyHandledProbeV1)]);

        var local = RoutesFor(host, typeof(LocallyHandledProbeV1))
            .Where(route => route.Sender.Destination.Scheme == "local")
            .ShouldHaveSingleItem();

        host.Services.GetRequiredService<IWolverineRuntime>()
            .Endpoints.EndpointFor(local.Sender.Destination)!.Mode
            .ShouldBe(EndpointMode.BufferedInMemory);
    }

    [Fact]
    public async Task A_second_ConfigureSending_on_one_convention_replaces_the_first_rather_than_composing()
    {
        // GREEN characterisation of Wolverine 6.51.0 itself, not of this repository's code: it is the
        // evidence behind the "ONE ConfigureSending call" comment. A second UseConventionalRouting
        // does not reach the same convention (it adds another, doubling every route), so the trap is
        // two ConfigureSending calls on one convention, reproduced here directly.
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(opts =>
        {
            opts.Discovery.DisableConventionalDiscovery();
            opts.UseRabbitMq(new Uri("amqp://guest:guest@127.0.0.1:5672")).UseConventionalRouting(routing =>
            {
                routing.ConfigureSending((exchange, context) =>
                {
                    if (context.MessageType == typeof(DurableProbeV1))
                    {
                        exchange.UseDurableOutbox();
                    }
                });
                routing.ConfigureSending((exchange, context) =>
                {
                    if (context.MessageType == typeof(InlineProbeV1))
                    {
                        exchange.UseDurableOutbox();
                    }
                });
            });
            opts.StubAllExternalTransports();
        });
        builder.Services.DisableAllWolverineMessagePersistence();

        using var host = builder.Build();
        await host.StartAsync();

        BrokerRoutesFor(host, typeof(InlineProbeV1)).ShouldHaveSingleItem().Sender.IsDurable.ShouldBeTrue(
            "the second callback is the one that ran");
        BrokerRoutesFor(host, typeof(DurableProbeV1)).ShouldHaveSingleItem().Sender.IsDurable.ShouldBeFalse(
            "the first callback was replaced, not composed with");
    }
}
