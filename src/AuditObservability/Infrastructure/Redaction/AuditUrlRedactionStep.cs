using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.Redaction;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Redaction;

/// <summary>
/// Reduces the camera URLs that audit rows stored before spec 377 to their origin, in place (ADR-0184). A dry
/// run by default: it counts and changes nothing. An applied run issues one guarded <c>UPDATE</c> per row, each
/// committing on its own, so an interrupted run leaves every finished row finished and the next run resumes.
/// URLs reach the database only as parameters and never reach a log line or an exception.
/// </summary>
public sealed class AuditUrlRedactionStep(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    IOptions<AuditUrlRedactionOptions> options,
    ILogger<AuditUrlRedactionStep> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (examined, pending) = await FindUnredactedAsync(context, cancellationToken);

        if (!options.Value.Apply)
        {
            ReportDryRun(examined, pending);
            return;
        }

        var rewritten = 0;
        List<Guid> rewrittenEvents = [];

        foreach (var row in pending)
        {
            if (await RewriteAsync(context, row, cancellationToken) == 1)
            {
                rewritten++;
                rewrittenEvents.Add(row.EventIdentifier);
            }
        }

        var (_, remaining) = await FindUnredactedAsync(context, cancellationToken);

        logger.UrlRedactionApplied(
            examined,
            pending.Count,
            rewritten,
            remaining.Count,
            string.Join(',', rewrittenEvents));
    }

    private void ReportDryRun(int examined, IReadOnlyList<Pending> pending) =>
        logger.UrlRedactionDryRun(
            examined,
            pending.Count(row => row.EventKind == nameof(CameraRegisteredV1)),
            pending.Count(row => row.EventKind == nameof(CameraAddressChangedV1)),
            pending.Count == 0 ? null : pending.Min(row => row.OccurredAt),
            pending.Count == 0 ? null : pending.Max(row => row.OccurredAt));

    private static async Task<(int Examined, IReadOnlyList<Pending> Pending)> FindUnredactedAsync(
        AuditObservabilityDbContext context,
        CancellationToken cancellationToken)
    {
        string[] kinds = [.. StoredCameraUrlRewrite.EventKinds];

        IReadOnlyList<StoredRow> rows = await context.Database
            .SqlQuery<StoredRow>(
                $"""
                SELECT
                    audit_id AS "AuditId",
                    occurred_at AS "OccurredAt",
                    event_identifier AS "EventIdentifier",
                    event_kind AS "EventKind",
                    payload->>'Url' AS "Url",
                    payload->>'PreviousUrl' AS "PreviousUrl"
                FROM audit_events
                WHERE event_kind = ANY({kinds})
                  AND schema_version = 1
                """)
            .ToListAsync(cancellationToken);

        List<Pending> pending = [];

        foreach (var row in rows)
        {
            var plan = StoredCameraUrlRewrite.PlanFor(row.EventKind, StoredValues(row));

            if (plan.HasValue)
            {
                pending.Add(new(row.AuditId, row.OccurredAt, row.EventIdentifier, row.EventKind, plan.Value));
            }
        }

        return (rows.Count, pending);
    }

    private static Dictionary<string, string?> StoredValues(StoredRow row)
    {
        var keys = StoredCameraUrlRewrite.KeysFor(row.EventKind);
        Dictionary<string, string?> stored = [];

        if (keys.Contains(nameof(StoredRow.Url)))
        {
            stored[nameof(StoredRow.Url)] = row.Url;
        }

        if (keys.Contains(nameof(StoredRow.PreviousUrl)))
        {
            stored[nameof(StoredRow.PreviousUrl)] = row.PreviousUrl;
        }

        return stored;
    }

    // One statement shape per set of changed keys, so the key names are fixed text and every value, old and
    // new, is a parameter. Each guard re-checks the value that was read, so a row changed in between is skipped.
    private static Task<int> RewriteAsync(
        AuditObservabilityDbContext context,
        Pending row,
        CancellationToken cancellationToken)
    {
        var (auditId, occurredAt, _, _, plan) = row;
        var url = plan.Changes.FirstOrDefault(change => change.Key == nameof(StoredRow.Url));
        var previousUrl = plan.Changes.FirstOrDefault(change => change.Key == nameof(StoredRow.PreviousUrl));
        var delta = plan.SizeDelta;

        FormattableString statement = (url, previousUrl) switch
        {
            ({ } u, { } p) =>
                $$"""
                UPDATE audit_events SET
                    payload = jsonb_set(jsonb_set(payload, '{Url}', to_jsonb({{u.New}}::text)), '{PreviousUrl}', to_jsonb({{p.New}}::text)),
                    payload_size_bytes = payload_size_bytes - {{delta}},
                    schema_version = 2
                WHERE audit_id = {{auditId}} AND occurred_at = {{occurredAt}} AND schema_version = 1
                  AND payload->>'Url' = {{u.Old}} AND payload->>'PreviousUrl' = {{p.Old}}
                """,
            ({ } u, null) =>
                $$"""
                UPDATE audit_events SET
                    payload = jsonb_set(payload, '{Url}', to_jsonb({{u.New}}::text)),
                    payload_size_bytes = payload_size_bytes - {{delta}},
                    schema_version = 2
                WHERE audit_id = {{auditId}} AND occurred_at = {{occurredAt}} AND schema_version = 1
                  AND payload->>'Url' = {{u.Old}}
                """,
            (null, { } p) =>
                $$"""
                UPDATE audit_events SET
                    payload = jsonb_set(payload, '{PreviousUrl}', to_jsonb({{p.New}}::text)),
                    payload_size_bytes = payload_size_bytes - {{delta}},
                    schema_version = 2
                WHERE audit_id = {{auditId}} AND occurred_at = {{occurredAt}} AND schema_version = 1
                  AND payload->>'PreviousUrl' = {{p.Old}}
                """,
            _ => throw new InvalidOperationException("A pending row always has a change to a registered key."),
        };

        return context.Database.ExecuteSqlAsync(statement, cancellationToken);
    }

    private sealed record StoredRow(
        Guid AuditId,
        DateTimeOffset OccurredAt,
        Guid EventIdentifier,
        string EventKind,
        string? Url,
        string? PreviousUrl);

    private sealed record Pending(
        Guid AuditId,
        DateTimeOffset OccurredAt,
        Guid EventIdentifier,
        string EventKind,
        StoredCameraUrlRewrite.Plan Plan);
}
