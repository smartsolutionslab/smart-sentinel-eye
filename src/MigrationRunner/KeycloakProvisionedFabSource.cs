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
///
/// <para>
/// The question is asked once, because a second ask cannot answer differently.
/// <c>/fabs</c> and its children are imported with the realm, from the same
/// document as the <c>migration-runner</c> client whose token this call needs,
/// and Keycloak binds its HTTP listener only after that import has finished —
/// so a caller that can reach the admin API at all is talking to a realm whose
/// groups are already there. Nothing creates a fab group at runtime either.
/// An empty tree is therefore a verdict, not a "not yet".
/// </para>
///
/// <para>
/// Three verdicts end the run, and they are three because they send the reader
/// to three different places (#2139): the <c>/fabs</c> group is not in the
/// realm, or it is there holding nothing, or it is there holding names none of
/// which is a fab. Until the group tree could say which of the first two it
/// meant, they shared a sentence that hedged between them.
/// </para>
/// </summary>
internal sealed class KeycloakProvisionedFabSource(
    IKeycloakAdminClient keycloak,
    ILogger<KeycloakProvisionedFabSource> logger) : IProvisionedFabSource
{
    /// <summary>The group whose children are the fabs.</summary>
    private const string FabGroupPath = "/fabs";

    /// <summary>
    /// What every fatal verdict here ends with. Held once so that the three
    /// stay three sentences about three different causes rather than drifting
    /// into one shared one, which is the defect #2062 and #2139 each closed
    /// half of.
    /// </summary>
    private const string WhyItIsFatal =
        "Provisioning cannot continue: every event written by any fab would be lost, and " +
        "proceeding would report success while doing nothing.";

    /// <summary>
    /// The group itself is not in the realm. Sends the reader to the realm
    /// document and to the data volume, because those are the two ways the
    /// group can be missing from a realm that imported it.
    /// </summary>
    private const string AbsentGroupVerdict =
        $"No group at '{FabGroupPath}' in the realm at all: the group itself is not there. It " +
        "is imported with the realm and nothing creates it later, so either the realm was " +
        "imported without it, or a warm data volume holds an older realm whose import was " +
        $"skipped. {WhyItIsFatal}";

    /// <summary>
    /// The group answered, and has no children. Sends the reader to the fabs
    /// declared under it — a different fix from the one above, which is why
    /// this is a different sentence.
    /// </summary>
    private const string ChildlessGroupVerdict =
        $"The group '{FabGroupPath}' is in the realm and has no children: no fab is declared " +
        "under it. Nothing creates a fab group at runtime, so re-asking cannot change this " +
        $"answer. {WhyItIsFatal}";

    public async Task<IReadOnlyList<FabIdentifier>> GetFabsAsync(CancellationToken cancellationToken)
    {
        // Not caught: an unreachable realm must fail the run rather than
        // provision nothing and report success (FR-011). "There are no fabs"
        // and "I could not tell" are opposite facts, and catching here would
        // make them one again from the other direction.
        Option<IReadOnlyList<string>> tree =
            await keycloak.GetSubGroupNamesAsync(FabGroupPath, cancellationToken);

        if (!tree.HasValue)
        {
            throw new InvalidOperationException(AbsentGroupVerdict);
        }

        IReadOnlyList<string> names = tree.Value;

        if (names.Count == 0)
        {
            throw new InvalidOperationException(ChildlessGroupVerdict);
        }

        return Usable(names);
    }

    /// <summary>
    /// The fabs among <paramref name="names"/>, or a failed run. Reached only
    /// once the group tree has answered with something, so the verdict here is
    /// about the names rather than about the realm.
    /// </summary>
    private List<FabIdentifier> Usable(IReadOnlyList<string> names)
    {
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
                $"No usable fab found under '{FabGroupPath}' in the realm: {names.Count} group(s) " +
                $"are there and none is a usable fab name ({string.Join(", ", unusable)}). " +
                WhyItIsFatal);
        }

        return fabs;
    }
}
