using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

/// <summary>
/// Names the fabs a rule name resolved in, for the ambiguity a by-name read
/// hits when a multi-fab caller holds the same name twice. Shared by the read
/// and dry-run handlers so neither has to reach into the other.
/// </summary>
internal static class RuleFabCandidates
{
    /// <summary>
    /// Naming them leaks nothing — the caller holds every one of them.
    /// Distinct: with more than one match sharing a fab, a caller-facing
    /// sentence that asserts "more than one fab" must not name one of them
    /// twice — the refusal branch below is keyed on this collection's count,
    /// not on the match count, so distinctness is what makes that count true.
    /// </summary>
    internal static IReadOnlyList<string> Fabs(IEnumerable<Rule> matches) =>
        [.. matches.Select(match => match.Fab.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
