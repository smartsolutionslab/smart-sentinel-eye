using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed dedup store for
/// <see cref="SystemVariableValueRequestedV1Handler"/>. Uses an
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> on the
/// <c>variable_value_request_dedup</c> table; the unique row is
/// keyed on <c>(variable_name, causing_event_identifier)</c>.
/// The <c>seen_at</c> column is for the future 7-day-TTL cleanup
/// worker.
/// </summary>
public sealed class VariableValueRequestDedupStore(
    SystemVariablesDbContext dbContext) : IVariableValueRequestDedupStore
{
    public async Task<bool> TryReserveAsync(
        string variableName, Guid causingEventIdentifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        const string sql =
            """
            INSERT INTO variable_value_request_dedup (variable_name, causing_event_identifier, seen_at)
            VALUES ({0}, {1}, NOW())
            ON CONFLICT (variable_name, causing_event_identifier) DO NOTHING;
            """;
        int rowsAffected = await dbContext.Database
            .ExecuteSqlRawAsync(sql, [variableName, causingEventIdentifier], cancellationToken)
            .ConfigureAwait(false);
        return rowsAffected == 1;
    }
}
