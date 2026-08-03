using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application.Queries.Handlers;

/// <summary>
/// Formats the fabs a rule name resolved in, for the ambiguity a by-name read
/// hits when a multi-fab caller holds the same name twice. Shared by the read
/// and dry-run handlers so neither has to reach into the other.
/// </summary>
internal static class RuleFabCandidates
{
    /// <summary>
    /// Naming them leaks nothing — the caller holds every one of them.
    /// </summary>
    internal static string Describe(IEnumerable<Rule> matches) =>
        string.Join(", ", matches.Select(match => match.Fab.Value).Order(StringComparer.Ordinal));
}
