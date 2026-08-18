using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Answers "which fabs exist" from the realm's <c>/fabs</c> group tree
/// (spec 019 FR-001).
///
/// <para>
/// This class is the only place in the system where EventIngestion's question
/// and Identity's answer meet, and it lives here for a reason that is easy to
/// lose: <c>AllowedCrossContext</c> in <c>BoundaryTests</c> is empty, so no
/// bounded context may reference another at any layer. MigrationRunner is not a
/// bounded context — it is the composition root for migrations (ADR-0067) and
/// already references all nine. Moving this file into either context would be
/// a boundary violation that the architecture test fails on.
/// </para>
/// </summary>
internal sealed class KeycloakProvisionedFabSource(
    IKeycloakAdminClient keycloak,
    ILogger<KeycloakProvisionedFabSource> logger) : IProvisionedFabSource
{
    /// <summary>The group whose children are the fabs.</summary>
    private const string FabGroupPath = "/fabs";

    public async Task<IReadOnlyList<FabIdentifier>> GetFabsAsync(CancellationToken cancellationToken)
    {
        // Not caught: an unreachable realm must fail the run rather than
        // provision nothing and report success (FR-011). "There are no fabs"
        // and "I could not tell" are the same value and opposite facts.
        IReadOnlyList<string> names =
            await keycloak.GetSubGroupNamesAsync(FabGroupPath, cancellationToken);

        List<FabIdentifier> fabs = [];
        List<string> unusable = [];
        foreach (string name in names)
        {
            try
            {
                FabIdentifier fab = FabIdentifier.From(name);
                if (!fabs.Contains(fab))
                {
                    fabs.Add(fab);
                }
            }
            catch (ArgumentException)
            {
                // Skipped, not fatal (FR-005): one group somebody named badly
                // must not stop every other fab from getting its storage. It is
                // still reported, because silently ignoring it is how a fab ends
                // up unable to store anything with nobody knowing why.
                unusable.Add(name);
            }
        }

        if (unusable.Count > 0)
        {
            logger.UnusableFabGroupNames(string.Join(", ", unusable), FabGroupPath);
        }

        if (fabs.Count == 0)
        {
            throw new InvalidOperationException(
                $"No usable fab found under '{FabGroupPath}' in the realm. Provisioning cannot " +
                "continue: every event written by any fab would be lost, and proceeding would " +
                "report success while doing nothing.");
        }

        return fabs;
    }
}
