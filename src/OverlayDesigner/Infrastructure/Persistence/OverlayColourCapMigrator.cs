using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;

/// <summary>
/// Darkens every stored element colour brighter than <see cref="OverlayColor.MaximumRelativeLuminance"/>
/// to its nearest allowed value (spec 463, #3108, ADR-0190), recording each change in
/// <c>overlay_colour_migrations</c>. A Text element whose darkened colour is no longer legible has its
/// alpha raised to the lowest legible one, so the existing <c>kind &lt;&gt; 'Text'</c> alpha CHECK still
/// holds. Runs in one transaction after <see cref="OverlayDesignerMigrator"/> (registration order is run
/// order, ADR-0067) and is idempotent: a compliant row is skipped, so a second run changes nothing.
///
/// <para>
/// Rows are read with raw SQL: materialising them through the EF conversion would throw
/// <see cref="OverlayColor.From"/> on exactly the rows this exists to fix.
/// </para>
/// </summary>
public sealed class OverlayColourCapMigrator(
    IDbContextFactory<OverlayDesignerDbContext> dbContextFactory,
    ILogger<OverlayColourCapMigrator> logger) : IMigrator
{
    public string ContextName => "OverlayDesigner.ColourCap";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var rows = await context.Database.SqlQueryRaw<StoredElementColour>(
            "SELECT revision_id AS \"RevisionId\", ordinal AS \"Ordinal\", kind AS \"Kind\", color AS \"Color\" "
            + "FROM overlay_revision_elements FOR UPDATE",
            []).ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var (revision, ordinal, kind, previous) in rows)
        {
            var migrated = Migrated(kind, previous);
            if (migrated == previous)
            {
                continue;
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE overlay_revision_elements SET color = {migrated} WHERE revision_id = {revision} AND ordinal = {ordinal}",
                cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO overlay_colour_migrations (revision_id, ordinal, previous_color, migrated_color, migrated_at)
                VALUES ({revision}, {ordinal}, {previous}, {migrated}, {DateTimeOffset.UtcNow})
                """,
                cancellationToken);
            logger.ColourDarkened(revision, ordinal, previous, migrated);
            changed++;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.ColourCapApplied(changed);
    }

    private static string Migrated(string kind, string stored)
    {
        var darkened = OverlayColor.NearestAllowed(stored);
        if (darkened == stored || kind != ElementKind.Text.Value)
        {
            return darkened;
        }

        var minimumAlpha = TextLegibility.MinimumAlpha(OverlayColor.From(darkened));
        var alpha = Convert.ToInt32(darkened.Substring(7, 2), 16);
        return alpha >= minimumAlpha ? darkened : $"{darkened[..7]}{minimumAlpha:X2}";
    }

    private sealed record StoredElementColour(Guid RevisionId, int Ordinal, string Kind, string Color);
}
