using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the <b>mechanically checkable</b> claims the subagent briefs make
/// about this repository — spec 068, issue 2058.
///
/// <para>
/// <c>.claude/agents/*.md</c> and <c>.claude/commands/*.md</c> are the whole
/// context a subagent has under ADR-0144: no CLAUDE.md breadth to catch a
/// contradiction, no human reading over its shoulder, and in the autonomous
/// lane no reviewer outside the same set of briefs. Nothing checked them
/// against the repository they describe, and six wrong claims accumulated. This
/// is the defect ADR-0130 named in the founding decisions — a record nobody
/// checks against the thing it describes — and a consistency guard is the
/// remedy that worked there.
/// </para>
///
/// <para>
/// <b>Commands are in scope for the same reason agents are.</b> Same file
/// format, same consumption, same failure mode, same glob; excluding them would
/// leave identical failure modes outside the guard for no saving.
/// </para>
///
/// <para>
/// <b>Three claim classes, three registers, none of them declared here.</b>
/// Every expectation is derived from the repository at run time. A guard that
/// reads its expectations out of a hand-written list proves that somebody was
/// told, not that the record is true — and the two look identical from a green
/// build.
/// </para>
///
/// <para>
/// <i>Decisions.</i> The ADR register is a <b>union</b>, and the union is
/// load-bearing: files begin at <c>0028</c>, and decisions 001–027 exist only
/// as 27 rows in <c>docs/adr/0000-initial-decisions.md</c>. A file-existence
/// register alone is red on arrival for <c>ADR-0007</c>, <c>ADR-0024</c> and
/// <c>ADR-0026</c> — all three correct citations, all three live in today's
/// briefs. A guard that fails on correct work in its first week gets deleted,
/// taking the protection with it.
/// </para>
///
/// <para>
/// <i>Paths.</i> A path claim is an inline-code span containing <c>/</c> whose
/// first segment — <b>after a leading <c>/</c> is trimmed</b> — is an existing
/// top-level entry of the repository. That single anchoring rule is both the
/// recogniser and the entire false-positive story: a route
/// (<c>camera-catalog/cameras</c>), a slash-command (<c>/speckit-plan</c>), a
/// folder convention (<c>Commands/</c>) and an interpolated URL are all naturally
/// unanchored, and real repository paths are naturally anchored. A span carrying a
/// glob metacharacter must match at least one entry — stricter than skipping it,
/// and it is how a brief legitimately names a <em>class</em> of file.
/// </para>
///
/// <para>
/// <b>The leading slash is trimmed rather than read as unanchored</b>, and the
/// census is the argument. The corpus carries 18 spans beginning <c>/</c>. Fifteen
/// are slash-commands or routes — <c>/speckit-plan</c>, <c>/verify</c>,
/// <c>/code-review</c>, <c>/cameras</c>, <c>/&lt;context&gt;</c> — whose first
/// segment is not a root entry with or without the slash, so trimming changes
/// nothing about them. The other three are the repo-root Playwright directory,
/// written <c>/e2e</c>, at <c>frontend-engineer.md:13</c>,
/// <c>test-writer.md:10</c> and <c>test-adversary.md:19</c>. Untrimmed, those
/// three are claims the guard cannot see — and so is
/// <c>/e2e/support/does-not-exist.ts</c>, a wrong path written in exactly the
/// spelling the briefs already use. Trimming moves three real claims from
/// invisible to checked and adds no false positive.
/// </para>
///
/// <para>
/// <i>CI job facts.</i> Deliberately narrowed to attributes of a <b>named
/// job</b>, not to config keys in general, and the narrowing is what makes the
/// arm workable at all. Two live sentences break the general form in opposite
/// directions: <c>infra-reviewer.md</c> uses "a <c>continue-on-error</c>
/// masking a real failure" as a review <em>hypothetical</em>, which a
/// presence rule turns red; <c>infra-engineer.md</c> correctly states that
/// "there is no <c>continue-on-error</c> anywhere in the file", which the
/// inverse rule turns red instead. Binding the claim to a job named in an
/// attribute position removes most of the polarity problem, and it is the shape
/// the error that actually happened had.
/// </para>
///
/// <para>
/// <b>What is left of the polarity problem is read with a negation vocabulary,
/// because it has to be.</b> The rule that an explicit "blocking" wins over a
/// <c>continue-on-error</c> mentioned beside it <em>is</em> a polarity read, and
/// a rule that only knows the prefix <c>non-</c> is wrong the first time someone
/// writes "not blocking" — a sentence false in precisely the way #2055's was,
/// and one that would satisfy the precedence rule and suppress the check.
/// So "blocking" negated by <c>non-</c>, "not", "never" or "no longer" is read
/// as a <em>non-blocking</em> claim rather than as no claim at all, and markdown
/// emphasis is stripped first so <c>**not blocking**</c> reads the same as
/// <c>not blocking</c>. The vocabulary is finite and an author who negates in a
/// word outside it walks past — that is the same declared price the rest of this
/// guard pays, and it is bounded by the fact that the un-negated word is the one
/// that stays checked.
/// </para>
///
/// <para>
/// <b>An attribute claim is not confined to an enumeration.</b> Three live
/// sentences say "the blocking CI <c>e2e</c> job" — a named job, an attribute,
/// in this arm's own vocabulary — outside any block that names <c>ci.yml</c> and
/// says "jobs". A rule that read attributes only from enumerations left all
/// three unchecked, so a brief could say <c>e2e</c> is non-blocking in three
/// files while the two enumerating bullets went red on the same fact. A block
/// that does not enumerate is therefore read sentence by sentence, and a
/// sentence naming exactly one workflow job key in an inline-code span makes an
/// attribute claim about that job.
/// </para>
///
/// <para>
/// <b>Under-recognition is the silent failure.</b> A claim the guard does not
/// parse is a claim it does not check, and from a green build that is
/// indistinguishable from compliance. So unparseable input is reported by name
/// rather than skipped — an ADR-shaped token that is not a recognised spelling
/// (assertion 2), a CI block that enumerates jobs but yields none (assertion
/// 6) — the corpus is asserted <b>per file</b> rather than in aggregate
/// (assertion 7), and a floor guards against every file being read while the
/// recognisers return nothing (assertion 8).
/// </para>
///
/// <para>
/// <b>That floor is per class, and the aggregate alone would not have been
/// one.</b> Three recognisers feed it, and summing them lets two of the three
/// die in silence: with the path recogniser broken the corpus loses every path
/// claim, assertion 3 passes vacuously for all 13 briefs, and a single total
/// still clears a single threshold on the citations alone. So each class carries
/// its own floor, and the total carries a fourth — a recogniser that stops
/// matching now falls through the floor belonging to it.
/// </para>
///
/// <para>
/// <b>Declared limits — stated here rather than discovered later.</b>
/// </para>
///
/// <para>
/// <i>Semantic claims are out of reach by construction.</i> "NRT is disabled",
/// "the publisher has never been run", "the two apps do not share a client" are
/// statements about how the system behaves or what has happened, not about a
/// token that does or does not exist. Two of the six known brief errors are of
/// this kind and no amount of parsing brings them in; issue 2081 is the half
/// that addresses them, by putting the obligation on the change that knows it
/// is changing the thing.
/// </para>
///
/// <para>
/// <i>A correct citation attached to a wrong claim passes every arm.</i>
/// <c>ADR-0048</c> exists, so "NRT disabled (ADR-0048)" is green here. This
/// guard checks that a citation <em>resolves</em>, never that it
/// <em>supports</em>.
/// </para>
///
/// <para>
/// <i>Unanchored prose is not checked at all.</i> That is the deliberate price
/// of having no exemption list: <c>Commands/</c>, <c>App.tsx</c> and
/// <c>camera-catalog/cameras</c> are outside the guard, and so is a genuinely
/// wrong path written the same way. The editorconfig section header
/// <c>[src/AppHost/**.cs]</c> is likewise not a claim, because the span begins
/// <c>[</c>.
/// </para>
///
/// <para>
/// <i>A symbol name in an inline span is not a path claim.</i>
/// <c>IdempotentRequest.ExecuteCreateAsync</c>, <c>RetryEveryMethod()</c> and
/// <c>WaitOnResourceUnavailable</c> name things in the source rather than files,
/// carry no <c>/</c>, and are never anchored. Swept by hand on 2026-09-05: every
/// such span in the corpus resolves to something that exists, so this is a
/// latent gap rather than a live defect — recorded here because a gap nobody
/// wrote down is the one the next reader assumes is covered.
/// </para>
///
/// <para>
/// <i>A path inside a fenced code block is not read.</i> The recogniser is the
/// inline-code span, and a fenced block's lines carry no backticks —
/// <c>commands/verify.md</c> tells an agent to run
/// <c>pwsh scripts/coverage-check.ps1</c> and
/// <c>dotnet test tests/Integration.Tests/…</c> inside fences, which is the very
/// place a wrong path costs an agent a run. Both resolve today, swept on the
/// same date. Reading fences means splitting a command line into paths and flags
/// and is a larger recogniser than this issue; declared rather than half-built.
/// </para>
///
/// <para>
/// <i><c>.claude/skills/</c> is deliberately outside the sweep.</i> The briefs
/// are <c>agents</c> and <c>commands</c>; <c>skills/*/SKILL.md</c> is vendored
/// Spec-Kit prose full of shell that a path recogniser would immediately
/// misread as repository claims. Including it would buy false positives, not
/// coverage. Assertion 7 discovers <c>agents</c> and <c>commands</c> directories
/// at any depth, so a brief filed under a restructured tree is still named.
/// </para>
///
/// <para>
/// <i>CI attributes are read at job level.</i> A <c>continue-on-error</c> on a
/// single <em>step</em> is not this arm's subject, and an explicit un-negated
/// "blocking" in a job's clause takes precedence over a <c>continue-on-error</c>
/// mentioned inside it — which is exactly how <c>infra-engineer.md</c>'s
/// correct negative claim stays green. A job for which a brief states no
/// attribute makes no claim, so silence is not checkable.
/// </para>
///
/// <para>
/// <i>A <c>needs</c> claim is read only from an enumeration.</i> Inside an
/// enumeration "needs" sits in a job's parenthesised attribute clause and is a
/// claim; outside one it is ordinary English — <c>commands/verify.md</c>'s
/// "Playwright needs the stack up" sits in the same sentence as
/// <c>e2e</c> and means nothing about the workflow's <c>needs:</c> key. So an
/// attribute sentence outside an enumeration is read for polarity only. A
/// sentence naming two or more jobs outside an enumeration is likewise not
/// attributed to either, because the guard cannot tell which one the attribute
/// belongs to; there is no such sentence today.
/// </para>
///
/// <para>
/// <i>Recall is unprovable.</i> No assertion can show the recognisers see every
/// claim. Assertions 2, 6, 7 and 8 narrow the gap by making unparseable input
/// and a collapsed corpus loud, but a claim written in a shape nobody imagined
/// is still invisible.
/// </para>
///
/// <para>
/// <b>Forward slashes throughout, and every path comparison in this file is
/// written against them.</b> <see cref="Path.GetRelativePath"/> returns the
/// <em>platform</em> separator, so a filter or an expected string written with
/// a backslash literal is green on a Windows developer machine and red on Linux
/// CI — the worst direction for a guard to break, because it passes exactly
/// where nobody looks. This guard is entirely about paths, which makes it the
/// highest-risk mistake available here.
/// </para>
///
/// <para>
/// <b>No way to excuse a brief</b> (assertion 9). A departure from these rules
/// is a blocked outcome a human accepts in writing; ADR-0144 forbids the lane
/// from reaching green by weakening a gate.
/// </para>
/// </summary>
public class AgentBriefClaimTests
{
    private const string GuardSource = "tests/Architecture.Tests/AgentBriefClaimTests.cs";
    private const string BriefRoot = ".claude";
    private const string Workflow = ".github/workflows/ci.yml";
    private const string FoundingDecisions = "docs/adr/0000-initial-decisions.md";
    private const string AdrDirectory = "docs/adr";

    /// <summary>
    /// <b>What the corpus actually carries, measured with this guard's own
    /// definitions on 2026-09-05.</b>
    ///
    /// <para>
    /// <b>80 ADR citation sites → 104 citation claims.</b> The site count is
    /// what <c>grep -o ADR-\d{4}</c> reports; a claim is a decision number, and
    /// the compound continuation the briefs already use adds 23 —
    /// <c>ADR-0038/0046/0066/0039/0090/0091/0094</c> is one site and seven
    /// claims — plus the single <c>adr/0144-</c> path citation. The floor is a
    /// floor on claims, so it is claims that are counted here.
    /// </para>
    ///
    /// <para>
    /// <b>37 anchored path spans</b>, counted per occurrence — 22 distinct
    /// spellings, 33 distinct within a file — because an occurrence is what the
    /// recogniser yields and what a floor on it measures. <b>11 CI job
    /// claims</b>: 4 jobs in each of the two enumerating bullets, plus the 3
    /// sentences that describe <c>e2e</c> outside an enumeration.
    /// <b>152 recognised claims</b> in total.
    /// </para>
    ///
    /// <para>
    /// The floors below are half of each, rounded down. This class doc has
    /// carried three wrong figures already — 27 path spans matched no
    /// reproducible definition at all — which is precisely the defect the guard
    /// exists to catch, so re-measure before editing rather than adjusting a
    /// number until it looks right.
    /// </para>
    /// </summary>
    private const int SmallestPlausibleClaimCount = 76;

    /// <summary>Half of the 104 decision claims. See <see cref="SmallestPlausibleClaimCount"/>.</summary>
    private const int SmallestPlausibleDecisionCount = 52;

    /// <summary>Half of the 37 anchored path spans.</summary>
    private const int SmallestPlausiblePathCount = 18;

    /// <summary>Half of the 11 CI job claims.</summary>
    private const int SmallestPlausibleJobCount = 5;

    /// <summary>
    /// The directory names that hold briefs. Used twice, on purpose and by two
    /// different methods: <see cref="BriefFiles"/> sweeps the two directly
    /// under <c>.claude/</c>, and <see cref="DiscoveredBriefFiles"/> finds them
    /// by name at <em>any</em> depth. Assertion 7 compares the two, so a brief
    /// filed under a restructured <c>.claude/</c> is named rather than silently
    /// dropped from the sweep.
    /// </summary>
    private static readonly string[] BriefFolders = ["agents", "commands"];

    /// <summary>An inline-code span, the unit both the path and the CI recognisers read.</summary>
    private static readonly Regex CodeSpan = new(
        @"`(?<span>[^`\r\n]+)`",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A recognised ADR citation. The <c>/</c> continuation is the spelling the
    /// briefs already use — <c>ADR-0024/0025</c>, <c>ADR-0077/0078</c> — and
    /// matching only the head would leave the second number of every such pair
    /// unchecked and unreported, which is the silent direction.
    /// </summary>
    private static readonly Regex AdrCitation = new(
        @"ADR-(?<numbers>\d{4}(?:/\d{4})*)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>The path spelling, used once in <c>.claude/commands/next-issue.md</c>.</summary>
    private static readonly Regex AdrPathCitation = new(
        @"adr/(?<number>\d{4})-",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Anything ADR-shaped. Deliberately looser than <see cref="AdrCitation"/>:
    /// what it finds and the strict pattern does not is an unparseable claim,
    /// reported by assertion 2 instead of slipping past into silence.
    ///
    /// <para>
    /// The plural is part of the shape. <c>ADRs 0105-0106</c> matches neither
    /// this pattern nor the strict one without it, so it is neither checked nor
    /// reported — the silent direction — and it is the spelling CLAUDE.md itself
    /// uses.
    /// </para>
    /// </summary>
    private static readonly Regex AdrShaped = new(
        @"\bADRs?[ \-]?\d{1,4}\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>A decision row of the founding document's table.</summary>
    private static readonly Regex DecisionRow = new(
        @"^\|\s*(?<number>\d{1,4})\s*\|",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>A Markdown bullet, which is where one block ends and the next begins.</summary>
    private static readonly Regex BulletStart = new(
        @"^\s*[-*+]\s",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>A workflow job key, as spelled in prose and in the workflow alike.</summary>
    private static readonly Regex JobName = new(
        @"^[a-z][a-z0-9_-]*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The word that turns a block naming <c>ci.yml</c> into an enumeration of
    /// jobs. The plural matters: <c>infra-reviewer.md</c>'s review heuristic
    /// says "a <em>job</em> that passes because a variable was never set", names
    /// no job in an attribute position, and must not be read as an enumeration.
    /// </summary>
    private static readonly Regex JobsWord = new(
        @"\bjobs\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A job described as blocking. Every negation this guard knows is excluded
    /// here and recognised as its opposite by <see cref="NegatedBlockingWord"/>:
    /// a rule that knew only <c>non-</c> read "not blocking" as a positive
    /// blocking claim and suppressed the check that sentence most needed.
    /// </summary>
    private static readonly Regex BlockingWord = new(
        @"(?<!non-)(?<!not )(?<!never )(?<!no longer )\bblocking\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    private static readonly Regex NonBlockingWord = new(
        @"\bnon-blocking\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>The negation vocabulary, spelled out rather than left to a lookbehind alone.</summary>
    private static readonly Regex NegatedBlockingWord = new(
        @"\b(?:not|never|no longer)\s+blocking\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Markdown emphasis, removed before polarity is read. The briefs write
    /// <c>**blocking**</c>, so a negation lands two asterisks away from the word
    /// it negates and no lookbehind reaches it.
    /// </summary>
    private static readonly Regex Emphasis = new(
        @"\*+",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex NeedsWord = new(
        @"\bneeds\b",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>Word characters and hyphens — how a job name is spotted in prose.</summary>
    private static readonly Regex ProseToken = new(
        @"[A-Za-z0-9-]+",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The content of a string literal, which is data rather than mechanism.
    /// Assertion 9 reads code, not the prose the code prints.
    /// </summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\\r\n]|\\.)*""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>Every brief, by repository-relative path, for the per-file arms.</summary>
    public static TheoryData<string> Briefs()
    {
        TheoryData<string> data = [];
        foreach (string brief in BriefFiles())
        {
            data.Add(brief);
        }

        return data;
    }

    /// <summary>
    /// <b>Assertion 1 — every cited decision exists.</b>
    ///
    /// <para>
    /// Per file, so the failure names the brief a reader has to open. The
    /// register is the union of the ADR files and the founding document's
    /// decision rows; a file-only register would call three correct citations
    /// errors.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Briefs))]
    public void Every_decision_a_brief_cites_exists(string brief)
    {
        HashSet<int> register = AdrRegister();

        string[] unresolved = AdrClaims(brief)
            .Where(claim => !register.Contains(claim.Number))
            .Select(claim => $"{claim.File}:{claim.Line} '{claim.Token}'")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        unresolved.ShouldBeEmpty(
            $"{brief} cites decisions that do not exist: {string.Join(", ", unresolved)}. The register is "
            + $"the {AdrDirectory}/NNNN-*.md files UNION the decision rows of {FoundingDecisions} — "
            + "decisions below 0028 have no file of their own, they are rows in the founding document, so "
            + "do not 'fix' a correct citation by creating a duplicate file. A subagent is given this brief "
            + "and nothing else (ADR-0144); a citation whose source it cannot check is one it must be able "
            + "to rely on.");
    }

    /// <summary>
    /// <b>Assertion 2 — every ADR-shaped token is a recognised citation.</b>
    ///
    /// <para>
    /// The companion to assertion 1, and the one that keeps it honest. A
    /// citation written <c>ADR-141</c> or <c>ADR 141</c> does not match the
    /// strict pattern, so assertion 1 never sees it and passes — the silent
    /// direction. This arm makes the near-miss loud instead.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_ADR_shaped_token_in_the_briefs_is_a_recognised_citation()
    {
        string[] unreadable = BriefFiles()
            .SelectMany(UnreadableAdrTokens)
            .Order(StringComparer.Ordinal)
            .ToArray();

        unreadable.ShouldBeEmpty(
            $"these tokens look like ADR citations but are not written in a spelling this guard reads: "
            + $"{string.Join(", ", unreadable)}. The recognised spellings are ADR-NNNN (four digits, "
            + "optionally continued as ADR-NNNN/NNNN) and adr/NNNN-slug. A token outside them is not "
            + "checked against the register at all, and an unchecked citation looks exactly like a correct "
            + "one from a green build.");
    }

    /// <summary>
    /// <b>Assertion 3 — every anchored path a brief quotes resolves.</b>
    ///
    /// <para>
    /// Per file. A span is a claim only if its first segment is a real
    /// top-level entry, which disposes of routes, slash-commands and folder
    /// conventions without an exemption list. A span carrying a glob
    /// metacharacter must match at least one entry.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Briefs))]
    public void Every_repository_path_a_brief_quotes_resolves(string brief)
    {
        DirectoryInfo root = RepositoryRoot();

        string[] unresolved = PathClaims(brief)
            .Where(claim => !Resolves(root, claim.Span))
            .Select(claim => $"{claim.File}:{claim.Line} `{claim.Span}`")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        unresolved.ShouldBeEmpty(
            $"{brief} quotes repository paths that do not exist: {string.Join(", ", unresolved)}. Each is "
            + "anchored at a real top-level entry, so it reads as a claim about this repository rather "
            + "than as illustrative prose. If the brief means a class of file, write the glob it already "
            + "means — `apps/*/src/app/auth.ts`, `specs/*/spec.md` — and it is checked as matching at "
            + "least one entry. If it means one file, name the file that exists. A subagent sent to a path "
            + "that is not there has no way to tell a typo from a thing it has not found yet.");
    }

    /// <summary>
    /// <b>Assertion 4 — an enumerated job set equals the workflow's jobs.</b>
    ///
    /// <para>
    /// Both directions, so a job added to CI that no brief learned about is as
    /// red as a job a brief invents.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_brief_that_enumerates_the_CI_jobs_names_exactly_the_jobs_that_exist()
    {
        string[] actual = [.. WorkflowJobs().Keys.Order(StringComparer.Ordinal)];

        string[] disagreeing = EnumeratingBlocks()
            .Where(block => !EnumeratedJobs(block).SequenceEqual(actual, StringComparer.Ordinal))
            .Select(block => $"{block.File}:{block.Line} names [{string.Join(", ", EnumeratedJobs(block))}]")
            .Order(StringComparer.Ordinal)
            .ToArray();

        disagreeing.ShouldBeEmpty(
            $"these brief blocks enumerate the {Workflow} jobs and disagree with it: "
            + $"{string.Join("; ", disagreeing)}. The workflow defines [{string.Join(", ", actual)}]. A job "
            + "the brief invents sends a subagent looking for a check that does not run; a job the brief "
            + "never learned about is a check nobody was told blocks their merge.");
    }

    /// <summary>
    /// <b>Assertion 5 — a job's claimed attributes agree with the workflow.</b>
    ///
    /// <para>
    /// The arm that catches the error that actually happened. Bound to a job
    /// named in an attribute position, which is what lets a hypothetical and a
    /// correct negative claim about the same key both stay green. Read from
    /// every block, not only from the ones that enumerate: three live sentences
    /// describe the <c>e2e</c> job's polarity outside any enumeration.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_CI_job_attribute_a_brief_claims_agrees_with_the_workflow()
    {
        Dictionary<string, WorkflowJob> jobs = WorkflowJobs();

        string[] wrong = BriefBlocks()
            .SelectMany(JobClaims)
            .Where(claim => jobs.ContainsKey(claim.Job))
            .SelectMany(claim => Disagreements(claim, jobs[claim.Job]))
            .Order(StringComparer.Ordinal)
            .ToArray();

        wrong.ShouldBeEmpty(
            $"these briefs describe a {Workflow} job in a way the workflow contradicts: "
            + $"{string.Join("; ", wrong)}. A brief that says a blocking job is cheap to break teaches the "
            + "subagent reading it to shrug at a red check that actually stops the merge — and #2055 fixed "
            + "exactly this sentence in one of the two files carrying it and left the other, which is the "
            + "argument for checking rather than remembering.");
    }

    /// <summary>
    /// <b>Assertion 5a — a negated "blocking" is read as the claim it is.</b>
    ///
    /// <para>
    /// Assertion 5's precedence rule — an explicit "blocking" wins over a
    /// <c>continue-on-error</c> beside it — <em>is</em> a polarity read, and a
    /// polarity read that knows only the prefix <c>non-</c> is wrong the first
    /// time someone writes "not blocking" about a job that blocks the merge.
    /// That sentence is false in exactly the way the one #2055 fixed was, and
    /// under the prefix-only rule the word "blocking" inside it satisfied the
    /// precedence and suppressed the very check it needed.
    /// </para>
    ///
    /// <para>
    /// Asserted against a written clause rather than against the corpus,
    /// because the corpus is — correctly — free of these sentences. The job it
    /// is read against is the real <c>integration</c>: needed by nothing,
    /// carrying no <c>continue-on-error</c>, so it blocks the merge.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("(needs backend; not blocking — a flake there is cheap, `continue-on-error`)")]
    [InlineData("(needs backend; **not blocking**)")]
    [InlineData("(needs backend; never blocking)")]
    [InlineData("(needs backend; no longer blocking)")]
    [InlineData("(needs backend; non-blocking)")]
    [InlineData("(needs backend; `continue-on-error`)")]
    public void A_clause_denying_that_a_merge_blocking_job_blocks_disagrees_with_the_workflow(string clause)
    {
        WorkflowJob integration = new("integration", ["backend"], false);
        JobClaim claim = new(new Block("written-here", 1, clause), integration.Name, clause);

        Disagreements(claim, integration).ShouldNotBeEmpty(
            $"'{clause}' says the job does not block the merge, and {Workflow} gives it no "
            + "continue-on-error key. A polarity rule that knows only the prefix 'non-' reads the word "
            + "'blocking' inside 'not blocking' as a positive claim, lets it win the precedence, and says "
            + "nothing at all about a sentence that is false the way #2055's was.");
    }

    /// <summary>
    /// <b>Assertion 5b — the negation vocabulary did not eat the positive claim.</b>
    ///
    /// <para>
    /// The companion to 5a, and the reason it is not enough to widen the
    /// negation pattern and stop. Both clauses are live sentences of the
    /// corpus, both are true, and both must stay green — the second is the one
    /// that mentions <c>continue-on-error</c> in order to deny it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("(needs backend; **blocking** — there is no `continue-on-error` anywhere in the file)")]
    [InlineData("the blocking CI `e2e` job verifies behaviour on a fresh stack.")]
    public void A_clause_saying_a_merge_blocking_job_blocks_agrees_with_the_workflow(string clause)
    {
        WorkflowJob integration = new("integration", ["backend"], false);
        JobClaim claim = new(new Block("written-here", 1, clause), integration.Name, clause);

        Disagreements(claim, integration).ShouldBeEmpty(
            $"'{clause}' says the job blocks the merge, which is what {Workflow} does. A negation "
            + "vocabulary wide enough to read 'not blocking' must not turn an ordinary 'blocking' into "
            + "no claim, or assertion 5 stops reading the sentences it was written for.");
    }

    /// <summary>
    /// <b>Assertion 6 — a block that enumerates jobs can be read.</b>
    ///
    /// <para>
    /// A block naming the workflow and saying "jobs" from which no job name
    /// parses is not a block without claims; it is a block whose claims left
    /// the guard. Loud, so the parser gets taught the shape rather than the
    /// claims quietly going unchecked.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_brief_block_that_enumerates_CI_jobs_can_be_parsed()
    {
        string[] unreadable = EnumeratingBlocks()
            .Where(block => EnumeratedJobs(block).Length == 0)
            .Select(block => $"{block.File}:{block.Line}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        unreadable.ShouldBeEmpty(
            $"these brief blocks name {Workflow} and say 'jobs', but no job name parses out of them: "
            + $"{string.Join(", ", unreadable)}. A job name is read as an inline-code span outside any "
            + "parentheses, because the parenthesised text after a job is its attribute clause. The block "
            + "is not necessarily wrong — the parser has to be taught its shape, because a claim that does "
            + "not parse is a claim that is never checked.");
    }

    /// <summary>
    /// <b>Assertion 7 — every brief present is a brief scanned, per file.</b>
    ///
    /// <para>
    /// The first version of the guard this one is modelled on checked that a
    /// directory existed, and would have passed with the directory emptied. So
    /// this is asserted per file and against an independently derived list:
    /// <see cref="DiscoveredBriefFiles"/> finds an <c>agents</c> or
    /// <c>commands</c> directory at any depth under <c>.claude/</c>, while the
    /// sweep reads the two directly beneath it. A restructure that files briefs
    /// one level down is named here rather than shrinking the corpus in
    /// silence.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_brief_in_the_repository_is_scanned_by_this_guard()
    {
        HashSet<string> swept = [.. BriefFiles()];

        string[] unscanned = DiscoveredBriefFiles()
            .Where(brief => !swept.Contains(brief))
            .Order(StringComparer.Ordinal)
            .ToArray();

        unscanned.ShouldBeEmpty(
            "these brief files are present but never read by this guard: "
            + $"{string.Join(", ", unscanned)}. The sweep reads {BriefRoot}/"
            + $"{string.Join($" and {BriefRoot}/", BriefFolders)}; a brief filed anywhere else contributes "
            + "no claim, every arm above passes without looking at it, and that is indistinguishable from "
            + "compliance. Teach the sweep the new layout rather than letting the guard go quiet.");
    }

    /// <summary>
    /// <b>Assertion 8 — the recognisers are still recognising something.</b>
    ///
    /// <para>
    /// The companion to assertion 7: every file can be read and the claim count
    /// still collapse to nothing if a recogniser stops matching. Every arm above
    /// would then be green because it looked at nothing.
    /// </para>
    ///
    /// <para>
    /// The total alone was not a floor on the recognisers, only on the largest
    /// of them. With the path recogniser broken the corpus loses all 37 path
    /// claims and assertion 3 passes vacuously for every brief, while the
    /// remaining citations still clear any aggregate threshold worth setting.
    /// So the total is asserted <em>after</em> each class clears its own.
    /// </para>
    /// </summary>
    [Fact]
    public void The_briefs_yield_a_claim_count_large_enough_to_hold_a_violation()
    {
        int claims = DecisionClaimCount() + PathClaimCount() + JobClaimCount();

        claims.ShouldBeGreaterThanOrEqualTo(
            SmallestPlausibleClaimCount,
            $"the guard recognised {claims} claims across the briefs, fewer than the "
            + $"{SmallestPlausibleClaimCount} a real corpus carries. The arms above are then green because "
            + "they read almost nothing, not because the briefs are correct. A recogniser stopped matching "
            + "— fix it before trusting any arm above.");
    }

    /// <summary>
    /// <b>Assertion 8a — the decision recogniser is still recognising.</b>
    /// </summary>
    [Fact]
    public void The_briefs_yield_enough_decision_citations_to_hold_a_violation()
    {
        int claims = DecisionClaimCount();

        claims.ShouldBeGreaterThanOrEqualTo(
            SmallestPlausibleDecisionCount,
            $"the guard recognised {claims} ADR citation claims across the briefs, fewer than the "
            + $"{SmallestPlausibleDecisionCount} a real corpus carries. Assertions 1 and 2 are then green "
            + "on almost nothing. The citation recogniser stopped matching — fix it rather than the floor.");
    }

    /// <summary>
    /// <b>Assertion 8b — the path recogniser is still recognising.</b>
    /// </summary>
    [Fact]
    public void The_briefs_yield_enough_anchored_paths_to_hold_a_violation()
    {
        int claims = PathClaimCount();

        claims.ShouldBeGreaterThanOrEqualTo(
            SmallestPlausiblePathCount,
            $"the guard recognised {claims} anchored path spans across the briefs, fewer than the "
            + $"{SmallestPlausiblePathCount} a real corpus carries. Assertion 3 is then green for every "
            + "brief because it read no path at all — which is exactly what a broken anchor rule looks "
            + "like from a passing build. Fix the recogniser rather than the floor.");
    }

    /// <summary>
    /// <b>Assertion 8c — the CI recogniser is still recognising.</b>
    /// </summary>
    [Fact]
    public void The_briefs_yield_enough_CI_job_claims_to_hold_a_violation()
    {
        int claims = JobClaimCount();

        claims.ShouldBeGreaterThanOrEqualTo(
            SmallestPlausibleJobCount,
            $"the guard recognised {claims} CI job claims across the briefs, fewer than the "
            + $"{SmallestPlausibleJobCount} a real corpus carries. Assertions 4 and 5 are then green "
            + "because no block parsed as a claim about a job. Fix the recogniser rather than the floor.");
    }

    private static int DecisionClaimCount() => BriefFiles().Sum(brief => AdrClaims(brief).Length);

    private static int PathClaimCount() => BriefFiles().Sum(brief => PathClaims(brief).Length);

    private static int JobClaimCount() => BriefBlocks().Sum(block => JobClaims(block).Length);

    /// <summary>
    /// <b>Assertion 9 — the gate has no soft edge.</b>
    ///
    /// <para>
    /// A departure from these rules is a blocked outcome a human accepts in
    /// writing, not a line added here; ADR-0144 names reaching green by
    /// weakening a gate as a thing the lane may not do. It reads code, not
    /// prose: comment lines, attribute lines and the content of string literals
    /// are outside the scan, because prose about this rule necessarily uses this
    /// rule's vocabulary.
    /// </para>
    ///
    /// <para>
    /// It polices a vocabulary, not a mechanism — an author who names the same
    /// thing something else walks past it. That is a fair price for making the
    /// obvious move loud, which is the move an agent under pressure to reach
    /// green makes; it is not a proof that no soft edge can be added.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("allowlist")]
    [InlineData("whitelist")]
    [InlineData("skiplist")]
    [InlineData("baseline")]
    [InlineData("exempt")]
    [InlineData("waiver")]
    [InlineData("waived")]
    [InlineData("knownViolation")]
    [InlineData("suppress")]
    [InlineData("#pragma warning disable")]
    public void The_guard_offers_no_way_to_excuse_a_brief(string mechanism)
    {
        string[] offenders = ExecutableLines(ReadRepositoryFile(GuardSource))
            .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the guard's own code names '{mechanism}': {string.Join(" | ", offenders)}. That reads as a "
            + "way to excuse a brief from the rule, and a rule with a soft edge is a review convention "
            + "wearing a build failure's clothes. A departure is a blocked outcome a human accepts in "
            + "writing; ADR-0144 forbids the lane from reaching green by weakening a gate.");
    }

    /// <summary>
    /// The decisions that exist: the four-digit prefixes of the ADR files,
    /// union the decision-row numbers of the founding document. The union is
    /// the whole point — see the class remarks.
    /// </summary>
    private static HashSet<int> AdrRegister()
    {
        HashSet<int> register = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot().FullName, AdrDirectory), "*.md"))
        {
            string name = Path.GetFileName(file);
            if (name.Length >= 4
                && int.TryParse(name[..4], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                register.Add(number);
            }
        }

        foreach (Match row in DecisionRow.Matches(ReadRepositoryFile(FoundingDecisions)))
        {
            register.Add(int.Parse(row.Groups["number"].Value, CultureInfo.InvariantCulture));
        }

        register.Count.ShouldBeGreaterThan(
            100,
            $"the decision register read {register.Count} decisions from {AdrDirectory} and "
            + $"{FoundingDecisions}. Assertion 1 would then reject correct citations. Fix the register "
            + "before touching a brief.");

        return register;
    }

    /// <summary>Every ADR citation in one brief, with the line that carries it.</summary>
    private static AdrClaim[] AdrClaims(string brief) =>
        Lines(brief)
            .SelectMany(line => CitationsOn(brief, line.Number, line.Text))
            .ToArray();

    private static IEnumerable<AdrClaim> CitationsOn(string brief, int number, string text)
    {
        foreach (Match citation in AdrCitation.Matches(text))
        {
            foreach (string digits in citation.Groups["numbers"].Value.Split('/'))
            {
                yield return new AdrClaim(
                    brief,
                    number,
                    citation.Value,
                    int.Parse(digits, CultureInfo.InvariantCulture));
            }
        }

        foreach (Match citation in AdrPathCitation.Matches(text))
        {
            yield return new AdrClaim(
                brief,
                number,
                citation.Value,
                int.Parse(citation.Groups["number"].Value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// ADR-shaped tokens in one brief that no recognised spelling covers. A
    /// loose match that begins inside a strict one is the same token read
    /// properly, not a near-miss.
    /// </summary>
    private static IEnumerable<string> UnreadableAdrTokens(string brief) =>
        Lines(brief).SelectMany(line => NearMissesOn(brief, line.Number, line.Text));

    private static IEnumerable<string> NearMissesOn(string brief, int number, string text)
    {
        List<(int Start, int End)> recognised = [.. AdrCitation.Matches(text)
            .Select(citation => (citation.Index, citation.Index + citation.Length))];

        foreach (Match shaped in AdrShaped.Matches(text))
        {
            bool covered = recognised.Any(range =>
                shaped.Index >= range.Start && shaped.Index < range.End);

            if (!covered)
            {
                yield return $"{brief}:{number} '{shaped.Value}'";
            }
        }
    }

    /// <summary>
    /// Every anchored path span in one brief. The anchor set is the entry names
    /// of the repository root, enumerated at run time — never written down, so
    /// a restructure that renames a top-level directory changes the recogniser
    /// with it and a brief still naming the old one goes red, which is correct.
    /// </summary>
    private static PathClaim[] PathClaims(string brief)
    {
        HashSet<string> anchors = RootEntries();

        return Lines(brief)
            .SelectMany(line => CodeSpan.Matches(line.Text)
                .Select(span => new PathClaim(brief, line.Number, span.Groups["span"].Value)))
            .Where(claim => IsAnchored(anchors, claim.Span))
            .ToArray();
    }

    /// <summary>
    /// Whether a span reads as a claim about this repository. The leading
    /// <c>/</c> is trimmed first: the briefs write the repo-root Playwright
    /// directory as <c>/e2e</c>, and leaving the slash on made those three spans
    /// — and a wrong path written the same way — invisible. See the class
    /// remarks for the census that shows the trim adds no false positive.
    /// </summary>
    private static bool IsAnchored(HashSet<string> anchors, string span) =>
        span.Contains('/') && anchors.Contains(span.TrimStart('/').Split('/')[0]);

    private static HashSet<string> RootEntries()
    {
        HashSet<string> entries =
        [
            .. Directory.EnumerateFileSystemEntries(RepositoryRoot().FullName)
                .Select(entry => Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar)))
        ];

        entries.ShouldContain(
            "src",
            "the repository root was enumerated and does not contain 'src', so the anchor set is wrong and "
            + "no path span is recognised as a claim. Every path arm would pass on nothing.");

        return entries;
    }

    /// <summary>
    /// Whether a span names something that is there: a file, a directory, or —
    /// if it carries a glob metacharacter — at least one matching entry.
    /// </summary>
    private static bool Resolves(DirectoryInfo root, string span)
    {
        string[] segments = span.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 && Matches(root.FullName, segments, 0);
    }

    private static bool Matches(string current, string[] segments, int index)
    {
        if (index == segments.Length)
        {
            return true;
        }

        string segment = segments[index];
        bool last = index == segments.Length - 1;

        if (!HasGlob(segment))
        {
            string next = Path.Combine(current, segment);
            return last
                ? File.Exists(next) || Directory.Exists(next)
                : Directory.Exists(next) && Matches(next, segments, index + 1);
        }

        return Directory.Exists(current)
            && Directory.EnumerateFileSystemEntries(current, segment)
                .Any(entry => last || (Directory.Exists(entry) && Matches(entry, segments, index + 1)));
    }

    private static bool HasGlob(string segment) =>
        segment.Contains('*') || segment.Contains('?');

    /// <summary>
    /// The blocks that name the workflow and enumerate its jobs. Everything
    /// else that mentions <c>ci.yml</c> — a contention-file list, a review
    /// heuristic about "a job that passes because a variable was never set" —
    /// makes no enumerable claim and is not read as one.
    /// </summary>
    private static Block[] EnumeratingBlocks() =>
        [.. BriefBlocks().Where(Enumerates)];

    /// <summary>Every block of every brief — the unit a claim is read in.</summary>
    private static Block[] BriefBlocks() =>
        [.. BriefFiles().SelectMany(brief => Blocks(brief, ReadRepositoryFile(brief)))];

    private static bool Enumerates(Block block) =>
        block.Text.Contains("ci.yml", StringComparison.Ordinal) && JobsWord.IsMatch(block.Text);

    /// <summary>
    /// The job names one block enumerates, in ordinal order: the inline-code
    /// spans at parenthesis depth zero, after the word "jobs", that are spelled
    /// like a workflow job key. Depth is what separates a job from its own
    /// attributes — <c>`integration` (needs backend, `continue-on-error`)</c>
    /// enumerates one job, not two.
    /// </summary>
    private static string[] EnumeratedJobs(Block block) =>
        [.. SpansByDepth(Enumeration(block))
            .Where(span => span.Depth == 0 && JobName.IsMatch(span.Text))
            .Select(span => span.Text)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The part of a block that enumerates: from the word "jobs" to the end of
    /// the <b>sentence</b> carrying it, not the end of the block.
    ///
    /// <para>
    /// The block is the wrong bound. <c>infra-reviewer.md:18</c> continues past
    /// its enumeration into "Actions pinned to commit SHAs" and "the existing
    /// NuGet/pnpm caches"; running to the end of the block makes every later
    /// lower-case inline span an enumerated job, so backticking
    /// <c>nuget</c>/<c>pnpm</c> — ordinary style here — reddens assertion 4 on a
    /// sentence that is entirely true. A guard that fails on correct editing is
    /// one that gets deleted.
    /// </para>
    /// </summary>
    private static string Enumeration(Block block)
    {
        Match jobs = JobsWord.Match(block.Text);
        if (!jobs.Success)
        {
            return block.Text;
        }

        int end = 0;
        foreach (string sentence in Sentences(block.Text))
        {
            end += sentence.Length;
            if (end > jobs.Index)
            {
                return block.Text[jobs.Index..end];
            }
        }

        return block.Text[jobs.Index..];
    }

    /// <summary>
    /// One claim per job a block describes. A block that enumerates yields the
    /// enumeration's claims; any other block is read sentence by sentence, so
    /// "the blocking CI <c>e2e</c> job" is a claim wherever it is written.
    /// </summary>
    private static JobClaim[] JobClaims(Block block) =>
        Enumerates(block) ? EnumerationClaims(block) : AttributeSentenceClaims(block);

    /// <summary>
    /// One claim per enumerated job: the job, and the text between its span and
    /// the next job's — its attribute clause.
    /// </summary>
    private static JobClaim[] EnumerationClaims(Block block)
    {
        string enumeration = Enumeration(block);
        Span[] spans = [.. SpansByDepth(enumeration).Where(span => span.Depth == 0 && JobName.IsMatch(span.Text))];

        return [.. spans.Select((span, position) => new JobClaim(
            block,
            span.Text,
            enumeration[span.End..(position + 1 < spans.Length ? spans[position + 1].Start : enumeration.Length)]))];
    }

    /// <summary>
    /// The attribute claims of a block that does not enumerate: one per sentence
    /// naming exactly one workflow job key in a depth-zero inline-code span, the
    /// whole sentence being the clause because the attribute may sit on either
    /// side of the job — "the blocking CI <c>e2e</c> job" puts it before. A
    /// sentence naming two jobs is not attributed to either.
    /// </summary>
    private static JobClaim[] AttributeSentenceClaims(Block block)
    {
        HashSet<string> jobs = [.. WorkflowJobs().Keys];

        return [.. Sentences(block.Text)
            .Select(sentence => (Sentence: sentence, Jobs: NamedJobs(sentence, jobs)))
            .Where(sentence => sentence.Jobs.Length == 1)
            .Select(sentence => new JobClaim(block, sentence.Jobs[0], sentence.Sentence))];
    }

    private static string[] NamedJobs(string sentence, HashSet<string> jobs) =>
        [.. SpansByDepth(sentence)
            .Where(span => span.Depth == 0 && jobs.Contains(span.Text))
            .Select(span => span.Text)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A text split into sentences, contiguously and in order. A terminator
    /// inside an inline-code span does not end a sentence — <c>global.json</c>
    /// and <c>scripts/wait-for-e2e-stack.sh</c> both carry one, and both sit
    /// inside the enumeration they would otherwise cut in half.
    /// </summary>
    private static IEnumerable<string> Sentences(string text)
    {
        const string terminators = ".!?";
        int start = 0;
        bool inSpan = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '`')
            {
                inSpan = !inSpan;
            }
            else if (!inSpan
                && terminators.Contains(character, StringComparison.Ordinal)
                && (index + 1 == text.Length || char.IsWhiteSpace(text[index + 1])))
            {
                yield return text[start..(index + 1)];
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    /// <summary>
    /// Where a job's attribute clause and the workflow disagree.
    ///
    /// <para>
    /// An explicit "blocking" in the clause wins over a <c>continue-on-error</c>
    /// mentioned inside it. That precedence is what keeps
    /// <c>infra-engineer.md</c>'s correct sentence — "blocking — there is no
    /// <c>continue-on-error</c> anywhere in the file" — green, without reading
    /// negation out of prose.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Disagreements(JobClaim claim, WorkflowJob job)
    {
        string clause = Emphasis.Replace(claim.Clause, string.Empty);
        string where = $"{claim.Block.File}:{claim.Block.Line} '{claim.Job}'";

        if (!ClaimsBlocking(clause) && ClaimsNonBlocking(clause) && !job.ContinueOnError)
        {
            yield return $"{where} is described as non-blocking, but {Workflow} gives it no "
                + "continue-on-error key, so it blocks the merge and a flake there is not cheap";
        }

        if (ClaimsBlocking(clause) && job.ContinueOnError)
        {
            yield return $"{where} is described as blocking, but {Workflow} marks it continue-on-error";
        }

        if (Enumerates(claim.Block) && NeedsWord.IsMatch(clause))
        {
            string[] claimed = ClaimedNeeds(clause);
            if (!claimed.SequenceEqual(job.Needs, StringComparer.Ordinal))
            {
                yield return $"{where} is described as needing [{string.Join(", ", claimed)}], but "
                    + $"{Workflow} gives it [{string.Join(", ", job.Needs)}]";
            }
        }
    }

    /// <summary>Whether a clause says the job blocks the merge, un-negated.</summary>
    private static bool ClaimsBlocking(string clause) => BlockingWord.IsMatch(clause);

    /// <summary>
    /// Whether a clause says the job does not block: the <c>non-</c> prefix, one
    /// of the negation words in front of "blocking", or a bare
    /// <c>continue-on-error</c> mentioned about it.
    /// </summary>
    private static bool ClaimsNonBlocking(string clause) =>
        NonBlockingWord.IsMatch(clause)
        || NegatedBlockingWord.IsMatch(clause)
        || clause.Contains("continue-on-error", StringComparison.Ordinal);

    /// <summary>
    /// The jobs a clause says another job needs: the workflow job names that
    /// appear as words after "needs", with inline-code spans removed first. The
    /// removal matters — <c>via `scripts/wait-for-e2e-stack.sh`</c> would
    /// otherwise contribute a phantom <c>e2e</c> dependency.
    /// </summary>
    private static string[] ClaimedNeeds(string clause)
    {
        HashSet<string> jobs = [.. WorkflowJobs().Keys];
        string text = CodeSpan.Replace(clause[NeedsWord.Match(clause).Index..], " ");

        return [.. ProseToken.Matches(text)
            .Select(token => token.Value)
            .Where(jobs.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The workflow's jobs, read from the file rather than declared here: the
    /// two-space keys under <c>jobs:</c>, each with the four-space
    /// <c>needs:</c> and <c>continue-on-error:</c> it does or does not carry. A
    /// <c>continue-on-error</c> on a single step sits deeper and is not a job
    /// attribute.
    /// </summary>
    private static Dictionary<string, WorkflowJob> WorkflowJobs()
    {
        Dictionary<string, List<string>> lines = new(StringComparer.Ordinal);
        string? current = null;
        bool inJobs = false;

        foreach (string line in ReadRepositoryFile(Workflow).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("jobs:", StringComparison.Ordinal))
            {
                inJobs = true;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            Match key = Regex.Match(line, @"^  (?<name>[A-Za-z0-9_-]+):\s*$", RegexOptions.None, TimeSpan.FromSeconds(5));
            if (key.Success)
            {
                current = key.Groups["name"].Value;
                lines[current] = [];
            }
            else if (current is not null)
            {
                lines[current].Add(line);
            }
        }

        Dictionary<string, WorkflowJob> jobs = lines.ToDictionary(
            job => job.Key,
            job => new WorkflowJob(job.Key, JobNeeds(job.Value), HasContinueOnError(job.Value)),
            StringComparer.Ordinal);

        jobs.ShouldNotBeEmpty(
            $"no jobs parsed out of {Workflow}, so assertions 4 and 5 compare every brief against an empty "
            + "workflow. The workflow's shape changed — teach this reader the new one.");

        return jobs;
    }

    private static string[] JobNeeds(List<string> body)
    {
        string? needs = body
            .Select(line => Regex.Match(line, @"^    needs:\s*(?<value>.+)$", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Where(match => match.Success)
            .Select(match => match.Groups["value"].Value)
            .FirstOrDefault();

        return needs is null
            ? []
            : [.. needs.Trim('[', ']', ' ')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal)];
    }

    private static bool HasContinueOnError(List<string> body) =>
        body.Any(line => line.StartsWith("    continue-on-error:", StringComparison.Ordinal));

    /// <summary>
    /// One brief split into blocks: a bullet item with its continuation lines,
    /// or a paragraph. A claim is read in the context of its block, which is
    /// what lets two consecutive bullets about the same workflow make different
    /// claims without contaminating each other.
    /// </summary>
    private static Block[] Blocks(string brief, string text)
    {
        List<Block> blocks = [];
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int index = 0; index < lines.Length;)
        {
            if (lines[index].Trim().Length == 0)
            {
                index++;
                continue;
            }

            int start = index;
            List<string> body = [lines[index++]];
            while (index < lines.Length && IsContinuation(lines[index]))
            {
                body.Add(lines[index++]);
            }

            blocks.Add(new Block(brief, start + 1, string.Join(' ', body)));
        }

        return [.. blocks];
    }

    private static bool IsContinuation(string line) =>
        line.Trim().Length > 0
        && !BulletStart.IsMatch(line)
        && !line.StartsWith('#');

    /// <summary>
    /// The inline-code spans of a text with the parenthesis depth each sits at,
    /// and where it starts and ends. Parentheses inside a span do not count —
    /// <c>`gatewayBaseQuery('&lt;context&gt;/&lt;group&gt;')`</c> would
    /// otherwise unbalance everything after it.
    /// </summary>
    private static IEnumerable<Span> SpansByDepth(string text)
    {
        int depth = 0;

        for (int index = 0; index < text.Length;)
        {
            char character = text[index];
            if (character == '`')
            {
                int close = text.IndexOf('`', index + 1);
                if (close < 0)
                {
                    yield break;
                }

                yield return new Span(text[(index + 1)..close], depth, index, close + 1);
                index = close + 1;
                continue;
            }

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth = Math.Max(0, depth - 1);
            }

            index++;
        }
    }

    /// <summary>
    /// The briefs the guard reads, by repository-relative path with forward
    /// slashes, in ordinal order.
    /// </summary>
    private static string[] BriefFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return [.. BriefFolders
            .Select(folder => Path.Combine(root.FullName, BriefRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The briefs that are <em>there</em>, found independently of the sweep: any
    /// <c>*.md</c> under a directory named <c>agents</c> or <c>commands</c> at
    /// any depth beneath <c>.claude/</c>. Assertion 7 compares this with the
    /// sweep.
    /// </summary>
    private static string[] DiscoveredBriefFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return [.. Directory
            .EnumerateDirectories(Path.Combine(root.FullName, BriefRoot), "*", SearchOption.AllDirectories)
            .Where(folder => BriefFolders.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static NumberedLine[] Lines(string relativePath) =>
        [.. ReadRepositoryFile(relativePath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select((text, index) => new NumberedLine(index + 1, text))];

    /// <summary>
    /// Lines that are neither commentary nor attribute metadata, with the
    /// content of every string literal removed — what is left is the code that
    /// could actually carry a mechanism, rather than the prose it prints.
    /// </summary>
    private static IEnumerable<string> ExecutableLines(string source) =>
        source.Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith('['))
            .Select(line => StringLiteral.Replace(line, "\"\""));

    private static string ReadRepositoryFile(string relativePath)
    {
        string[] parts = [RepositoryRoot().FullName, .. relativePath.Split('/')];
        string path = Path.Combine(parts);

        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Reported with <c>/</c> throughout. <see cref="Path.GetRelativePath"/>
    /// returns the platform separator, so a backslash in an expected string is
    /// green on Windows and red on Linux CI — this repository has been bitten by
    /// exactly that.
    /// </summary>
    private static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

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

    private sealed record NumberedLine(int Number, string Text);

    private sealed record Block(string File, int Line, string Text);

    private sealed record Span(string Text, int Depth, int Start, int End);

    private sealed record AdrClaim(string File, int Line, string Token, int Number);

    private sealed record PathClaim(string File, int Line, string Span);

    private sealed record JobClaim(Block Block, string Job, string Clause);

    private sealed record WorkflowJob(string Name, string[] Needs, bool ContinueOnError);
}
