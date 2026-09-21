using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed durable per-overlay version counter (issue #2426).
/// Replaces the process-lifetime counter that used to live on
/// <c>InMemoryReverseIndex</c> — a restart of this service must not
/// reset what a connected kiosk already holds as its high-water mark.
/// Mirrors <see cref="VariableValueRequestDedupStore"/> in shape: a
/// single parameterised <c>INSERT ... ON CONFLICT</c> over
/// <see cref="SystemVariablesDbContext"/>.
/// </summary>
public sealed class OverlayTextVersionStore(SystemVariablesDbContext dbContext) : IOverlayTextVersions
{
    // Spec 202 plan.md §3 (SC-3). Kiosks connected across the deploy still
    // hold in-memory high-water marks minted by the retired per-process
    // counter. A durable counter starting an empty table at 1 would hand out
    // 1 and reproduce #2426 once, silently, on the very deploy that fixes
    // it — and a browser does not reload when a server restarts. Reaching
    // this floor from the retired counter would need roughly 11,500 changes
    // per second sustained for a full day against one overlay, which is
    // unreachable by four orders of magnitude for this context's write path.
    private const long Floor = 1_000_000_000;

    public async Task<IReadOnlyDictionary<Guid, long>> AdvanceAsync(
        IReadOnlyCollection<Guid> overlayIdentifiers, CancellationToken cancellationToken)
    {
        Ensure.That(overlayIdentifiers).IsNotNull();

        Guid[] distinctIdentifiers = [.. overlayIdentifiers.Distinct()];

        const string sql =
            """
            INSERT INTO overlay_text_version (overlay_identifier, version)
            SELECT unnest({0}), {1}
            ON CONFLICT (overlay_identifier)
            DO UPDATE SET version = overlay_text_version.version + 1
            RETURNING overlay_identifier AS "OverlayIdentifier", version AS "Version";
            """;

        List<OverlayTextVersionRow> rows = await dbContext.Database
            .SqlQueryRaw<OverlayTextVersionRow>(sql, distinctIdentifiers, Floor)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, long> versionByOverlay = rows.ToDictionary(row => row.OverlayIdentifier, row => row.Version);

        Dictionary<Guid, long> advanced = [];
        foreach (Guid overlayIdentifier in overlayIdentifiers)
        {
            advanced[overlayIdentifier] = versionByOverlay[overlayIdentifier];
        }

        return advanced;
    }

    public async Task<long> CurrentAsync(Guid overlayIdentifier, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT version AS "Value" FROM overlay_text_version WHERE overlay_identifier = {0};
            """;

        long?[] versions = await dbContext.Database
            .SqlQueryRaw<long?>(sql, overlayIdentifier)
            .ToArrayAsync(cancellationToken);

        return versions.Length == 1 && versions[0] is { } version ? version : 0;
    }

    private sealed record OverlayTextVersionRow(Guid OverlayIdentifier, long Version);
}
