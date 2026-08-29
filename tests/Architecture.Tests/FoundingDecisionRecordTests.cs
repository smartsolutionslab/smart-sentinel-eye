namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the corrections spec 047 made to the founding decisions (ADR-0130,
/// issue 1969).
///
/// <para>
/// Nobody had ever checked <c>0000-initial-decisions.md</c> against the code.
/// Three features discovered that the hard way — 040, 045 and 046 — each by
/// trying to build on a decision and failing.
/// </para>
///
/// <para>
/// <b>These are consistency checks, not text pins.</b> Each reads the code
/// <em>and</em> the record and fails when they disagree, in either direction.
/// Building <c>IRuleEngine</c> does not fail the suite; building it and leaving
/// §IX recording it as absent does. A guard that failed on legitimate work would
/// be deleted within a month, taking the corrections' protection with it.
/// </para>
///
/// <para>
/// <b>Every claim is checked independently.</b> An earlier draft shared one
/// sentinel sentence between two variable types, so building exactly one of them
/// left the suite unpassable by any wording — the obstruction this file says it
/// avoids. Found in code review.
/// </para>
/// </summary>
public class FoundingDecisionRecordTests
{
    /// <summary>
    /// §IX mandates strategy interfaces "in v1" so v2 can land without breaking
    /// changes. Two do not exist. The section must say so while that is true,
    /// and must stop saying so once it is not.
    /// </summary>
    [Theory]
    [InlineData("IRuleEngine", "the rule engine")]
    [InlineData("IAuthorizationDecisionPoint", "authorization")]
    public void Section_nine_agrees_with_the_code_about_its_strategy_interfaces(string symbol, string row)
    {
        bool existsInCode = DeclaredInSource(symbol);
        bool recordedAbsent = ReadConstitution().Contains($"`{symbol}` — absent", StringComparison.Ordinal);

        RecordMustAgree(
            existsInCode,
            recordedAbsent,
            whenBuilt: $"{symbol} now exists, so §IX must stop recording {row}'s interface as absent. "
            + "Update the row — this guard pins the record against drift, never against progress.",
            whenAbsent: $"{symbol} does not exist and §IX mandates it 'in v1'. Recording it as present "
            + "would exempt a constitutional obligation by clerical error, which is how ADR-0117's leg "
            + "table went wrong (ADR-0130).");
    }

    /// <summary>
    /// Decision 019 named CEL; the language that exists is AEL, hand-written.
    /// A divergence, not a gap — the job is done under another name.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> assert that AEL must exist. An earlier draft
    /// did, which would have blocked the very migration to CEL that decision 019
    /// originally mandated. The guard asserts agreement, not a preferred outcome.
    /// </remarks>
    [Fact]
    public void The_expression_language_row_names_the_language_that_exists()
    {
        bool aelExists = DeclaredInSource("AelInterpreter");
        bool recordsAel = ReadInitialDecisions()
            .Contains("The language is AEL, not CEL", StringComparison.Ordinal);

        RecordMustAgree(
            aelExists,
            !recordsAel,
            whenBuilt: "AEL is the built expression language and decision 019 must say so. Restoring the "
            + "CEL claim sends the next reader looking for a package that was never referenced (ADR-0130).",
            whenAbsent: "AEL has gone, so decision 019's correction no longer describes the code. If the "
            + "language changed, amend the row — do not leave it asserting AEL.");
    }

    /// <summary>
    /// ADR-0118 chose one sink and abandoned the Grafana/Prometheus stack. The
    /// constitution went on describing Prometheus in three places, because no
    /// code changed — an ADR did.
    /// </summary>
    [Fact]
    public void The_constitution_does_not_claim_a_metrics_stack_that_was_never_built()
    {
        string appHost = ReadRepositoryFile(Path.Combine("src", "AppHost", "AppHost.cs"));

        // Any declaration shape, not one literal: AppHost adds resources through
        // AddContainer, AddDockerfile and per-integration helpers alike, so
        // matching a single call would miss a Prometheus added another way.
        bool prometheusDeclared = appHost.Contains("prometheus", StringComparison.OrdinalIgnoreCase);
        string constitution = ReadConstitution();
        bool constitutionClaimsIt =
            constitution.Contains("**Prometheus** for metrics", StringComparison.Ordinal)
            || constitution.Contains("**30 days** in Prometheus", StringComparison.Ordinal);

        RecordMustAgree(
            prometheusDeclared,
            !constitutionClaimsIt,
            whenBuilt: "Prometheus now appears in AppHost, so ADR-0118's single-sink decision has been "
            + "reopened. That needs an ADR — then update these claims rather than deleting them.",
            whenAbsent: "ADR-0118 abandoned the Grafana/Prometheus stack. These claims survived in §Stack "
            + "and §Retention for months because no code changed (ADR-0130). §Retention now states what "
            + "the sink actually provides — restore the fiction and you restore the false promise with it.");
    }

    /// <summary>
    /// Decision 017 declares six variable types; three exist, and
    /// <c>VariableType.From</c> throws on the rest.
    /// </summary>
    /// <remarks>
    /// <b>One sentinel per type.</b> Sharing a sentence between the two made
    /// partial progress unrepresentable: implementing only <c>Json</c> required
    /// the sentence both gone and present.
    /// </remarks>
    [Theory]
    [InlineData("DateTime", "`datetime` does not exist")]
    [InlineData("Json", "`json` does not exist")]
    public void The_variable_type_row_agrees_with_the_types_that_exist(string type, string sentinel)
    {
        string variableType = ReadRepositoryFile(Path.Combine(
            "src", "SystemVariables", "Domain", "Variable", "VariableType.cs"));

        bool typeExists = variableType.Contains($"VariableType {type} {{", StringComparison.Ordinal);
        bool recordedAbsent = ReadInitialDecisions().Contains(sentinel, StringComparison.Ordinal);

        RecordMustAgree(
            typeExists,
            recordedAbsent,
            whenBuilt: $"VariableType.{type} now exists, so decision 017 must stop recording it as absent. "
            + "Update the row — building the type is exactly what this guard must not obstruct.",
            whenAbsent: $"VariableType.{type} does not exist and From() throws on it, so no variable of "
            + "that type can be created. An overlay expecting one cannot have it (issue 1971).");
    }

    /// <summary>
    /// <b>FR-007.</b> Amended rows keep their original text, checked per row —
    /// a file-wide count lets one row with two "Originally"s cover another that
    /// preserved nothing.
    /// </summary>
    [Fact]
    public void Every_amended_row_still_shows_what_was_originally_decided()
    {
        string[] amendedRows = ReadInitialDecisions()
            .Split('\n')
            .Where(line => line.Contains("**Amended by ADR-", StringComparison.Ordinal))
            .ToArray();

        amendedRows.ShouldNotBeEmpty("rows 014, 026 and spec 047's six are amended; finding none means "
            + "the annotations were lost.");

        foreach (string row in amendedRows)
        {
            string id = row.Length > 7 ? row[..7] : row;
            row.ShouldContain(
                "Originally",
                customMessage: $"amended row '{id}…' does not keep its original wording. Overwriting a "
                + "decision with what happened erases the evidence that the record drifted, which is the "
                + "most useful thing the audit produced (ADR-0130).");
        }
    }

    /// <summary>
    /// The comparison every guard above uses. Failing here is the guard working:
    /// the record and the code disagree, and one of them must move.
    /// </summary>
    private static void RecordMustAgree(bool existsInCode, bool recordedAbsent, string whenBuilt, string whenAbsent) =>
        recordedAbsent.ShouldBe(!existsInCode, existsInCode ? whenBuilt : whenAbsent);

    /// <summary>
    /// Whether the repository <b>declares</b> a symbol, ignoring comments.
    /// </summary>
    /// <remarks>
    /// A raw substring scan counted a reminder comment mentioning <c>IRuleEngine</c> as the
    /// interface existing, which would have demanded §IX record a nonexistent
    /// seam as present — the exemption this whole file exists to prevent.
    /// </remarks>
    private static bool DeclaredInSource(string symbol)
    {
        string root = RepositoryRoot().FullName;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string line in File.ReadLines(file))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal)
                    || code.StartsWith('*')
                    || code.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (code.Contains(symbol, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ReadConstitution() =>
        ReadRepositoryFile(Path.Combine(".specify", "memory", "constitution.md"));

    private static string ReadInitialDecisions() =>
        ReadRepositoryFile(Path.Combine("docs", "adr", "0000-initial-decisions.md"));

    private static string ReadRepositoryFile(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllText(path);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
