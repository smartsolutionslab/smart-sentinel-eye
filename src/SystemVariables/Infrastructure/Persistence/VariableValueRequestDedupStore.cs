using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.EventHandlers;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed dedup store for
/// <see cref="SystemVariableValueRequestedV1Handler"/>. Uses an
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> on the
/// <c>variable_value_request_dedup</c> table; the unique row is
/// keyed on <c>(fab, variable_name, causing_event_identifier)</c>.
/// The <c>seen_at</c> column is for the future 7-day-TTL cleanup
/// worker.
/// </summary>
public sealed class VariableValueRequestDedupStore(
    SystemVariablesDbContext dbContext) : IVariableValueRequestDedupStore
{
    public async Task<bool> TryReserveAsync(
        FabIdentifier fab, string variableName, Guid causingEventIdentifier, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(variableName).IsNotNull().IsNotNullOrWhiteSpace();
        const string sql =
            """
            INSERT INTO variable_value_request_dedup (fab, variable_name, causing_event_identifier, seen_at)
            VALUES ({0}, {1}, {2}, NOW())
            ON CONFLICT (fab, variable_name, causing_event_identifier) DO NOTHING;
            """;
        int rowsAffected = await dbContext.Database
            .ExecuteSqlRawAsync(sql, [fab.Value, variableName, causingEventIdentifier], cancellationToken);
        return rowsAffected == 1;
    }
}
