using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Infrastructure.Archive;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

/// <summary>
/// Composition root for the AuditObservability Infrastructure
/// layer (ADR-0051). Wires persistence, the query handlers, the
/// audit-write subscriber, and the Wolverine outbox + bus
/// subscriptions.
/// </summary>
public static class AuditObservabilityInfrastructureModule
{
    public const string ContextName = "audit-observability";
    public const string OutboxSchema = "wolverine_audit";

    /// <summary>
    /// Parallel listeners per audit queue (ADR-0124, spec 009 NFR-001).
    ///
    /// <para>
    /// One listener drained a queue at ~100 rows/s, which is exactly the rate
    /// NFR-001 demands and therefore no rate at all: measured against a run-mode
    /// stack, latency held to ~68 ev/s and collapsed to a p50 of seconds by ~86.
    /// The ceiling was one handler's turnaround, not the process's capacity — the
    /// publishers' outboxes stayed empty throughout, so the backlog was only ever
    /// in front of this consumer.
    /// </para>
    ///
    /// <para>
    /// Audit rows are order-independent and the write is idempotent on
    /// <c>event_identifier</c>, so parallel delivery changes no outcome — see
    /// ADR-0124 for why that is a property of this context and not a licence to
    /// widen the default.
    /// </para>
    ///
    /// <para>
    /// <b>Four is an upper bound found by hitting it, not a guess.</b> Eight was
    /// measured and is worse than useless on the shared stack: the dev/CI
    /// Postgres runs at <c>max_connections = 100</c> for all nine contexts, and
    /// eight listeners took <c>audit-db</c> alone to 22 connections and the
    /// cluster past its limit. What failed was not audit — it was
    /// <b>system-variables</b>, refusing writes with <c>53300: sorry, too many
    /// clients already</c>. Raising this without re-checking that budget breaks a
    /// bystander, which is the kind of failure nobody attributes to a listener
    /// count.
    /// </para>
    /// </summary>
    public const int AuditListenerCount = 4;

    /// <summary>
    /// Rows committed in one transaction when a batch fills (ADR-0127).
    /// Wolverine's default is 100 and there is no evidence for a different one.
    /// </summary>
    public const int AuditBatchSize = 100;

    /// <summary>
    /// How long a partly-filled batch waits for stragglers before it commits
    /// anyway — and therefore latency this adds outright to any event that
    /// arrives into an empty batch.
    ///
    /// <para>
    /// Wolverine's default is <b>250 ms</b>, which is five times NFR-001's whole
    /// p99 budget and would be self-defeating here. Ten milliseconds is chosen
    /// against the arithmetic rather than by feel: batching <c>N</c> messages at
    /// rate <c>R</c> needs a window of <c>N/R</c>, so at the 100 ev/s NFR-001
    /// names, a 10 ms window collects about one message and a window long enough
    /// to collect ten would cost 100 ms.
    /// </para>
    ///
    /// <para>
    /// Which is to say this is expected to help under <b>backlog</b>, where
    /// batches fill by size rather than by time, and to cost a little at the
    /// steady state. Whether that trade is worth taking is the measurement
    /// ADR-0127 records, not something this constant asserts.
    /// </para>
    /// </summary>
    public static readonly TimeSpan AuditBatchTriggerTime = TimeSpan.FromMilliseconds(10);

    public static IHostApplicationBuilder AddAuditObservabilityInfrastructure(this IHostApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.AddAuditObservabilityPersistence();

        builder.Services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        builder.Services.AddScoped<IAuditEventQuerySource, AuditEventQuerySource>();

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton(V1ResourceMap.Default);
        builder.Services.AddScoped<AuditingMessageHandler>();

        builder.Services.AddScoped<SearchAuditQueryHandler>();
        builder.Services.AddScoped<GetResourceTimelineQueryHandler>();
        builder.Services.AddScoped<GetAuditEventQueryHandler>();

        // Retention: TimescaleDB inventory + MinIO archiver + hosted worker.
        builder.AddMinioClient("minio");
        builder.Services.AddOptions<MinioOptions>()
            .Bind(builder.Configuration.GetSection(MinioOptions.SectionName));
        builder.Services.AddScoped<IAuditChunkInventory, TimescaleAuditChunkInventory>();
        builder.Services.AddScoped<IAuditChunkArchiver, MinioAuditChunkArchiver>();
        builder.Services.AddOptions<AuditRetentionOptions>()
            .Bind(builder.Configuration.GetSection(AuditRetentionOptions.SectionName));
        builder.Services.AddHostedService<AuditRetentionHostedService>();

        builder.AddWolverineForContext<AuditObservabilityDbContext>(
            moduleQueuePrefix: ContextName,
            outboxSchema: OutboxSchema,
            postgresConnectionName: AuditObservabilityPersistenceModule.DatabaseConnectionName,
            listenerCount: AuditListenerCount,
            useNativeAcks: true,
            configureMore: opts => opts.BatchMessagesOf<SystemVariableValueChangedV1>(batching =>
            {
                batching.BatchSize = AuditBatchSize;
                batching.TriggerTime = AuditBatchTriggerTime;
                batching.ExecuteOnDedicatedLocalQueue();
            }));

        return builder;
    }
}
