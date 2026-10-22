using System.Globalization;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests;

/// <summary>
/// ADR-0182 conditions (4) "every attempt reloads" and the rolled-back attempt's announcement (#2783), proven on the
/// production-like host: the two transaction settings of <c>WolverineDefaults</c> are on, the retire arrives through
/// the local queue the way a listener delivers it, and the repository dispatches before it fails, as
/// <c>StreamRepository.SaveAsync</c> does.
///
/// <para>
/// Not pinned: the handler has no DbContext, so <c>AutoApplyTransactions</c> has nothing to wrap and no database is
/// involved; what is proven is that the settings do not change the reload and the announcement properties.
/// </para>
/// </summary>
public class RetireStreamProductionLikeTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-08-24T11:00:00Z", CultureInfo.InvariantCulture);

    private static IHost BuildHost(Script script)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddLogging()
            .AddSingleton(script)
            .AddSingleton<IRtspGateway, RetireStreamFailurePolicyTests.FakeGateway>()
            .AddSingleton<IClock, RetireStreamFailurePolicyTests.FixedClock>()
            .AddSingleton<IJourneyOrigin, NoJourneys>()
            .AddScoped<IEventBus, AmbientEventBus>()
            .AddScoped<IDomainEventDispatcher, DomainEventDispatcher>()
            .AddScoped<IDomainEventHandler<StreamHealthChangedDomainEvent>, StreamHealthChangedDomainEventHandler>()
            .AddScoped<IStreamRepository, DispatchingRepository>()
            .AddScoped<
                ICommandHandler<RetireStreamCommand, Result<Option<StreamIdentifier>, RetireStreamError>>,
                RetireStreamCommandHandler>()
            .AddScoped<CameraRetiredIntegrationEventHandler>();
        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(CameraRegisteredIntegrationEventHandler).Assembly);
            opts.Discovery.IncludeType<HealthChangeCollector>();
            opts.Policies.Add<RetireStreamFailurePolicy>();
            opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.AutoApplyTransactions();
            opts.StubAllExternalTransports();
        });

        return builder.Build();
    }

    [Fact]
    public async Task A_retire_delivered_through_the_local_queue_reloads_on_each_attempt_and_announces_once()
    {
        Script script = new();
        using var host = BuildHost(script);
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessageBus>().SendAsync(Retired(script.Camera));
        await script.Committed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await script.Announced.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(1)); // a stale second announcement would arrive after the first

        script.Repositories.Count.ShouldBe(2);
        script.Repositories.ShouldAllBe(repository => repository.Loads == 1);
        script.Announcements.Count.ShouldBe(1, "only the committed attempt's announcement is delivered");
        script.Announcements[0].FromState.ShouldBe(StreamState.Degraded.Value, "the reloaded state, not attempt 1's");
    }

    [Fact]
    public async Task A_retire_invoked_inline_reloads_on_each_attempt_and_announces_once()
    {
        Script script = new();
        using var host = BuildHost(script);
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(Retired(script.Camera));
        await script.Announced.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(1)); // a stale second announcement would arrive after the first

        script.Repositories.Count.ShouldBe(2);
        script.Repositories.ShouldAllBe(repository => repository.Loads == 1);
        script.Announcements.Count.ShouldBe(1);
        script.Announcements[0].FromState.ShouldBe(StreamState.Degraded.Value);
    }

    private static CameraRetiredV1 Retired(CameraIdentifier camera) =>
        new(
            camera.Value,
            "munich",
            "Gate 1",
            Moment,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.CreateVersion7(), Moment, "munich", null));

    public sealed class Script
    {
        public CameraIdentifier Camera { get; } = CameraIdentifier.From(Guid.CreateVersion7());

        public List<DispatchingRepository> Repositories { get; } = [];

        public List<StreamHealthChangedV1> Announcements { get; } = [];

        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Announced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class NoJourneys : IJourneyOrigin
    {
        public IJourney Begin(string name) => new Inert();

        private sealed class Inert : IJourney
        {
            public void Failed(Exception exception)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// The ambient branch of <c>OutboxEventBus</c>: a publish inside a handler goes to the message context.
    /// </summary>
    public sealed class AmbientEventBus(IMessageContext ambient) : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull =>
            ambient.PublishAsync(integrationEvent).AsTask();
    }

    /// <summary>
    /// What reaches a subscriber, i.e. what the message context actually released.
    /// </summary>
    public sealed class HealthChangeCollector(Script script)
    {
        public void Handle(StreamHealthChangedV1 message)
        {
            script.Announcements.Add(message);
            script.Announced.TrySetResult();
        }
    }

    /// <summary>
    /// Dispatches before it commits, like <c>StreamRepository.SaveAsync</c>; the first scope's commit loses the race,
    /// and the second scope loads a stream the health sweep has since degraded.
    /// </summary>
    public sealed class DispatchingRepository : IStreamRepository
    {
        private readonly Script script;
        private readonly IDomainEventDispatcher dispatcher;
        private readonly bool losesTheRace;
        private StreamAggregate? loaded;

        public DispatchingRepository(Script script, IDomainEventDispatcher dispatcher)
        {
            this.script = script;
            this.dispatcher = dispatcher;
            losesTheRace = script.Repositories.Count == 0;
            script.Repositories.Add(this);
        }

        public int Loads { get; private set; }

        public Task<Option<StreamAggregate>> GetByCameraAsync(
            CameraIdentifier camera,
            CancellationToken cancellationToken)
        {
            Loads++;
            var stream = new StreamBuilder().ForCamera(camera).At(Moment).Build();
            stream.ClearPendingEvents();
            if (!losesTheRace)
            {
                stream.ReportDegraded(StreamError.From("swept"), new RetireStreamFailurePolicyTests.FixedClock());
                stream.ClearPendingEvents();
            }

            loaded = stream;
            return Task.FromResult(Option<StreamAggregate>.Some(stream));
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            var events = loaded!.PendingEvents.ToArray();
            loaded.ClearPendingEvents();
            await dispatcher.DispatchAsync(events, cancellationToken);

            if (losesTheRace)
            {
                throw new DbUpdateConcurrencyException("the health sweep saved first");
            }

            script.Committed.TrySetResult();
        }

        public Task<Option<StreamAggregate>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Option<StreamAggregate>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(StreamAggregate stream) => throw new NotSupportedException();

        public Task<bool> IsUnchangedSinceLoadAsync(StreamAggregate stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
