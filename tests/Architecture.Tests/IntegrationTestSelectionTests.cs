using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards which CI job reads an integration test's verdict (issues #2141, #2134).
///
/// <para>
/// The Docker-free step in <c>ci.yml</c> selects <b>by trait</b>, deliberately:
/// the name filter it replaced read <c>~AspireFixtureReportSelectionTests</c>,
/// and the very next Docker-free class did not match it, recreating the omission
/// the step exists to remove (#2064). A trait is the one selector a new class
/// cannot silently fall outside of — but only if every class carries one.
/// </para>
///
/// <para>
/// A class carrying neither <c>[Collection(AspireCollection.Name)]</c> nor a
/// category trait declares nothing, so its verdict is deferred to the
/// thirty-minute Docker job. That is the same omission again, one layer down,
/// and it is silent: the tests pass, in the expensive job, and nobody reads a
/// green run to find out which job produced it.
/// </para>
///
/// <para>
/// The guard checks that a declaration <b>exists</b>; it does not adjudicate
/// which. Two classes lack the collection and still need a live run-mode stack
/// (<c>RunModeVariableResidueSweep</c>, <c>RunModeIngestAttributionTests</c>), so
/// inferring "no collection ⇒ Docker-free" would demand the wrong declaration of
/// them.
/// </para>
///
/// <para>
/// Reads source from disk rather than referencing <c>Integration.Tests</c>: a
/// project reference would drag the Aspire hosting and DCP dependency graph into
/// a project that today runs in seconds with no Docker — defeating a guard whose
/// entire purpose is to move a verdict out of the Docker job.
/// <c>LogTailCoverageTests</c> and <c>GuardBanWiringTests</c> read the tree for
/// the same reason.
/// </para>
/// </summary>
public class IntegrationTestSelectionTests
{
    private const string ScannedTree = "tests/Integration.Tests";

    /// <summary>
    /// #2289. The workflow file the reader below parses <b>as text</b>, not as
    /// YAML — the same choice <c>AgentBriefClaimTests.WorkflowJobs()</c> and
    /// <c>AppHostE2ESwitchTests</c> already made, so this guard does not add a
    /// YAML dependency to a test project that runs in seconds (spec 185, A-3).
    /// </summary>
    private const string Workflow = ".github/workflows/ci.yml";

    /// <summary>
    /// The exact VSTest run-setting that turns "no test matched the filter"
    /// into a non-zero exit (spec 185 §2). It is inert unless it appears after
    /// a standalone <c>--</c> token — everything before that separator is a
    /// <c>dotnet test</c> argument, not a VSTest run-setting override.
    /// </summary>
    private const string TreatNoTestsAsErrorFlag = "RunConfiguration.TreatNoTestsAsError=true";

    private static readonly Regex BlockComment = new(
        @"/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LineComment = new(
        @"//[^\r\n]*",
        RegexOptions.Compiled);

    private static readonly Regex FactOrTheory = new(
        @"^[ \t]*\[(Fact|Theory)\b",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Anchored at the start of a line so an <b>attribute</b> is matched and a
    /// <b>mention</b> is not. <c>RunModeDriverTests</c> names the collection in a
    /// doc-comment and the fixture in reflection code, precisely because its job
    /// is to assert it acquires neither; a scan keying on the identifier anywhere
    /// in the file credits it, and the class escapes.
    /// </summary>
    private static readonly Regex CollectionDeclaration = new(
        @"^[ \t]*\[\s*Collection\(\s*AspireCollection\.Name\s*\)\s*\]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// The same trait-declaration shape as <see cref="CategoryDeclaration"/>,
    /// generalised to <b>capture</b> the value instead of matching a frozen
    /// list of four — #2289's F3 needs to ask "does any class declare this
    /// name", for a name it read out of <c>ci.yml</c>, not out of a literal.
    /// </summary>
    private static readonly Regex AnyCategoryDeclaration = new(
        @"^[ \t]*\[\s*Trait\(\s*""Category""\s*,\s*""(?<name>[A-Za-z0-9_]+)""\s*\)\s*\]",
        RegexOptions.Multiline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Pulls the quoted argument to <c>--filter</c> out of a joined
    /// <c>dotnet test</c> command. Anchored on the literal token so a filter
    /// value that itself contains the word <c>filter</c> cannot confuse it.
    /// </summary>
    private static readonly Regex FilterArgument = new(
        @"--filter\s+""(?<value>[^""]*)""",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Every <c>Category</c> term inside a <c>--filter</c> value, deliberately
    /// blind to <c>=</c> versus <c>!=</c> (plan.md §3.1): both an inclusion and
    /// an exclusion are a claim that the named category exists on the test
    /// side, so <c>Category!=X&amp;Category!=Y</c> yields <c>X</c> and
    /// <c>Y</c> as two separate names, not one.
    /// </summary>
    private static readonly Regex CategoryTerm = new(
        @"Category\s*!?=\s*(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Phase 6 review, #2289. A double-quoted span, whole and including its
    /// quotes — used by <see cref="TestInvocation.FailsOnNoTests"/> to blank
    /// out a <c>--filter</c> value before tokenizing on spaces. Without this,
    /// a filter value that itself contains the text <c>-- RunConfiguration.
    /// TreatNoTestsAsError=true</c> tokenizes into a standalone <c>--</c>
    /// followed by the flag token — a false positive: the guard would credit
    /// a flag that never actually reaches VSTest, because it never left the
    /// quotes <c>dotnet test</c> itself saw as one argument.
    /// </summary>
    private static readonly Regex QuotedSpan = new(
        @"""[^""]*""",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// #2289, T009. Constrained to the categories <c>ci.yml</c> actually
    /// filters on today — <b>derived</b> from the workflow itself via
    /// <see cref="DerivedCategoryNames"/> rather than typed as a frozen
    /// four-name literal. The literal this replaced was exactly the failure
    /// mode spec 185 §1.4 names: rename a category on both sides and the
    /// literal still matches, crediting a declaration that selects zero
    /// tests in <c>ci.yml</c>. Sourced from parsed text rather than typed as
    /// a literal, so each name is <see cref="Regex.Escape"/>d before joining —
    /// a category name is <c>[A-Za-z0-9_]+</c> today (<see
    /// cref="AnyCategoryDeclaration"/>) and carries nothing a regex would
    /// treat specially, but escaping costs nothing and does not depend on
    /// that staying true. Matching <c>[Trait("Category"</c> without reading
    /// the value would still credit any spelling — <see
    /// cref="A_misspelled_category_value_is_not_a_declaration"/> is what
    /// catches that.
    ///
    /// <para>
    /// Must stay declared below <see cref="FilterArgument"/> and
    /// <see cref="CategoryTerm"/>: this field's initializer calls <see
    /// cref="DerivedCategoryNames"/>, which reads both of those fields, and
    /// C# runs static field initializers in textual order — moving this
    /// field above either would read it as still <see langword="null"/>.
    /// </para>
    /// </summary>
    private static readonly Regex CategoryDeclaration = new(
        $"""^[ \t]*\[\s*Trait\(\s*"Category"\s*,\s*"(?:{string.Join('|', DerivedCategoryNames().Select(Regex.Escape))})"\s*\)\s*\]""",
        RegexOptions.Multiline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// One <c>dotnet test</c> invocation read out of <c>ci.yml</c>, continuation
    /// lines joined into a single <see cref="Command"/> (plan.md §3.1). <see
    /// cref="Line"/> is 1-based and points at the line the invocation
    /// <b>opens</b> on — the line a reader jumps to, not a line inside the
    /// continuation.
    /// </summary>
    private sealed record TestInvocation(int Line, string Command)
    {
        /// <summary>
        /// The quoted argument to <c>--filter</c>, or <c>null</c> when this
        /// invocation carries none. An invocation with no filter runs the
        /// whole matched project and cannot select zero tests the way a
        /// filtered one can — it is never asked for <see cref="FailsOnNoTests"/>.
        /// </summary>
        public string? Filter
        {
            get
            {
                Match match = FilterArgument.Match(Command);
                return match.Success ? match.Groups["value"].Value : null;
            }
        }

        public bool IsFiltered => Filter is not null;

        /// <summary>
        /// True only when <see cref="TreatNoTestsAsErrorFlag"/> appears as its
        /// own token strictly after a standalone <c>--</c> token in <see
        /// cref="Command"/>, with every double-quoted span (a <c>--filter</c>
        /// value, most often) blanked out via <see cref="QuotedSpan"/> before
        /// tokenizing. Two traps this deliberately refuses to credit (plan.md
        /// §3.3, spec 185 §2.1, phase 6 review): the flag's text sitting
        /// inside a quoted <c>--filter</c> value — including a value that
        /// itself contains what looks like a standalone <c>--</c> followed by
        /// the flag, which tokenizes into exactly that shape if the quotes
        /// are not stripped first — and the flag appearing <i>before</i> the
        /// separator (it is then an unrecognised <c>dotnet test</c> argument,
        /// never reaching VSTest as a run-setting override).
        /// </summary>
        public bool FailsOnNoTests
        {
            get
            {
                string[] tokens = QuotedSpan.Replace(Command, "QUOTEDVALUE")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int separator = Array.IndexOf(tokens, "--");

                return separator >= 0
                    && tokens.Skip(separator + 1).Any(token => token == TreatNoTestsAsErrorFlag);
            }
        }
    }

    /// <summary>
    /// Every <c>dotnet test</c> invocation in <c>ci.yml</c>, continuation
    /// lines joined. A line opens an invocation when its trimmed text starts
    /// with <c>dotnet test</c>; while the line just consumed ends with a
    /// trailing <c>\</c>, the next line is appended (trimmed) to the same
    /// invocation; otherwise the invocation closes (plan.md §3.1).
    /// </summary>
    private static TestInvocation[] WorkflowInvocations() => ParseInvocations(WorkflowLines());

    /// <summary>
    /// The parsing half of <see cref="WorkflowInvocations"/>, taking lines
    /// directly rather than reading <c>ci.yml</c> — what F4's synthetic-input
    /// facts call, in exactly the way the existing eight facts call <see
    /// cref="Describe"/> with literal text rather than the real tree.
    /// </summary>
    private static TestInvocation[] ParseInvocations(string[] lines)
    {
        List<TestInvocation> invocations = [];
        int index = 0;

        while (index < lines.Length)
        {
            string trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("dotnet test", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            int line = index + 1;
            List<string> segments = [TrimContinuation(trimmed)];

            // Bounds-checked (phase 6 review, #2289): a workflow whose very last
            // line is this invocation's opener and ends in `\` would otherwise
            // index one past the end here — an IndexOutOfRangeException with no
            // diagnostic, instead of just closing the invocation on what is, by
            // construction, an unterminated continuation.
            while (index < lines.Length - 1 && EndsInContinuation(lines[index]))
            {
                index++;
                segments.Add(TrimContinuation(lines[index].Trim()));
            }

            invocations.Add(new TestInvocation(line, string.Join(' ', segments)));
            index++;
        }

        return [.. invocations];
    }

    private static bool EndsInContinuation(string line) => line.TrimEnd().EndsWith('\\');

    private static string TrimContinuation(string line)
    {
        string trimmed = line.TrimEnd();
        return trimmed.EndsWith('\\') ? trimmed[..^1].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Every category name a <c>--filter</c> value mentions. Blind to
    /// <c>=</c> versus <c>!=</c> by design (see <see cref="CategoryTerm"/>).
    /// </summary>
    private static string[] Categories(string filter) =>
        [.. CategoryTerm.Matches(filter).Select(match => match.Groups["name"].Value)];

    /// <summary>
    /// #2289, T009. Every category name <c>ci.yml</c>'s filters mention
    /// today — the single source both <see cref="CategoryDeclaration"/> and
    /// F3 (<see
    /// cref="Every_category_the_workflow_filters_on_is_declared_by_a_test_class"/>)
    /// are built from, rather than each computing the set its own way, so
    /// the two cannot drift apart (phase 6 review, #2289). Sorted so the
    /// built regex (and any message built from it) is deterministic across
    /// runs. Runs during static field initialization for <see
    /// cref="CategoryDeclaration"/>'s field initializer, which is why it is
    /// declared after <see cref="FilterArgument"/> and <see
    /// cref="CategoryTerm"/> in this file: C# runs static field initializers
    /// in textual order, and this method reads both of those fields through
    /// <see cref="WorkflowInvocations"/> and <see cref="Categories"/>.
    /// </summary>
    private static string[] DerivedCategoryNames() => [.. WorkflowInvocations()
        .Where(invocation => invocation.IsFiltered)
        .SelectMany(invocation => Categories(invocation.Filter!))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)];

    private static string[] WorkflowLines() =>
        ReadWorkflowText().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string ReadWorkflowText()
    {
        DirectoryInfo root = RepositoryRoot();
        string path = Path.Combine([root.FullName, .. Workflow.Split('/')]);

        File.Exists(path).ShouldBeTrue(
            $"expected {Workflow} at {path} — if it moved, update this guard rather than deleting it.");

        return File.ReadAllText(path);
    }

    [Fact]
    public void Every_integration_test_class_declares_where_it_runs()
    {
        TestFile[] scanned = ScannedFiles();

        scanned.Count(file => file.Facts > 0).ShouldBeGreaterThan(
            0,
            $"no test methods were found under {ScannedTree} — the scan is broken, not the code. "
            + "A source-scanning guard that matches nothing passes, and a passing guard that checks "
            + "nothing is indistinguishable from one that holds.");

        TestFile[] undeclared = scanned.Where(file => file.Undeclared).ToArray();

        undeclared.ShouldBeEmpty(Explain(undeclared, scanned));
    }

    /// <summary>
    /// Trap 1: <c>RunModeDriverTests</c> carries the literal attribute text inside
    /// its doc-comment, and a scan that credits it lands on 23 instead of 34 —
    /// which the census did.
    ///
    /// <para>
    /// This shape is refused twice over, independently. With <see cref="StripComments"/>
    /// neutered, the line anchor alone still refuses it — verified by counterfactual —
    /// because the <c>[</c> sits behind <c>/// &lt;c&gt;</c> and never begins its line.
    /// But the anchor is not what does the work in the guard as built either: stripping
    /// deletes the whole <c>///</c> line, anchor or no, before either match runs. Neither
    /// mechanism is individually necessary here — the counterfactual shows stripping is
    /// not <i>sufficient credit</i> for this case, not that the anchor is the sole cause.
    /// <see cref="A_commented_out_declaration_is_not_a_declaration"/> is the shape where
    /// stripping is load-bearing alone, and is why FR-003 is a requirement rather than a
    /// second opinion on this one.
    /// </para>
    /// </summary>
    [Fact]
    public void A_doc_comment_naming_the_collection_attribute_is_not_a_declaration()
    {
        const string source = """
            /// <summary>
            /// The mutation this exists for: giving the run-mode class
            /// <c>[Collection(AspireCollection.Name)]</c>.
            /// </summary>
            public class DocumentedTests
            {
                [Fact]
                public void It_holds() { }
            }
            """;

        Describe("synthetic/DocumentedTests.cs", source).Undeclared.ShouldBeTrue(
            "prose describing the attribute is not the attribute. A guard that cannot tell a "
            + "doc-comment from a declaration is itself a member of the population it enforces.");
    }

    /// <summary>
    /// Trap 1 again, in the one shape line-anchoring does not catch. A
    /// commented-out attribute begins its own line, so the anchor sees it exactly
    /// as it sees a live one, and only <see cref="StripComments"/> tells the two
    /// apart. Without this case the doc-comment test above passes with stripping
    /// removed — the requirement would be asserted by a test that does not depend
    /// on it.
    /// </summary>
    [Fact]
    public void A_commented_out_declaration_is_not_a_declaration()
    {
        const string source = """
            /*
            [Trait("Category", "FixtureLogic")]
            */
            public class ParkedTests
            {
                [Fact]
                public void It_holds() { }
            }
            """;

        Describe("synthetic/ParkedTests.cs", source).Undeclared.ShouldBeTrue(
            "an attribute someone commented out selects nothing: the class runs where it ran "
            + "before, and the comment is the only thing that says otherwise.");
    }

    /// <summary>
    /// A <c>/*</c> inside a string literal is not a comment opener. <see cref="StripComments"/>
    /// used to run over the raw file before counting facts, so a glob like <c>"**/*.cs"</c>
    /// opened a "comment" that a later <c>/* trailing */</c> closed, erasing every
    /// <c>[Fact]</c> in between and dropping the file out of the population — silent-green,
    /// not safe-direction, since <c>Undeclared</c> requires <c>Facts &gt; 0</c>. Facts are now
    /// counted on raw source, so the population gate no longer depends on the stripper at all.
    /// </summary>
    [Fact]
    public void A_string_literal_containing_block_comment_syntax_does_not_erase_the_population()
    {
        const string source = """
            public class GlobTests
            {
                private const string Pattern = "**/*.cs";

                [Fact]
                public void It_holds() { }

                [Fact]
                public void It_also_holds() { }
            }
            /* trailing */
            """;

        TestFile file = Describe("synthetic/GlobTests.cs", source);

        file.Facts.ShouldBe(2,
            "the string literal's `/*` is not a comment opener; a naive stripper treating it as "
            + "one erases both facts and removes the file from the population entirely.");
        file.Undeclared.ShouldBeTrue(
            "the class carries neither declaration, and a wrong fact count must not excuse that.");
    }

    /// <summary>
    /// Trap 2, the same wrong count of 23 by the other route: naming the fixture
    /// in reflection code or in an assertion message is not being decorated with
    /// it — and here the naming exists <i>because</i> the class asserts it does
    /// not acquire the fixture.
    /// </summary>
    [Fact]
    public void Naming_the_fixture_in_code_is_not_a_declaration()
    {
        const string source = """
            public class ReflectingTests
            {
                [Fact]
                public void It_does_not_acquire_the_fixture()
                {
                    CollectionAttribute? collection = typeof(Other).GetCustomAttribute<CollectionAttribute>();
                    collection.ShouldBeNull("a collection attribute injects AspireFixture");
                    Parameters().ShouldNotContain(p => p.ParameterType.Name.Contains("AspireFixture"));
                }
            }
            """;

        Describe("synthetic/ReflectingTests.cs", source).Undeclared.ShouldBeTrue(
            "an identifier in reflection code or an assertion message is a mention, not a "
            + "declaration; the guard must match the attribute on the class declaration.");
    }

    /// <summary>
    /// A misspelled category is not a declaration <see cref="CategoryDeclaration"/> credits.
    /// Before this test existed, matching <c>[Trait("Category"</c> without reading the value
    /// meant <c>[Trait("Category", "FixtureLogick")]</c> satisfied the guard while the two
    /// filtered steps in <c>ci.yml</c> select and exclude neither — the class would run
    /// only in the thirty-minute Docker job, exactly the omission this guard exists to close,
    /// now behind a declaration that looks correct.
    /// </summary>
    [Fact]
    public void A_misspelled_category_value_is_not_a_declaration()
    {
        const string source = """
            [Trait("Category", "FixtureLogick")]
            public class MisspelledCategoryTests
            {
                [Fact]
                public void It_holds() { }
            }
            """;

        Describe("synthetic/MisspelledCategoryTests.cs", source).Undeclared.ShouldBeTrue(
            $"\"FixtureLogick\" selects nothing at {InclusionStepCitation()} and is excluded by nothing at "
            + $"{ExclusionStepCitation()}, so it is not one of the four declarations the guard recognises.");
    }

    /// <summary>
    /// No soft edge: the obligation attaches to classes that produce a verdict.
    /// A helper type produces none, and demanding a category of it would teach
    /// people to annotate files rather than to declare where tests run. The
    /// census's first pass wrongly included seven such files.
    /// </summary>
    [Fact]
    public void A_file_with_no_test_methods_is_not_asked_to_declare()
    {
        const string source = """
            public sealed record IngestSpanResult(int Total, int Attributed);

            public static class IngestSpanMeasurement
            {
                public static IngestSpanResult Measure() => new(0, 0);
            }
            """;

        Describe("synthetic/IngestSpanMeasurement.cs", source).Undeclared.ShouldBeFalse(
            "a file holding no [Fact] or [Theory] contributes no verdict to either job, so it "
            + "has nothing to declare.");
    }

    /// <summary>
    /// Either declaration satisfies the guard, because the two describe different
    /// classes of test and the guard does not adjudicate between them.
    /// </summary>
    [Fact]
    public void Either_declaration_satisfies_the_guard()
    {
        const string collectionDeclared = """
            [Collection(AspireCollection.Name)]
            public class StackTests
            {
                [Fact]
                public void It_holds() { }
            }
            """;

        const string categoryDeclared = """
            [Trait("Category", "FixtureLogic")]
            public class CheapTests
            {
                [Fact]
                public void It_holds() { }
            }
            """;

        Describe("synthetic/StackTests.cs", collectionDeclared).Undeclared.ShouldBeFalse(
            "a class in the fixture's collection has declared: it runs in the integration job.");
        Describe("synthetic/CheapTests.cs", categoryDeclared).Undeclared.ShouldBeFalse(
            "a class carrying a category has declared: the trait decides which job selects it.");
    }

    /// <summary>
    /// #2289, F2 — the positive control for <see cref="WorkflowInvocations"/>.
    /// Asserts parity against an independently-computed raw line-count rather
    /// than a frozen number, so this cannot go stale as <c>ci.yml</c> grows a
    /// step: a reader whose continuation-line scan breaks under-counts and
    /// would let <see cref="Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing"/>
    /// (F1) pass vacuously over an incomplete set — the exact defect this
    /// feature exists to close, rebuilt inside its own guard (AS-6).
    ///
    /// <para>
    /// Phase 6 review, #2289. A second parity check, against a different blind
    /// spot: <see cref="TestInvocation.IsFiltered"/> recognises exactly one
    /// spelling, <c>--filter "..."</c>. An invocation rewritten to
    /// <c>--filter Category=X</c> (unquoted), <c>--filter=Category=X</c>
    /// (equals-joined) or single-quoted still contains the literal substring
    /// <c>--filter</c> and so is still <b>found</b> above — the invocation
    /// count would stay in parity — but <see cref="TestInvocation.Filter"/>
    /// would silently read <c>null</c>, exempting that step from F1's flag
    /// requirement and F3's category derivation without failing anywhere.
    /// Counting the literal substring independently of recognition, the same
    /// shape as the invocation-count check above, catches that the moment a
    /// rewrite introduces it, rather than trying to enumerate every valid
    /// <c>--filter</c> spelling in <see cref="FilterArgument"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void The_workflow_reader_finds_every_dotnet_test_invocation()
    {
        string[] lines = WorkflowLines();
        int rawCount = lines.Count(line => line.TrimStart().StartsWith("dotnet test", StringComparison.Ordinal));

        TestInvocation[] invocations = WorkflowInvocations();

        invocations.Length.ShouldBe(rawCount,
            $"the reader found {invocations.Length} `dotnet test` invocation(s) in {Workflow}, but a raw count "
            + $"of lines whose trimmed text starts with `dotnet test` finds {rawCount}. A reader that "
            + "under-counts here is broken, not the workflow, and every other fact in this class is unsound "
            + "until this one is green again.");

        invocations.Any(invocation => invocation.IsFiltered).ShouldBeTrue(
            $"none of the {invocations.Length} `dotnet test` invocation(s) found in {Workflow} carry a "
            + "--filter. F1 below has nothing to check without at least one — this positive control has gone "
            + "stale along with it.");

        int filterMentions = invocations.Count(invocation =>
            invocation.Command.Contains("--filter", StringComparison.Ordinal));
        int recognizedFilters = invocations.Count(invocation => invocation.IsFiltered);

        filterMentions.ShouldBe(recognizedFilters,
            $"{filterMentions} invocation(s) in {Workflow} contain the literal `--filter` substring, but only "
            + $"{recognizedFilters} match the quoted-value spelling `--filter \"...\"` this reader recognises. "
            + "A rewrite to an unquoted, equals-joined, or single-quoted --filter would silently exempt that "
            + "step from F1's flag requirement and F3's category derivation while still counting as \"found\" "
            + "above — exactly the vacuous-pass shape this class exists to prevent.");
    }

    /// <summary>
    /// #2289, F1 — <b>the red this feature exists to turn green</b> (spec
    /// AS-3). Neither filtered step in <c>ci.yml</c> carries
    /// <see cref="TreatNoTestsAsErrorFlag"/> today, so a typo'd or renamed
    /// category selects zero tests and the step still exits 0. The message
    /// names every offending invocation's real line number, its filter, and
    /// the exact text to add — this is <c>infra-engineer</c>'s brief for
    /// phase 4b, not just "assertion failed".
    /// </summary>
    [Fact]
    public void Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing()
    {
        TestInvocation[] filtered = [.. WorkflowInvocations().Where(invocation => invocation.IsFiltered)];

        filtered.ShouldNotBeEmpty(
            $"no filtered `dotnet test` invocation was found in {Workflow} — the reader is broken, not the "
            + "workflow. An empty set here would let this fact pass vacuously over nothing to check, which is "
            + "the exact defect class this feature exists to close.");

        TestInvocation[] missing = [.. filtered.Where(invocation => !invocation.FailsOnNoTests)];

        missing.ShouldBeEmpty(ExplainMissingFlag(missing));
    }

    /// <summary>
    /// #2289, F3. Cross-checks the category names <see cref="WorkflowInvocations"/>
    /// derives from <c>ci.yml</c>'s filters against the trait names actually
    /// declared by a test class (AS-4) — reusing <see cref="AnyCategoryDeclaration"/>
    /// and <see cref="StripComments"/> rather than re-scanning the tree a
    /// second, different way. The converse (a class declaring a name
    /// <c>ci.yml</c> does not filter on) is already covered by
    /// <see cref="Every_integration_test_class_declares_where_it_runs"/>, since
    /// such a class no longer matches the derived <see cref="CategoryDeclaration"/>
    /// set once phase 4b builds it from this same source (AS-5).
    ///
    /// <para>
    /// Phase 6 review, #2289. Calls <see cref="DerivedCategoryNames"/> rather
    /// than recomputing the same set inline: the two used to be a literal
    /// duplicate of each other under a comment claiming they "can never drift
    /// apart" — the weakest possible defense of that claim. Calling the one
    /// method <see cref="CategoryDeclaration"/> is itself built from makes it
    /// true by construction instead.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_category_the_workflow_filters_on_is_declared_by_a_test_class()
    {
        string[] categories = DerivedCategoryNames();

        categories.ShouldNotBeEmpty(
            $"the reader derived no category names from {Workflow}'s filters — an empty set would make every "
            + "class vacuously \"declared\", which is the exact silent-pass shape this fact exists to refuse.");

        HashSet<string> declared = DeclaredCategoryNames();

        string[] undeclared = [.. categories.Where(name => !declared.Contains(name))];

        undeclared.ShouldBeEmpty(
            $"{Workflow} filters on {string.Join(", ", undeclared)}, but no test class under {ScannedTree} "
            + "declares that category — that filter selects zero tests today, exactly the shape this whole "
            + "feature exists to catch, one step earlier than CI.");
    }

    /// <summary>
    /// #2289, F4 — the reader's parsing logic, exercised directly against
    /// literal workflow text rather than the real file, exactly as the eight
    /// pre-existing facts feed <see cref="Describe"/> synthetic sources
    /// (plan.md §3.3).
    /// </summary>
    [Fact]
    public void A_filtered_invocation_split_across_continuation_lines_is_read_as_one_command()
    {
        const string workflow = """
            jobs:
              backend:
                steps:
                  - run: |
                      dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
                        -c Release \
                        --no-build \
                        --filter "Category=FixtureLogic" \
                        --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min
            """;

        TestInvocation[] invocations = ParseWorkflow(workflow);

        invocations.Length.ShouldBe(1,
            "a `dotnet test` invocation split across five continuation lines must be read as one invocation, "
            + "not five separate ones.");
        invocations[0].Filter.ShouldBe("Category=FixtureLogic",
            "the --filter value sits on a continuation line joined onto the opening `dotnet test` line.");
    }

    [Fact]
    public void The_flag_text_inside_a_quoted_filter_value_does_not_count_as_present()
    {
        const string workflow = """
                  - run: |
                      dotnet test foo.csproj \
                        --filter "Category=RunConfiguration.TreatNoTestsAsError=true"
            """;

        TestInvocation invocation = ParseWorkflow(workflow).Single();

        invocation.FailsOnNoTests.ShouldBeFalse(
            "the flag's text sitting inside a quoted --filter value is not the flag reaching VSTest — there is "
            + "no standalone `--` separator anywhere in this command, so nothing after it to check.");
    }

    /// <summary>
    /// Phase 6 review, #2289. The sibling above proves nothing about a
    /// quoted value that itself <i>contains</i> a standalone <c>--</c>
    /// followed by the flag text — split on plain spaces without stripping
    /// quotes first, this tokenizes into exactly the shape <see
    /// cref="TestInvocation.FailsOnNoTests"/> looks for, and would be a false
    /// positive: the flag never actually left the quotes <c>dotnet test</c>
    /// saw as one <c>--filter</c> argument, so it never reached VSTest.
    /// </summary>
    [Fact]
    public void A_standalone_separator_inside_a_quoted_filter_value_does_not_count_as_present()
    {
        const string workflow = """
                  - run: |
                      dotnet test foo.csproj \
                        --filter "Category=A -- RunConfiguration.TreatNoTestsAsError=true &Category=B"
            """;

        TestInvocation invocation = ParseWorkflow(workflow).Single();

        invocation.FailsOnNoTests.ShouldBeFalse(
            "the `--` and the flag text both sit inside the quoted --filter value; splitting on plain spaces "
            + "without stripping quotes first would misread them as a real standalone separator plus a real "
            + "flag token, crediting a flag that never left the quotes and so never reached VSTest.");
    }

    [Fact]
    public void The_flag_appearing_before_the_separator_does_not_count_as_present()
    {
        const string workflow = """
                  - run: |
                      dotnet test foo.csproj \
                        --filter "Category=X" \
                        RunConfiguration.TreatNoTestsAsError=true \
                        -- SomeOtherSetting=false
            """;

        TestInvocation invocation = ParseWorkflow(workflow).Single();

        invocation.FailsOnNoTests.ShouldBeFalse(
            "the flag token sits before the standalone `--` separator, so it never reaches VSTest as a "
            + "run-setting override — it is an unrecognised dotnet test argument instead.");
    }

    [Fact]
    public void An_unfiltered_dotnet_test_invocation_is_not_asked_to_carry_the_flag()
    {
        const string workflow = """
                  - run: |
                      dotnet test tests/Shared.Kernel.Tests/SmartSentinelEye.Shared.Kernel.Tests.csproj \
                        -c Release --no-build
            """;

        TestInvocation invocation = ParseWorkflow(workflow).Single();

        invocation.IsFiltered.ShouldBeFalse(
            "this invocation carries no --filter, so it cannot select zero tests the way a filtered one can — "
            + "F1 excludes it from the invocations it holds to the flag.");
    }

    [Fact]
    public void An_exclusion_filter_with_two_terms_yields_both_category_names()
    {
        string[] categories = Categories("Category!=Measurement&Category!=Disruptive&Category!=Maintenance");

        categories.ShouldBe(["Measurement", "Disruptive", "Maintenance"],
            "each Category!=X term names a separate category that must exist on the test side; combining them "
            + "into one string would make F3 check a name no class could ever declare.");
    }

    [Fact]
    public void A_filter_the_workflow_does_not_contain_yields_no_categories()
    {
        Categories(string.Empty).ShouldBeEmpty(
            "no Category term to find means no category names — an empty or absent filter must not "
            + "manufacture names for F3 to check.");
    }

    [Fact]
    public void A_filter_the_workflow_does_contain_yields_its_category_names()
    {
        Categories("Category=FixtureLogic").ShouldBe(["FixtureLogic"],
            "the positive twin: a filter that does name a category must yield exactly that name.");
    }

    private static TestInvocation[] ParseWorkflow(string workflow) =>
        ParseInvocations(workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));

    /// <summary>
    /// The trait-category names actually declared under <see cref="ScannedTree"/>,
    /// read with the same comment-stripping <see cref="Describe"/> already
    /// applies before matching a category declaration — not a second, competing
    /// scanner.
    /// </summary>
    private static HashSet<string> DeclaredCategoryNames()
    {
        DirectoryInfo root = RepositoryRoot();

        IEnumerable<string> names = Directory
            .EnumerateFiles(Path.Combine(root.FullName, ScannedTree), "*.cs", SearchOption.AllDirectories)
            .Select(file => Relative(root, file))
            .Where(IsSource)
            .SelectMany(relative => AnyCategoryDeclaration
                .Matches(StripComments(File.ReadAllText(Path.Combine(root.FullName, relative))))
                .Select(match => match.Groups["name"].Value));

        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    /// <summary>
    /// #2289, T009 (phase 6 review). Replaces the deleted
    /// <c>CheapStep</c>/<c>ExcludeStep</c> constants for <see cref="Explain"/>
    /// and <see cref="A_misspelled_category_value_is_not_a_declaration"/>:
    /// those held hard-coded <c>ci.yml</c> line numbers in a comment, which
    /// is exactly the staleness spec 185 §1.2 caught (one of the two had
    /// already drifted, silently). This reads the current line back out of
    /// the same <see cref="WorkflowInvocations"/> the rest of the class
    /// already trusts, so a line shifting under a workflow edit cannot leave
    /// the message wrong the way the constant did.
    ///
    /// <para>
    /// <c>FirstOrDefault</c>, not <c>First</c>: Shouldly evaluates a
    /// <c>ShouldBeTrue</c>/<c>ShouldBeEmpty</c> message argument eagerly, so
    /// this runs on every green run of <see cref="Explain"/>'s caller, not
    /// only on failure. A workflow that no longer matches
    /// <paramref name="matchesFilter"/> at all must not throw an opaque,
    /// message-less <c>InvalidOperationException</c> out of a passing test —
    /// it falls back to a description naming <b>this</b> guard's own
    /// staleness instead.
    /// </para>
    /// </summary>
    private static string CitationFor(Func<string, bool> matchesFilter, string description)
    {
        TestInvocation? invocation = WorkflowInvocations()
            .FirstOrDefault(candidate => candidate.IsFiltered && matchesFilter(candidate.Filter!));

        return invocation is null
            ? $"{Workflow}'s {description} (not located — the reader may be stale)"
            : $"{Workflow}:{invocation.Line}";
    }

    /// <summary>
    /// The step that names categories with a bare <c>Category=X</c> — an
    /// inclusion, which is what "selected by" means in <see cref="Explain"/>'s
    /// message. Excludes an exclusion term's <c>!=</c> spelling explicitly,
    /// since that also contains a literal <c>=</c>.
    /// </summary>
    private static string InclusionStepCitation() =>
        CitationFor(
            filter => filter.Contains('=', StringComparison.Ordinal)
                && !filter.Contains("!=", StringComparison.Ordinal),
            "inclusion step");

    /// <summary>The step that names categories with <c>Category!=X</c>.</summary>
    private static string ExclusionStepCitation() =>
        CitationFor(filter => filter.Contains("!=", StringComparison.Ordinal), "exclusion step");

    private static string ExplainMissingFlag(TestInvocation[] missing)
    {
        List<string> message =
        [
            $"{missing.Length} filtered `dotnet test` invocation(s) in {Workflow} can select zero tests and "
            + $"still exit 0, because none carries `-- {TreatNoTestsAsErrorFlag}` after a standalone `--`:",
            string.Empty,
        ];

        message.AddRange(missing.Select(invocation =>
            $"  {Workflow}:{invocation.Line} — --filter \"{invocation.Filter}\" — "
            + $"add `-- {TreatNoTestsAsErrorFlag}` as the final continuation line"));

        return string.Join(Environment.NewLine, message);
    }

    /// <summary>
    /// Phase 6 review, #2289. <c>"A", "B" and "C"</c> — the same style the
    /// hard-coded list in <see cref="Explain"/> used to spell out by hand,
    /// now built from <see cref="DerivedCategoryNames"/> so a rename on the
    /// <c>ci.yml</c> side cannot leave this message asserting a set that no
    /// longer matches the file it just cited a fresh line number from.
    /// </summary>
    private static string FormatNameList(IReadOnlyList<string> names)
    {
        string[] quoted = [.. names.Select(name => $"\"{name}\"")];

        return quoted.Length switch
        {
            0 => string.Empty,
            1 => quoted[0],
            _ => string.Join(", ", quoted[..^1]) + $" and {quoted[^1]}",
        };
    }

    private static string Explain(TestFile[] undeclared, TestFile[] scanned)
    {
        int tests = undeclared.Sum(file => file.Facts);
        int population = scanned.Count(file => file.Facts > 0);

        List<string> message =
        [
            $"{undeclared.Length} of {population} test classes under {ScannedTree} carry neither "
            + "[Collection(AspireCollection.Name)] nor [Trait(\"Category\", …)], so their "
            + $"{tests} tests declare no job and are read only by the 30-minute integration job:",
            string.Empty,
        ];

        message.AddRange(undeclared
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"  {file.Path} ({file.Facts} tests)"));

        string cheapStep = InclusionStepCitation();
        string excludeStep = ExclusionStepCitation();
        string categoryList = FormatNameList(DerivedCategoryNames());

        message.Add(string.Empty);
        message.Add(
            $"Only {categoryList} count — that is "
            + $"the exact set {cheapStep} selects and {excludeStep} excludes, so any other spelling is "
            + "silently undeclared, not merely unrecognised.");
        message.Add(string.Empty);
        message.Add(
            "Add one of the legitimate declarations — the correct fix differs between them and the "
            + "wrong one is silent:");
        message.Add(
            $"  needs no stack                 → [Trait(\"Category\", \"FixtureLogic\")], selected by {cheapStep}");
        message.Add(
            "  needs a stack CI does not boot → [Trait(\"Category\", \"Measurement\" | \"Disruptive\" "
            + $"| \"Maintenance\")], excluded by {excludeStep}");
        message.Add(
            "  needs the fixture's stack      → [Collection(AspireCollection.Name)], no trait needed");

        return string.Join(Environment.NewLine, message);
    }

    private static TestFile[] ScannedFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return Directory
            .EnumerateFiles(Path.Combine(root.FullName, ScannedTree), "*.cs", SearchOption.AllDirectories)
            .Select(file => Relative(root, file))
            .Where(IsSource)
            .Select(relative => Describe(relative, File.ReadAllText(Path.Combine(root.FullName, relative))))
            .ToArray();
    }

    private static TestFile Describe(string path, string source)
    {
        // The population gate (Facts > 0) is counted on raw source, not on the
        // comment-stripped text below: BlockComment is a single naive `/\*.*?\*/`
        // over the whole file, so a `/*` inside a string literal (a glob like
        // "**/*.cs") opens a "comment" the stripper never closes correctly,
        // erasing every fact after it and silently dropping the file out of the
        // population it should have been checked against. The fact regex is
        // already line-anchored on `[`, so a comment cannot inflate the count —
        // only a genuine [Fact]/[Theory] at the start of its own line counts.
        int facts = FactOrTheory.Count(source);

        string declarations = StripComments(source);

        return new TestFile(
            path,
            facts,
            CollectionDeclaration.IsMatch(declarations),
            CategoryDeclaration.IsMatch(declarations));
    }

    /// <summary>
    /// FR-003. Block comments first, then <c>//</c> to end of line — which covers
    /// <c>///</c> XML docs, since those begin with it.
    /// </summary>
    private static string StripComments(string source) =>
        LineComment.Replace(BlockComment.Replace(source, string.Empty), string.Empty);

    /// <summary>
    /// Reported with <c>/</c> throughout. <see cref="Path.GetRelativePath"/>
    /// returns the platform separator, so a backslash in an expected string is
    /// green on Windows and red on Linux CI — this repository has been bitten by
    /// exactly that.
    /// </summary>
    private static string Relative(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsSource(string relative) =>
        !relative.Contains("/obj/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal);

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

    private sealed record TestFile(string Path, int Facts, bool DeclaresCollection, bool DeclaresCategory)
    {
        public bool Undeclared => Facts > 0 && !DeclaresCollection && !DeclaresCategory;
    }
}
