using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed dedup store for
/// <see cref="WallSceneSwitchRequestedV1Handler"/> (spec 296 FR-012). Uses
/// an <c>INSERT ... ON CONFLICT DO NOTHING</c> on the
/// <c>wall_switch_request_receipts</c> table; the unique row is keyed on
/// <c>(rule_id, causing_event_id)</c>. Mirrors
/// <c>VariableValueRequestDedupStore</c> line for line.
/// </summary>
public sealed class WallSwitchRequestDedupStore(LayoutCompositionDbContext dbContext) : IWallSwitchRequestDedupStore
{
    public async Task<bool> TryReserveAsync(
        RuleIdentifier rule, CausingEventIdentifier causingEvent, WallIdentifier wall,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO wall_switch_request_receipts (rule_id, causing_event_id, wall_id, received_at)
            VALUES ({0}, {1}, {2}, NOW())
            ON CONFLICT (rule_id, causing_event_id) DO NOTHING;
            """;
        int rowsAffected = await dbContext.Database
            .ExecuteSqlRawAsync(sql, [rule.Value, causingEvent.Value, wall.Value], cancellationToken);
        return rowsAffected == 1;
    }
}
