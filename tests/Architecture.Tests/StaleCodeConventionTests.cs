using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the stale-version vocabulary (ADR-0119).
///
/// <para>
/// A refusal because the caller's version has moved must carry a code ending
/// <c>_STALE</c>. The HTTP status must not be used to identify one, because both
/// statuses in play are overloaded: <c>409</c> also covers name collisions and
/// terminal-state refusals, and <c>412</c> also covers Identity's upsert
/// preconditions.
/// </para>
///
/// <para>
/// This exists because the convention was followed by imitation for six
/// contexts and missed by the seventh, and nothing noticed. Spec 029 shipped
/// <c>CAMERA_VERSION_MISMATCH</c>; the shared client's stale predicate did not
/// recognise it, so an operator with a stale version was told to <b>try
/// again</b> — which resubmits unchanged and replays their edit over the
/// winner's, the exact lost update the whole mechanism exists to prevent.
/// </para>
///
/// <para>
/// Reads source rather than reflecting over assemblies, because
/// <c>ApiError</c> takes its code as a constructor argument, so the value
/// exists only once an instance is built. <c>HandlerDeconstructionTests</c>
/// reads source for a comparable reason.
/// </para>
/// </summary>
public class StaleCodeConventionTests
{
    /// <summary>
    /// Any screaming-snake string literal — the shape every <c>ApiError</c> code
    /// takes.
    /// </summary>
    private static readonly Regex ErrorCodeLiteral = new(
        @"""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Words a context reaches for when it means "your version has moved" and
    /// has not read ADR-0119. <c>MISMATCH</c> is the one that actually happened.
    /// </summary>
    private static readonly string[] MeansStale =
        ["VERSION_MISMATCH", "VERSION_CONFLICT", "VERSION_OUTDATED", "STALE_VERSION", "REVISION_MISMATCH", "CONCURRENCY_CONFLICT"];

    [Fact]
    public void A_lost_update_is_named_STALE_and_nothing_else()
    {
        List<string> offenders = [];

        foreach ((string path, string source) in ReadSources())
        {
            foreach (Match match in ErrorCodeLiteral.Matches(source))
            {
                string code = match.Groups[1].Value;

                if (code.EndsWith("_STALE", StringComparison.Ordinal))
                {
                    continue;
                }

                if (MeansStale.Any(phrase => code.Contains(phrase, StringComparison.Ordinal)))
                {
                    offenders.Add($"{path}: {code}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "ADR-0119: a refusal because the caller's version is no longer current must carry a code "
            + "ending '_STALE'. The shared client keys on that suffix to decide what an operator is told; "
            + "a code named any other way falls through to generic wording that tells them to try again, "
            + "which replays their change over the other writer's. Rename the code(s) above to end '_STALE'. "
            + "The HTTP status is free — 409 and 412 are both in use and neither is authoritative.");
    }

    /// <summary>
    /// The counterpart to the check above, which would pass just as happily
    /// against a codebase with no optimistic concurrency at all: an empty set of
    /// offenders proves nothing unless something is known to be there.
    ///
    /// <para>
    /// Eight today — seven per-context refusals plus the shared Layer-2 handler
    /// in ServiceDefaults. That eighth is why this feature turned out bigger
    /// than #1857 described: it was named <c>AGGREGATE_VERSION_CONFLICT</c>, so
    /// no client recognised the true database race as a lost update, in
    /// <em>every</em> context rather than only the one the issue named.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_lost_update_refusal_in_the_product_is_accounted_for()
    {
        HashSet<string> codes = new(StringComparer.Ordinal);

        foreach ((_, string source) in ReadSources())
        {
            foreach (Match match in ErrorCodeLiteral.Matches(source))
            {
                string code = match.Groups[1].Value;
                if (code.EndsWith("_STALE", StringComparison.Ordinal))
                {
                    codes.Add(code);
                }
            }
        }

        codes.ShouldBe(
            [
                "AGGREGATE_VERSION_STALE",
                "CAMERA_VERSION_STALE",
                "LAYOUT_REVISION_STALE",
                "OVERLAY_REVISION_STALE",
                "RULE_STALE",
                "VARIABLE_STALE",
                "WEBHOOK_CLIENT_STALE",
                "WEBHOOK_INTEGRATION_STALE",
            ],
            ignoreOrder: true,
            "Eight codes: seven per-context refusals plus AGGREGATE_VERSION_STALE, the shared Layer-2 "
            + "handler in ServiceDefaults that covers the true database race for every mutating endpoint. "
            + "If this list shrank, a context lost its concurrency "
            + "refusal; if it grew, a new one arrived and the shared client's tests should cover it too "
            + "(specs/031-stale-version-convention).");
    }

    private static Dictionary<string, string> ReadSources()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetRelativePath(root.FullName, f), File.ReadAllText, StringComparer.Ordinal);
    }
}
