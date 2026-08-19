using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Reports how many announcements are waiting to be delivered, and how long the
/// oldest has waited (spec 021 FR-008, FR-009).
///
/// <para>
/// This exists because the feature it belongs to is invisible when it works.
/// Before it, a failed announcement vanished and left nothing to look at; after
/// it, a failed announcement is a durable row that will be retried — and an
/// outbox quietly growing looks exactly like an empty one until the disk fills.
/// Trading a silent loss for a silent backlog would not be much of a trade.
/// </para>
///
/// <para>
/// <b>Degraded, not Unhealthy.</b> A backlog means delivery is behind, which is
/// what the outbox is for; nothing has been lost and the write path is still
/// serving. Failing the readiness probe would take a service out of rotation for
/// a condition it is currently handling correctly.
/// </para>
/// </summary>
public sealed class OutboxBacklogHealthCheck<TDbContext>(TDbContext database, string outboxSchema)
    : IHealthCheck
    where TDbContext : DbContext
{
    /// <summary>
    /// Past this, delivery is not merely behind — something is wrong with it.
    /// A minute of backlog on a path sized for thousands of events a second is
    /// already a long time to be talking to nobody.
    /// </summary>
    public static readonly TimeSpan ConcerningAge = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            (long pending, TimeSpan oldest) = await ReadBacklogAsync(cancellationToken);

            Dictionary<string, object> data = new(StringComparer.Ordinal)
            {
                ["pending"] = pending,
                ["oldestSeconds"] = Math.Round(oldest.TotalSeconds, 1),
                ["schema"] = outboxSchema,
            };

            if (pending == 0)
            {
                return HealthCheckResult.Healthy("No announcements are waiting.", data);
            }

            string description = string.Create(
                CultureInfo.InvariantCulture,
                $"{pending} announcement(s) waiting; oldest {oldest.TotalSeconds:F0}s.");

            return oldest >= ConcerningAge
                ? HealthCheckResult.Degraded(description, data: data)
                : HealthCheckResult.Healthy(description, data);
        }
        catch (DbException ex)
        {
            // The database being unreachable is already reported by the
            // connection's own check. Repeating it as an outbox failure would
            // be one cause producing two alarms.
            return HealthCheckResult.Healthy("Backlog not readable; the database check owns this.", data: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["error"] = ex.GetType().Name,
            });
        }
    }

    private async Task<(long Pending, TimeSpan Oldest)> ReadBacklogAsync(CancellationToken cancellationToken)
    {
        // Wolverine owns this table; nothing in this repository writes it, which
        // is why reading it is the only way to see a pending announcement at all.
        await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
            SELECT count(*),
                   COALESCE(EXTRACT(EPOCH FROM (now() - min("execution_time"))), 0)
            FROM {outboxSchema}.wolverine_outgoing_envelopes
            """;

        await database.Database.OpenConnectionAsync(cancellationToken);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, TimeSpan.Zero);
        }

        long pending = reader.GetInt64(0);
        double oldestSeconds = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
        return (pending, TimeSpan.FromSeconds(oldestSeconds));
    }
}
