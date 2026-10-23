using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// ADR-0185 decision 1, spec 442 T002 and T006: what the declared type's durable sender does to the
/// database, observed against the stack's real Postgres and RabbitMQ.
///
/// <para>
/// A probe host is built in the test process through <c>AddWolverineForContext</c> itself, against a
/// throwaway schema, with test-only message types (never in <c>Shared.Contracts</c>), so the facts
/// are about the mechanism and not about any one context's publish site. The row is observed with
/// <see cref="OutboxInsertProbe"/>, not by polling: a delivered row is deleted within milliseconds.
/// </para>
///
/// <para>
/// <b>Phase 4a (ADR-0144), red first.</b> The declared-type facts fail until phase 4b wires
/// <c>durableOutboxMessageTypes</c>. The undeclared-type and rollback facts are characterisation of
/// today and stay green. <b>Not covered here:</b> that a send failure keeps the durable row and
/// delivers it later. Doing it without a broker restart (excluded from CI) needs failure injection
/// this fixture does not have; it is observed locally by <c>OutboxSurvivesAKillTests</c>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DurableOutboxMechanismTests(AspireFixture aspire)
{
    public sealed record DurableProbeV1(Guid Identifier);

    public sealed record InlineProbeV1(Guid Identifier);

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    [Fact]
    public async Task A_declared_types_outgoing_row_is_committed_before_CommitAsync_returns()
    {
        await using var probe = await ProbeHost.StartAsync(aspire, retryOnFailure: false);
        await using var insertProbe = await probe.InstallInsertProbeAsync();

        await using (var scope = probe.Host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var commit = scope.ServiceProvider.GetRequiredService<ITransactionalCommit>();

            await bus.PublishAsync(new DurableProbeV1(Guid.NewGuid()));
            await commit.CommitAsync(CancellationToken.None);
        }

        (await insertProbe.CommittedRowsForAsync(nameof(DurableProbeV1))).ShouldBe(
            1,
            "a publish with no accompanying EF write must still commit its envelope row before the caller continues");
    }

    [Fact]
    public async Task An_undeclared_types_publish_commits_no_outgoing_row()
    {
        await using var probe = await ProbeHost.StartAsync(aspire, retryOnFailure: false);
        await using var insertProbe = await probe.InstallInsertProbeAsync();

        await using (var scope = probe.Host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var commit = scope.ServiceProvider.GetRequiredService<ITransactionalCommit>();

            await bus.PublishAsync(new InlineProbeV1(Guid.NewGuid()));
            await commit.CommitAsync(CancellationToken.None);
        }

        (await insertProbe.CommittedRowsForAsync(nameof(InlineProbeV1))).ShouldBe(0);
    }

    [Fact]
    public async Task A_declared_types_publish_that_is_never_committed_leaves_no_row()
    {
        await using var probe = await ProbeHost.StartAsync(aspire, retryOnFailure: false);
        await using var insertProbe = await probe.InstallInsertProbeAsync();

        await using (var scope = probe.Host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

            await bus.PublishAsync(new DurableProbeV1(Guid.NewGuid()));
        }

        (await insertProbe.CommittedRowsForAsync(nameof(DurableProbeV1))).ShouldBe(
            0,
            "the open transaction is rolled back when the scope disposes, which is why every durable publish needs a commit after it");
    }

    [Fact]
    public async Task A_declared_type_cannot_be_published_on_a_context_with_a_retrying_execution_strategy()
    {
        await using var probe = await ProbeHost.StartAsync(aspire, retryOnFailure: true);

        await using var scope = probe.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => bus.PublishAsync(new DurableProbeV1(Guid.NewGuid())),
            "a retrying strategy rejects the user-initiated transaction a durable publish opens; "
            + "PostgresTelemetry sets DisableRetry for this reason (ADR-0185 decision 1)");
    }

    private sealed class ProbeHost : IAsyncDisposable
    {
        private readonly AspireFixture aspire;
        private readonly string connectionString;

        private ProbeHost(AspireFixture aspire, IHost host, string schema, string connectionString)
        {
            this.aspire = aspire;
            Host = host;
            Schema = schema;
            this.connectionString = connectionString;
        }

        public IHost Host { get; }

        public string Schema { get; }

        public static async Task<ProbeHost> StartAsync(AspireFixture aspire, bool retryOnFailure)
        {
            var postgres = await aspire.App.GetConnectionStringAsync(AspireFixture.CameraCatalogConnectionName)
                ?? throw new InvalidOperationException("The camera-catalog database was not provisioned.");
            var rabbit = await aspire.App.GetConnectionStringAsync("rabbitmq")
                ?? throw new InvalidOperationException("RabbitMQ was not provisioned.");

            var suffix = Guid.NewGuid().ToString("N");
            var schema = $"wolverine_probe_{suffix}";

            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:probe-db"] = postgres,
                    ["ConnectionStrings:rabbitmq"] = rabbit,
                });

            builder.Services.AddDbContext<ProbeDbContext>(
                options => options.UseNpgsql(
                    postgres,
                    npgsql =>
                    {
                        if (retryOnFailure)
                        {
                            npgsql.EnableRetryOnFailure();
                        }
                    }));

            builder.AddWolverineForContext<ProbeDbContext>(
                moduleQueuePrefix: $"probe-{suffix}",
                outboxSchema: schema,
                postgresConnectionName: "probe-db",
                durableOutboxMessageTypes: [typeof(DurableProbeV1)]);

            var host = builder.Build();
            await host.StartAsync();

            return new ProbeHost(aspire, host, schema, postgres);
        }

        public async Task<OutboxInsertProbe> InstallInsertProbeAsync()
        {
            DbContextOptionsBuilder<ProbeDbContext> options = new();
            options.UseNpgsql(connectionString);
            var database = new ProbeDbContext(options.Options);

            return await OutboxInsertProbe.InstallAsync(database, Schema);
        }

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            Host.Dispose();

            await using var database = await aspire.CreateCameraCatalogDbContextAsync();
#pragma warning disable EF1002
            await database.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS {Schema} CASCADE");
#pragma warning restore EF1002
        }
    }
}
