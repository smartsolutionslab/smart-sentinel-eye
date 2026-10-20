using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Queries.Handlers;

/// <summary>
/// The one rule for which audit rows a fab-scoped read may return: a
/// row of one of the caller's fabs, or a row whose lack of a fab is by design
/// (<see cref="FabAttribution.NotApplicable"/>). A row with no fab that nobody
/// resolved (<see cref="FabAttribution.Unresolved"/>) belongs to a fab the
/// caller cannot be shown to hold, so it is hidden. A caller with no usable fab
/// claim is unscoped and sees only the fab-neutral rows.
/// </summary>
internal sealed class AuditReadScope
{
    private readonly List<FabIdentifier> fabs;

    private AuditReadScope(List<FabIdentifier> fabs)
    {
        this.fabs = fabs;
    }

    public bool IsScoped => fabs.Count > 0;

    public static AuditReadScope Named(FabIdentifier fab) => new([fab]);

    // Per entry, not all-or-nothing: a single group outside FabIdentifier's
    // grammar (nested group, wrong case) is skipped rather than failing the
    // whole read. Not logged: the misconfiguration belongs to the realm's
    // Keycloak group setup, not something this caller can act on.
    public static AuditReadScope ForCaller(IReadOnlyList<string> callerFabs)
    {
        List<FabIdentifier> usable = [];
        foreach (var candidate in callerFabs)
        {
            try
            {
                usable.Add(FabIdentifier.From(candidate));
            }
            catch (ArgumentException)
            {
                // Skipped: one malformed group must not fail the whole read.
            }
        }

        return new(usable);
    }

    public IQueryable<AuditEventEntity> Apply(IQueryable<AuditEventEntity> source)
    {
        var allowed = fabs;
        return IsScoped
            ? source.Where(auditEvent =>
                (auditEvent.Fab == null && auditEvent.FabAttribution == FabAttribution.NotApplicable)
                || (auditEvent.Fab != null && allowed.Contains(auditEvent.Fab)))
            : source.Where(auditEvent =>
                auditEvent.Fab == null && auditEvent.FabAttribution == FabAttribution.NotApplicable);
    }
}
