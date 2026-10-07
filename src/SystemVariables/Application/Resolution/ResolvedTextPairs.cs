using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// The one place the pair-by-template rule is written (spec 301, #2720,
/// US2, FR-005). Every producer of a resolved-text set — the push
/// (<c>VariableValueChangedDomainEventHandler</c>,
/// <c>VariableArchivedDomainEventHandler</c>) and the snapshot read
/// (<c>GetOverlaySnapshotQueryHandler</c>) — calls this, so the rule
/// cannot diverge between them.
/// </summary>
public static class ResolvedTextPairs
{
    /// <summary>
    /// Resolves every label template in <paramref name="templates"/> against
    /// <paramref name="snapshot"/>, then collapses the result to the set a
    /// consumer pairs by template: a shape's empty-string placeholder is
    /// skipped (it has no template to key by), and a template already seen
    /// is dropped (<see cref="StringComparer.Ordinal"/>), keeping the first
    /// occurrence's position. Order is the order of first appearance.
    /// </summary>
    public static IReadOnlyList<(string Template, string Resolved)> Build(
        IReadOnlyList<string> templates,
        IResolver resolver,
        IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot)
    {
        Ensure.That(templates).IsNotNull();
        Ensure.That(resolver).IsNotNull();
        Ensure.That(snapshot).IsNotNull();

        List<(string Template, string Resolved)> pairs = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string template in templates)
        {
            if (template.Length == 0 || !seen.Add(template))
            {
                continue;
            }

            pairs.Add((template, resolver.Resolve(template, snapshot)));
        }

        return pairs;
    }
}
