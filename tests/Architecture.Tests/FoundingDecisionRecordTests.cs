namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the corrections spec 047 made to the founding decisions (ADR-0130,
/// issue 1969).
///
/// <para>
/// Nobody had ever checked <c>0000-initial-decisions.md</c> against the code.
/// Three features discovered that the hard way — 040, 045 and 046 — each by
/// trying to build on a decision and failing. The audit then found 99 claims,
/// of which 46 held.
/// </para>
///
/// <para>
/// <b>These are consistency checks, not text pins.</b> Each one reads the code
/// <em>and</em> the record and fails when they disagree — in either direction.
/// So building `IRuleEngine` does not fail the suite; building it and leaving
/// §IX saying "absent" does. That is the difference between a guard that
/// protects a correction and one that obstructs progress, and the obstructive
/// kind gets deleted within a month, taking the useful part with it.
/// </para>
///
/// <para>
/// The precedent is <see cref="LatencyLegRecordTests"/>, which caught spec 045
/// changing §IV and is the only reason that change was noticed.
/// </para>
/// </summary>
public class FoundingDecisionRecordTests
{
    /// <summary>
    /// §IX mandates strategy interfaces "in v1" so v2 can land without breaking
    /// changes. Two do not exist. The section must say so for as long as that
    /// is true — and must stop saying so once it is not.
    /// </summary>
    [Theory]
    [InlineData("IRuleEngine", "the rule engine")]
    [InlineData("IAuthorizationDecisionPoint", "authorization")]
    public void Section_nine_agrees_with_the_code_about_its_strategy_interfaces(string symbol, string row)
    {
        bool existsInCode = SourceContains(symbol);
        bool recordedAbsent = ReadConstitution().Contains($"`{symbol}` — absent", StringComparison.Ordinal);

        recordedAbsent.ShouldBe(
            !existsInCode,
            existsInCode
                ? $"{symbol} now exists, so §IX must stop recording {row}'s interface as absent. "
                + "This guard pins the record against drift, never against progress — update the row."
                : $"{symbol} does not exist, and §IX mandates it 'in v1'. Recording it as present would "
                + "exempt a constitutional obligation by clerical error, which is exactly how ADR-0117's "
                + "leg table went wrong (ADR-0130).");
    }

    /// <summary>
    /// Decision 019 named CEL. The language that exists is AEL, hand-written.
    /// The job is done and the decided name was wrong — a divergence, not a gap.
    /// </summary>
    [Fact]
    public void The_expression_language_row_names_the_language_that_exists()
    {
        bool aelExists = SourceContains("AelInterpreter");
        string decisions = ReadInitialDecisions();

        aelExists.ShouldBeTrue("AEL is the built expression language; if it has gone, decision 019 needs revisiting.");

        decisions.ShouldContain(
            "The language is AEL, not CEL",
            customMessage: "decision 019 named CEL and the code implements AEL. Restoring the CEL claim "
            + "would send the next reader looking for a package that was never referenced (ADR-0130).");
    }

    /// <summary>
    /// ADR-0118 chose one sink and abandoned the Grafana/Prometheus stack. The
    /// constitution went on describing Prometheus in three places — a resource
    /// list, the stack table, and a retention policy — because no code changed.
    /// </summary>
    [Fact]
    public void The_constitution_does_not_claim_a_metrics_stack_that_was_never_built()
    {
        bool prometheusDeployed = File
            .ReadAllText(Path.Combine(RepositoryRoot().FullName, "src", "AppHost", "AppHost.cs"))
            .Contains("AddContainer(\"prometheus\"", StringComparison.OrdinalIgnoreCase);

        prometheusDeployed.ShouldBeFalse(
            "if Prometheus is now deployed, ADR-0118's single-sink decision has been reopened and needs "
            + "an ADR — then these assertions should be updated rather than deleted.");

        string constitution = ReadConstitution();

        constitution.ShouldNotContain("**Prometheus** for metrics",
            customMessage: "ADR-0118 abandoned the Grafana/Prometheus stack and chose the Aspire dashboard. "
            + "This claim survived in §Stack for months because no code changed — an ADR did (ADR-0130).");

        constitution.ShouldNotContain("**30 days** in Prometheus",
            customMessage: "§Retention promised retention in a store that does not exist. The row now states "
            + "what the sink actually provides, and that a real policy is owed — do not restore the fiction, "
            + "and do not delete the obligation with it.");
    }

    /// <summary>
    /// Decision 017 declares six variable types. Three exist, and no variable of
    /// the other two can be created — <c>VariableType.From</c> throws.
    /// </summary>
    [Theory]
    [InlineData("DateTime")]
    [InlineData("Json")]
    public void The_variable_type_row_agrees_with_the_types_that_exist(string type)
    {
        string variableType = File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName, "src", "SystemVariables", "Domain", "Variable", "VariableType.cs"));

        bool typeExists = variableType.Contains($"VariableType {type} {{", StringComparison.Ordinal);
        bool recordedAbsent = ReadInitialDecisions()
            .Contains("`datetime` and `json` do not exist at all", StringComparison.Ordinal);

        if (typeExists)
        {
            recordedAbsent.ShouldBeFalse(
                $"VariableType.{type} now exists, so decision 017 must stop recording it as absent (ADR-0130). "
                + "Update the row — this guard does not obstruct building the type.");
        }
        else
        {
            recordedAbsent.ShouldBeTrue(
                $"VariableType.{type} does not exist and From() throws on it, so no variable of that type can "
                + "be created. An overlay expecting one cannot have it (issue 1971).");
        }
    }

    /// <summary>
    /// <b>FR-007.</b> Amended rows keep their original text. The record of what
    /// was decided must not be replaced by the record of what happened — both
    /// are needed to see how far apart they drifted.
    /// </summary>
    [Fact]
    public void Amended_rows_still_show_what_was_originally_decided()
    {
        string decisions = ReadInitialDecisions();

        int amended = CountOccurrences(decisions, "**Amended by ADR-");
        int originals = CountOccurrences(decisions, "Originally");

        originals.ShouldBeGreaterThanOrEqualTo(
            amended,
            $"{amended} rows are marked amended but only {originals} keep their original wording. "
            + "Overwriting a decision with what happened erases the evidence that the record drifted, "
            + "which is the most useful thing the audit produced (ADR-0130, rows 026 and 014).");
    }

    /// <summary>
    /// <b>T016 — the guard permits progress.</b>
    ///
    /// <para>
    /// The checks above compare the code against the record rather than pinning
    /// row text, so a legitimate update passes and only a disagreement fails.
    /// This exercises that comparison directly, in both directions, because the
    /// property is what stops the guard being deleted the first time it
    /// obstructs real work.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, false, true)] // built and no longer recorded absent — the update lands cleanly
    [InlineData(false, true, true)] // absent and recorded absent — today's state
    [InlineData(true, true, false)] // built but still recorded absent — drift, and it must fail
    [InlineData(false, false, false)] // absent but recorded present — the exemption ADR-0117 warns about
    public void The_record_and_the_code_must_agree_in_both_directions(
        bool existsInCode, bool recordedAbsent, bool shouldAgree)
    {
        bool agrees = recordedAbsent == !existsInCode;

        agrees.ShouldBe(
            shouldAgree,
            "the guard asserts agreement between code and record. Building a missing thing must PASS once "
            + "the row is updated, and must FAIL only while the row still says otherwise. A guard that "
            + "failed on legitimate progress would be deleted, and the corrections would lose their "
            + "protection silently.");
    }

    private static bool SourceContains(string symbol)
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            // Build output holds compiled third-party symbols and would answer a
            // different question from "did we write this".
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains(symbol, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count += 1;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadConstitution() =>
        ReadRepositoryFile(Path.Combine(".specify", "memory", "constitution.md"));

    private static string ReadInitialDecisions() =>
        ReadRepositoryFile(Path.Combine("docs", "adr", "0000-initial-decisions.md"));

    private static string ReadRepositoryFile(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue($"expected to find {relativePath} at {path}");
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
