using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults;
using Wolverine;
using Wolverine.Runtime;

namespace SmartSentinelEye.Integration.Tests.Messaging;

/// <summary>
/// ADR-0189 Decision 1, Postgres half (issue #3149). A host composed with
/// <c>AddWolverineForContext</c> against the fixture's Postgres stamps every dead letter it stores
/// with an <c>expires</c> time 14 days out, which the durability agent then purges.
///
/// <para>
/// Runs in its own schema so it creates and drops only what it owns.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class PostgresDeadLetterExpiryTests(AspireFixture aspire)
{
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    [Fact]
    public async Task A_dead_letter_stored_by_a_composed_host_expires_in_fourteen_days()
    {
        var cancellationToken = CancellationToken.None;
        var postgres = await aspire.App.GetConnectionStringAsync(
            AspireFixture.EventIngestionConnectionName, cancellationToken);
        var rabbit = await aspire.App.GetConnectionStringAsync("rabbitmq", cancellationToken);
        var schema = $"wolverine_dlq_{Guid.NewGuid():N}";

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:probe-db"] = postgres,
                ["ConnectionStrings:rabbitmq"] = rabbit,
            });
        builder.AddWolverineForContext<ProbeDbContext>(
            moduleQueuePrefix: "deadletterexpiryprobe",
            outboxSchema: schema,
            postgresConnectionName: "probe-db",
            configureMore: opts => opts.Discovery.DisableConventionalDiscovery());

        using var host = builder.Build();
        await using var dataSource = NpgsqlDataSource.Create(postgres ?? throw new InvalidOperationException("no postgres connection string"));
        try
        {
            await host.StartAsync(cancellationToken);

            Envelope envelope = new()
            {
                Id = Guid.NewGuid(),
                MessageType = "dead-letter-expiry-probe",
                Data = [1],
                ContentType = "application/json",
                Destination = new Uri("local://dead-letter-expiry-probe"),
            };
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            await runtime.Storage.Inbox.MoveToDeadLetterStorageAsync(
                envelope, new InvalidOperationException("dead-letter-expiry-probe"));

            await using var command = dataSource.CreateCommand(
                $"select expires from {schema}.wolverine_dead_letters where id = $1");
            command.Parameters.AddWithValue(envelope.Id);
            var expires = await command.ExecuteScalarAsync(cancellationToken);

            expires.ShouldNotBeNull("the stored dead letter carries no expiry, so it would be kept forever");
            expires.ShouldNotBeOfType<DBNull>("the stored dead letter carries no expiry, so it would be kept forever");
            var expiresAt = (DateTimeOffset)(expires is DateTime moment
                ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
                : expires);
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            remaining.ShouldBeInRange(TimeSpan.FromDays(14) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
            await using var drop = dataSource.CreateCommand($"drop schema if exists {schema} cascade");
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
