using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 190 (issue #2257) phase 4a — the characterisation harness for the
/// extraction of <c>RepositorySource</c>, <c>SourceMask</c> and
/// <c>RouteChainReader</c> out of the six source-scanning guards. ADR-0144:
/// this is the GREEN colour — behaviour-preserving — and this file is not part
/// of what is preserved. It is the proof that the extraction preserves it.
///
/// <para>
/// <b>Two mechanisms, because one is not enough (plan.md §7).</b>
/// </para>
///
/// <para>
/// <b>M1 — the fixture golden test, permanent.</b> Every fixture in
/// <see cref="SourceScanFixtures"/> masked with every
/// <c>SourceMask.MaskStrictness</c>, expected output written out literally —
/// not hashed, so a reader sees exactly which character moved. It asserts the
/// three masker behaviours actually <b>disagree</b> on
/// <see cref="SourceScanFixtures.VerbatimString"/>,
/// <see cref="SourceScanFixtures.EscapedQuote"/> and
/// <see cref="SourceScanFixtures.QuoteAsCharLiteral"/> — a test that only
/// asserted agreement would pass against a masker someone had already
/// collapsed to one behaviour.
/// </para>
///
/// <para>
/// <b>M1 is written against the shared type before it exists</b> (spec 190's
/// phase-4a brief). <c>SourceMask</c> and <c>SourceMask.MaskStrictness</c> are
/// not implemented until phase 4b's <c>SourceMask.cs</c> (tasks.md T12), so
/// this file does not compile yet — the compiler names the missing types. That
/// is the concrete red state phase 4b turns green; it is not a defect in this
/// file and no stub should be added here to silence it (that would be
/// production code written by the test-writer phase, which the phase split
/// forbids).
/// </para>
///
/// <para>
/// <b>M2 — the real-corpus sweep, transitional.</b> Frozen, throwaway verbatim
/// copies of each pre-refactor masker and chain-end walk, swept over every
/// <c>.cs</c> under <c>src/*/Api</c>. Today, before any extraction exists, the
/// only comparison available is frozen copy against frozen copy — which is
/// still a real claim: it is the first time spec.md §1.3's "byte-identical"
/// assertion about the three <c>CommentsAndLiteralInteriors</c> guards, and the
/// three identical <c>StatementEnd</c> copies, is checked by running them
/// against real files rather than reading their source. Phase 4b's T13 extends
/// this region to compare each frozen copy against <c>SourceMask.Apply</c> /
/// <c>RouteChainReader.StatementEnd</c> once those exist; T29 deletes this
/// entire region, together with the frozen copies, once the extraction is
/// proven (plan.md §7 — a permanent frozen copy of the thing just deduplicated
/// is the defect this spec closes).
/// </para>
/// </summary>
public sealed class SourceScanCharacterisationTests
{
    // =====================================================================
    // M2 — frozen pre-refactor implementations (throwaway reference code,
    // deleted by tasks.md T29). Each is a byte-for-byte copy of the private
    // method it is named after, taken from the guard and spec that wrote it.
    // =====================================================================

    /// <summary>
    /// Behaviour A ("CommentsAndLiteralInteriors"), as
    /// <c>PreconditionDeclarationTests.Mask</c> (spec 072) has it today.
    /// </summary>
    private static string MaskAsSpec072Wrote(string text)
    {
        static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';

        static int Blank(char[] masked, int from, int count)
        {
            for (int i = from; i < from + count && i < masked.Length; i++)
            {
                masked[i] = ' ';
            }

            return from + count;
        }

        int MaskBlockComment(string t, char[] masked, int from)
        {
            int i = from;
            while (i < t.Length && !(t[i] == '*' && Next(t, i) == '/'))
            {
                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return Blank(masked, i, 2);
        }

        int MaskVerbatim(string t, char[] masked, int from)
        {
            int i = from + 2;
            while (i < t.Length)
            {
                if (t[i] == '"' && Next(t, i) == '"')
                {
                    masked[i] = ' ';
                    masked[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                if (t[i] == '"')
                {
                    return i + 1;
                }

                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return i;
        }

        int MaskLiteral(string t, char[] masked, int from)
        {
            char quote = t[from];
            int i = from + 1;
            while (i < t.Length && t[i] != quote && t[i] != '\n')
            {
                masked[i] = ' ';
                if (t[i] == '\\' && i + 1 < t.Length)
                {
                    masked[i + 1] = ' ';
                    i++;
                }

                i++;
            }

            return i + 1;
        }

        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '/' && Next(text, index) == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    masked[index++] = ' ';
                }
            }
            else if (text[index] == '/' && Next(text, index) == '*')
            {
                index = MaskBlockComment(text, masked, index);
            }
            else if (text[index] == '@' && Next(text, index) == '"')
            {
                index = MaskVerbatim(text, masked, index);
            }
            else if (text[index] is '"' or '\'')
            {
                index = MaskLiteral(text, masked, index);
            }
            else
            {
                index++;
            }
        }

        return new string(masked);
    }

    /// <summary>
    /// The same behaviour, as <c>RouteValueRefusalDeclarationTests.Mask</c>
    /// (spec 091) has it today — a separate frozen copy so the comparison below
    /// is a claim about two files, not a function checked against itself.
    /// </summary>
#pragma warning disable S4144 // Deliberate: freezing the byte-identical body is the characterisation, not an accident.
    private static string MaskAsSpec091Wrote(string text)
    {
        static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';

        static int Blank(char[] masked, int from, int count)
        {
            for (int i = from; i < from + count && i < masked.Length; i++)
            {
                masked[i] = ' ';
            }

            return from + count;
        }

        int MaskBlockComment(string t, char[] masked, int from)
        {
            int i = from;
            while (i < t.Length && !(t[i] == '*' && Next(t, i) == '/'))
            {
                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return Blank(masked, i, 2);
        }

        int MaskVerbatim(string t, char[] masked, int from)
        {
            int i = from + 2;
            while (i < t.Length)
            {
                if (t[i] == '"' && Next(t, i) == '"')
                {
                    masked[i] = ' ';
                    masked[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                if (t[i] == '"')
                {
                    return i + 1;
                }

                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return i;
        }

        int MaskLiteral(string t, char[] masked, int from)
        {
            char quote = t[from];
            int i = from + 1;
            while (i < t.Length && t[i] != quote && t[i] != '\n')
            {
                masked[i] = ' ';
                if (t[i] == '\\' && i + 1 < t.Length)
                {
                    masked[i + 1] = ' ';
                    i++;
                }

                i++;
            }

            return i + 1;
        }

        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '/' && Next(text, index) == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    masked[index++] = ' ';
                }
            }
            else if (text[index] == '/' && Next(text, index) == '*')
            {
                index = MaskBlockComment(text, masked, index);
            }
            else if (text[index] == '@' && Next(text, index) == '"')
            {
                index = MaskVerbatim(text, masked, index);
            }
            else if (text[index] is '"' or '\'')
            {
                index = MaskLiteral(text, masked, index);
            }
            else
            {
                index++;
            }
        }

        return new string(masked);
    }
#pragma warning restore S4144

    /// <summary>
    /// The same behaviour again, as
    /// <c>StatusProducerDeclarationTests.Mask</c> (spec 130) has it today.
    /// </summary>
#pragma warning disable S4144 // Deliberate: freezing the byte-identical body is the characterisation, not an accident.
    private static string MaskAsSpec130Wrote(string text)
    {
        static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';

        static int Blank(char[] masked, int from, int count)
        {
            for (int i = from; i < from + count && i < masked.Length; i++)
            {
                masked[i] = ' ';
            }

            return from + count;
        }

        int MaskBlockComment(string t, char[] masked, int from)
        {
            int i = from;
            while (i < t.Length && !(t[i] == '*' && Next(t, i) == '/'))
            {
                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return Blank(masked, i, 2);
        }

        int MaskVerbatim(string t, char[] masked, int from)
        {
            int i = from + 2;
            while (i < t.Length)
            {
                if (t[i] == '"' && Next(t, i) == '"')
                {
                    masked[i] = ' ';
                    masked[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                if (t[i] == '"')
                {
                    return i + 1;
                }

                masked[i] = t[i] == '\n' ? '\n' : ' ';
                i++;
            }

            return i;
        }

        int MaskLiteral(string t, char[] masked, int from)
        {
            char quote = t[from];
            int i = from + 1;
            while (i < t.Length && t[i] != quote && t[i] != '\n')
            {
                masked[i] = ' ';
                if (t[i] == '\\' && i + 1 < t.Length)
                {
                    masked[i + 1] = ' ';
                    i++;
                }

                i++;
            }

            return i + 1;
        }

        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '/' && Next(text, index) == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    masked[index++] = ' ';
                }
            }
            else if (text[index] == '/' && Next(text, index) == '*')
            {
                index = MaskBlockComment(text, masked, index);
            }
            else if (text[index] == '@' && Next(text, index) == '"')
            {
                index = MaskVerbatim(text, masked, index);
            }
            else if (text[index] is '"' or '\'')
            {
                index = MaskLiteral(text, masked, index);
            }
            else
            {
                index++;
            }
        }

        return new string(masked);
    }
#pragma warning restore S4144

    /// <summary>
    /// Behaviour C ("CommentsOnlyLiteralsIntact"), as
    /// <c>ConcurrencyConflictDeclarationTests.MaskComments</c> (spec 075) has it
    /// today — comments blanked, literal content left intact, no <c>@"</c> and
    /// no char-literal branch at all.
    /// </summary>
    private static string MaskAsSpec075Wrote(string text)
    {
        static bool Starts(string t, int index, string token) =>
            index + token.Length <= t.Length
            && string.CompareOrdinal(t, index, token, 0, token.Length) == 0;

        static int EndOfStringLiteral(string t, int open)
        {
            for (int i = open + 1; i < t.Length; i++)
            {
                if (t[i] is '"' or '\n')
                {
                    return i;
                }
            }

            return t.Length - 1;
        }

        int BlankBlockComment(string t, char[] masked, int start)
        {
            int close = t.IndexOf("*/", start + 2, StringComparison.Ordinal);
            int end = close < 0 ? t.Length : close + 2;
            for (int i = start; i < end; i++)
            {
                if (t[i] != '\n')
                {
                    masked[i] = ' ';
                }
            }

            return end - 1;
        }

        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                index = EndOfStringLiteral(text, index) + 1;
            }
            else if (Starts(text, index, "//"))
            {
                while (index < text.Length && text[index] != '\n')
                {
                    masked[index] = ' ';
                    index++;
                }
            }
            else if (Starts(text, index, "/*"))
            {
                index = BlankBlockComment(text, masked, index) + 1;
            }
            else
            {
                index++;
            }
        }

        return new string(masked);
    }

    /// <summary>
    /// Behaviour B, stage one ("CommentsBlankedLiteralsIntact"), as
    /// <c>EndpointScopeDeclarationTests.WithoutComments</c> (spec 070) has it
    /// today — comments blanked, literals skipped whole with content intact.
    /// </summary>
    private static string BlankCommentsAsSpec070Wrote(string source)
    {
        static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';

        static int EndOfLiteral(string t, int start, bool verbatim)
        {
            char quote = t[start];
            int index = start + 1;
            while (index < t.Length)
            {
                char current = t[index];
                if (verbatim)
                {
                    if (current == quote)
                    {
                        if (Next(t, index) == quote)
                        {
                            index += 2;
                            continue;
                        }

                        return index + 1;
                    }

                    index++;
                    continue;
                }

                if (current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == quote)
                {
                    return index + 1;
                }

                if (current == '\n')
                {
                    return index;
                }

                index++;
            }

            return index;
        }

        char[] result = source.ToCharArray();
        int index2 = 0;
        while (index2 < source.Length)
        {
            char current = source[index2];

            if (current == '/' && Next(source, index2) == '/')
            {
                while (index2 < source.Length && source[index2] != '\n')
                {
                    result[index2++] = ' ';
                }

                continue;
            }

            if (current == '/' && Next(source, index2) == '*')
            {
                while (index2 < source.Length && !(source[index2] == '*' && Next(source, index2) == '/'))
                {
                    if (source[index2] != '\n')
                    {
                        result[index2] = ' ';
                    }

                    index2++;
                }

                for (int blank = 0; blank < 2 && index2 < source.Length; blank++)
                {
                    result[index2++] = ' ';
                }

                continue;
            }

            if (current == '@' && Next(source, index2) == '"')
            {
                index2 = EndOfLiteral(source, index2 + 1, verbatim: true);
                continue;
            }

            if (current is '"' or '\'')
            {
                index2 = EndOfLiteral(source, index2, verbatim: false);
                continue;
            }

            index2++;
        }

        return new string(result);
    }

    /// <summary>
    /// Behaviour B, stage two ("LiteralInteriorsOnly"), as
    /// <c>EndpointScopeDeclarationTests.MaskLiterals</c> (spec 070) has it
    /// today — literal interiors blanked, comments untouched (this stage
    /// assumes stage one already ran).
    /// </summary>
    private static string BlankLiteralsAsSpec070Wrote(string text)
    {
        static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';

        static int EndOfLiteral(string t, int start, bool verbatim)
        {
            char quote = t[start];
            int index = start + 1;
            while (index < t.Length)
            {
                char current = t[index];
                if (verbatim)
                {
                    if (current == quote)
                    {
                        if (Next(t, index) == quote)
                        {
                            index += 2;
                            continue;
                        }

                        return index + 1;
                    }

                    index++;
                    continue;
                }

                if (current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == quote)
                {
                    return index + 1;
                }

                if (current == '\n')
                {
                    return index;
                }

                index++;
            }

            return index;
        }

        char[] result = text.ToCharArray();
        int index2 = 0;
        while (index2 < text.Length)
        {
            int start;
            int end;

            if (text[index2] == '@' && Next(text, index2) == '"')
            {
                start = index2 + 1;
                end = EndOfLiteral(text, index2 + 1, verbatim: true);
            }
            else if (text[index2] is '"' or '\'')
            {
                start = index2;
                end = EndOfLiteral(text, index2, verbatim: false);
            }
            else
            {
                index2++;
                continue;
            }

            for (int inner = start + 1; inner < end - 1 && inner < text.Length; inner++)
            {
                if (result[inner] != '\n')
                {
                    result[inner] = ' ';
                }
            }

            index2 = Math.Max(end, index2 + 1);
        }

        return new string(result);
    }

    /// <summary>
    /// The <c>NotFound</c> (<c>-1</c>) / already-masked chain-end walk, as
    /// <c>PreconditionDeclarationTests.StatementEnd</c> (spec 072) has it today.
    /// </summary>
    private static int StatementEndAsSpec072Wrote(string masked, int from)
    {
        int depth = 0;
        for (int i = from; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The same shape, as <c>RouteValueRefusalDeclarationTests.StatementEnd</c>
    /// (spec 091) has it today.
    /// </summary>
#pragma warning disable S4144 // Deliberate: freezing the byte-identical body is the characterisation, not an accident.
    private static int StatementEndAsSpec091Wrote(string masked, int from)
    {
        int depth = 0;
        for (int i = from; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return i;
            }
        }

        return -1;
    }
#pragma warning restore S4144

    /// <summary>
    /// The same shape again, as
    /// <c>StatusProducerDeclarationTests.StatementEnd</c> (spec 130) has it
    /// today.
    /// </summary>
#pragma warning disable S4144 // Deliberate: freezing the byte-identical body is the characterisation, not an accident.
    private static int StatementEndAsSpec130Wrote(string masked, int from)
    {
        int depth = 0;
        for (int i = from; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return i;
            }
        }

        return -1;
    }
#pragma warning restore S4144

    /// <summary>
    /// The literal-aware, <c>NotFound</c>-sentinel chain-end walk, as
    /// <c>ConcurrencyConflictDeclarationTests.StatementEnd</c> (spec 075) has it
    /// today — steps over string and char literals itself, because its input is
    /// only comment-masked (<see cref="MaskAsSpec075Wrote"/>), not
    /// literal-masked (issue #2183).
    /// </summary>
    private static int StatementEndAsSpec075Wrote(string text, int from)
    {
        static int EndOfStringLiteral(string t, int open)
        {
            for (int i = open + 1; i < t.Length; i++)
            {
                if (t[i] is '"' or '\n')
                {
                    return i;
                }
            }

            return t.Length - 1;
        }

        static int EndOfCharLiteral(string t, int open)
        {
            int i = open + 1;
            while (i < t.Length)
            {
                if (t[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (t[i] is '\'' or '\n')
                {
                    return i;
                }

                i++;
            }

            return t.Length - 1;
        }

        int depth = 0;
        int index = from;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '"')
            {
                index = EndOfStringLiteral(text, index);
            }
            else if (c == '\'')
            {
                index = EndOfCharLiteral(text, index);
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>
    /// The <c>EndOfText</c>-sentinel, already-masked chain-end walk, as
    /// <c>EndpointScopeDeclarationTests.StatementEnd</c> (spec 070/085) has it
    /// today — returns <c>masked.Length</c>, not <c>-1</c>, because its three
    /// call sites use the result directly as a slice bound (see that class's own
    /// 22-line doc comment: do not "fix" this to <c>-1</c>).
    /// </summary>
    private static int StatementEndAsSpec070Wrote(string masked, int from)
    {
        int depth = 0;
        for (int i = from; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return i;
            }
        }

        return masked.Length;
    }

    // =====================================================================
    // M2 — real-corpus sweep infrastructure. Not one of the frozen copies:
    // this is harness scaffolding, deleted along with the rest of this region
    // by tasks.md T29.
    // =====================================================================

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

    private static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    private static List<string> ApiSourceFiles(DirectoryInfo root)
    {
        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateDirectories(src)
            .Select(context => Path.Combine(context, "Api"))
            .Where(Directory.Exists)
            .SelectMany(api => Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .Where(file => !file.Contains("/obj/", StringComparison.Ordinal)
                && !file.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string ReadAsGuardsDo(DirectoryInfo root, string relativeFile) =>
        File.ReadAllText(Path.Combine(root.FullName, relativeFile)).Replace("\r", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Harness-only helper mirroring the guards' own (byte-identical) balanced
    /// -delimiter walk — used here to find each mapping call's argument list so
    /// the chain-end sweep has real anchors, not to characterise anything of its
    /// own.
    /// </summary>
    private static int Balanced(string text, int openIndex, char open, char close)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static readonly Regex MappingCall = new(
        @"\.Map[A-Za-z]*\(", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex MutatingMappingCall = new(
        @"\.(?:Post|Put|Patch|Delete)\(", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// Every mapping call's argument-list end, found generically (any
    /// <c>.MapXxx(</c>) rather than by one guard's own regex — a neutral proxy
    /// anchor, adequate for sweeping a chain-end walk's <em>mechanism</em>
    /// (bracket depth, literal-stepping) without reproducing any guard's
    /// rule-specific mapping regex, which is the guard's business, not this
    /// harness's (plan.md §3.3).
    /// </summary>
    private static IEnumerable<int> MappingCallEnds(string masked, Regex callPattern)
    {
        foreach (Match call in callPattern.Matches(masked))
        {
            int open = call.Index + call.Length - 1;
            int close = Balanced(masked, open, '(', ')');
            if (close >= 0)
            {
                yield return close + 1;
            }
        }
    }

    private static void AssertIdenticalAcrossRealCorpus(
        string leftName, Func<string, string> left,
        string rightName, Func<string, string> right)
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in ApiSourceFiles(root))
        {
            string text = ReadAsGuardsDo(root, file);
            string leftResult = left(text);
            string rightResult = right(text);

            if (string.Equals(leftResult, rightResult, StringComparison.Ordinal))
            {
                continue;
            }

            int offset = FirstMismatch(leftResult, rightResult);
            char leftChar = offset < leftResult.Length ? leftResult[offset] : '∅';
            char rightChar = offset < rightResult.Length ? rightResult[offset] : '∅';
            true.ShouldBeTrue(
                $"{leftName} and {rightName} disagree in {file} at offset {offset}: "
                + $"'{leftChar}' (U+{(int)leftChar:X4}) vs '{rightChar}' (U+{(int)rightChar:X4}).");
        }
    }

    private static int FirstMismatch(string left, string right)
    {
        int shortest = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shortest; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return shortest;
    }

    // =====================================================================
    // M2 — the facts. All frozen-copy-against-frozen-copy, or a smoke check
    // against corruption, because SourceMask/RouteChainReader do not exist
    // yet (tasks.md T2's own done-when). T13/T22-26 extend these to compare
    // against the shared implementation once it does.
    // =====================================================================

    /// <summary>
    /// spec.md §1.3's MD5-identical claim about the three
    /// <c>CommentsAndLiteralInteriors</c> guards, checked by running the code
    /// rather than reading it: <c>PreconditionDeclarationTests</c>,
    /// <c>RouteValueRefusalDeclarationTests</c> and
    /// <c>StatusProducerDeclarationTests</c>' current maskers produce the same
    /// output on every file under <c>src/*/Api</c>.
    /// </summary>
    [Fact]
    public void The_three_byte_identical_A_maskers_already_agree_on_every_api_source_file()
    {
        AssertIdenticalAcrossRealCorpus(
            nameof(MaskAsSpec072Wrote), MaskAsSpec072Wrote,
            nameof(MaskAsSpec091Wrote), MaskAsSpec091Wrote);

        AssertIdenticalAcrossRealCorpus(
            nameof(MaskAsSpec072Wrote), MaskAsSpec072Wrote,
            nameof(MaskAsSpec130Wrote), MaskAsSpec130Wrote);
    }

    /// <summary>
    /// Tasks.md T13 — the proof T14-T18 rely on: <c>SourceMask.Apply</c> under
    /// each of the four strictnesses returns a char-for-char identical string to
    /// the frozen pre-refactor masker it replaces, over every file under
    /// <c>src/*/Api</c>. Once this is green the six guards can be repointed
    /// without re-deriving the claim per guard.
    /// </summary>
    [Fact]
    public void The_shared_SourceMask_matches_every_frozen_masker_char_for_char_on_every_api_source_file()
    {
        AssertIdenticalAcrossRealCorpus(
            nameof(MaskAsSpec072Wrote), MaskAsSpec072Wrote,
            "SourceMask.Apply(CommentsAndLiteralInteriors)",
            text => SourceMask.Apply(text, MaskStrictness.CommentsAndLiteralInteriors));

        AssertIdenticalAcrossRealCorpus(
            nameof(MaskAsSpec075Wrote), MaskAsSpec075Wrote,
            "SourceMask.Apply(CommentsOnlyLiteralsIntact)",
            text => SourceMask.Apply(text, MaskStrictness.CommentsOnlyLiteralsIntact));

        AssertIdenticalAcrossRealCorpus(
            nameof(BlankCommentsAsSpec070Wrote), BlankCommentsAsSpec070Wrote,
            "SourceMask.Apply(CommentsBlankedLiteralsIntact)",
            text => SourceMask.Apply(text, MaskStrictness.CommentsBlankedLiteralsIntact));

        AssertIdenticalAcrossRealCorpus(
            "BlankLiteralsAsSpec070Wrote(BlankCommentsAsSpec070Wrote(text))",
            text => BlankLiteralsAsSpec070Wrote(BlankCommentsAsSpec070Wrote(text)),
            "SourceMask.Apply(…, LiteralInteriorsOnly) after SourceMask.Apply(…, CommentsBlankedLiteralsIntact)",
            text => SourceMask.Apply(
                SourceMask.Apply(text, MaskStrictness.CommentsBlankedLiteralsIntact),
                MaskStrictness.LiteralInteriorsOnly));
    }

    /// <summary>
    /// The three identical <c>NotFound</c>/already-masked chain-end walks
    /// (<c>PreconditionDeclarationTests</c>, <c>RouteValueRefusalDeclarationTests</c>,
    /// <c>StatusProducerDeclarationTests</c>) agree at every mapping call site in
    /// the real corpus, walking the <c>CommentsAndLiteralInteriors</c>-masked
    /// text the way all three guards actually call it.
    /// </summary>
    [Fact]
    public void The_three_byte_identical_StatementEnd_walks_already_agree_at_every_mapping_call_in_the_real_corpus()
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in ApiSourceFiles(root))
        {
            string masked = MaskAsSpec072Wrote(ReadAsGuardsDo(root, file));
            foreach (int from in MappingCallEnds(masked, MappingCall))
            {
                int a = StatementEndAsSpec072Wrote(masked, from);
                int b = StatementEndAsSpec091Wrote(masked, from);
                int c = StatementEndAsSpec130Wrote(masked, from);

                (a == b && b == c).ShouldBeTrue(
                    $"StatementEnd walks disagree in {file} from offset {from}: "
                    + $"072={a}, 091={b}, 130={c}.");
            }
        }
    }

    /// <summary>
    /// The two-stage masker preserves length and does not throw on every real
    /// file — a corruption smoke test, not an agreement claim: whether stage-two
    /// -on-stage-one ever equals the one-pass <c>CommentsAndLiteralInteriors</c>
    /// masker on real code is exactly what spec 190's assumption A4 records as
    /// unverified, and this test does not assert it either way.
    /// </summary>
    [Fact]
    public void The_two_stage_masker_preserves_length_on_every_api_source_file()
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in ApiSourceFiles(root))
        {
            string text = ReadAsGuardsDo(root, file);
            string stageOne = BlankCommentsAsSpec070Wrote(text);
            string stageTwo = BlankLiteralsAsSpec070Wrote(stageOne);

            stageOne.Length.ShouldBe(text.Length, $"{file}: stage one changed length");
            stageTwo.Length.ShouldBe(text.Length, $"{file}: stage two changed length");
        }
    }

    /// <summary>
    /// The literal-aware chain-end walk (<c>ConcurrencyConflictDeclarationTests</c>,
    /// spec 075) never runs past the end of its own input on real code — a
    /// smoke test guarding against the exact regression issue #2183 was: a walk
    /// whose bracket depth never returns to zero would return <c>-1</c> here for
    /// a chain that plainly has a terminating semicolon.
    /// </summary>
    [Fact]
    public void The_literal_aware_StatementEnd_walk_finds_a_terminator_for_every_mutating_mapping_in_the_real_corpus()
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in ApiSourceFiles(root))
        {
            string masked = MaskAsSpec075Wrote(ReadAsGuardsDo(root, file));
            foreach (int from in MappingCallEnds(masked, MutatingMappingCall))
            {
                int end = StatementEndAsSpec075Wrote(masked, from);
                end.ShouldBeGreaterThanOrEqualTo(
                    from, $"{file}: literal-aware StatementEnd from {from} returned {end}");
            }
        }
    }

    /// <summary>
    /// The <c>EndOfText</c>-sentinel chain-end walk
    /// (<c>EndpointScopeDeclarationTests</c>, spec 070/085) never returns
    /// <c>-1</c> on real code — the whole reason it exists is that
    /// <c>masked[x..(-1)]</c> throws at its call sites, so this is the guard
    /// against silently reintroducing that.
    /// </summary>
    [Fact]
    public void The_end_of_text_StatementEnd_walk_never_returns_the_not_found_sentinel_on_the_real_corpus()
    {
        DirectoryInfo root = RepositoryRoot();
        foreach (string file in ApiSourceFiles(root))
        {
            string stageOne = BlankCommentsAsSpec070Wrote(ReadAsGuardsDo(root, file));
            string masked = BlankLiteralsAsSpec070Wrote(stageOne);
            foreach (int from in MappingCallEnds(masked, MappingCall))
            {
                StatementEndAsSpec070Wrote(masked, from).ShouldNotBe(-1, $"{file} from offset {from}");
            }
        }
    }

    // =====================================================================
    // M1 — the fixture golden test (permanent; plan.md §7, tasks.md T2).
    //
    // Written against SourceMask — the NEW shared type — before it exists.
    // This is deliberate: M1 is the contract test for the abstraction phase
    // 4b is about to build, not a characterisation of code that already
    // works. Until tasks.md T12 lands SourceMask.cs, this region does not
    // compile: the compiler names SourceMask and MaskStrictness as unknown.
    // That is the concrete red state phase 4b turns green — it is not a
    // defect in this file, and no stub belongs here (the phase split
    // forbids the test-writer phase from also writing the code it
    // characterises).
    //
    // Every expected value below was computed independently (a small
    // byte-for-byte mirror of each guard's current masker, run once outside
    // this repository) and is written out literally, per fixture per
    // strictness — not hashed, so a reader sees exactly which character
    // moved if the shared implementation ever disagrees with these.
    // =====================================================================


    [Fact]
    public void The_LineComment_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.LineComment, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler)                                       \n    .WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.LineComment, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler)                                       \n    .WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.LineComment, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler)                                       \n    .WithSummary(\"  \");");

        SourceMask.Apply(SourceScanFixtures.LineComment, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler)                                       \n    .WithSummary(\"  \");");
    }

    [Fact]
    public void The_BlockComment_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.BlockComment, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler)\n            \n               \n    .WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.BlockComment, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler)\n            \n               \n    .WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.BlockComment, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapPost(\"  \", Handler)\n            \n               \n    .WithSummary(\"  \");");

        SourceMask.Apply(SourceScanFixtures.BlockComment, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapPost(\"  \", Handler)\n            \n               \n    .WithSummary(\"  \");");
    }

    [Fact]
    public void The_SemicolonInSummary_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.SemicolonInSummary, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).WithSummary(\"does x; then y\");");

        SourceMask.Apply(SourceScanFixtures.SemicolonInSummary, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).WithSummary(\"does x; then y\");");

        SourceMask.Apply(SourceScanFixtures.SemicolonInSummary, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapPost(\"  \", Handler).WithSummary(\"              \");");

        SourceMask.Apply(SourceScanFixtures.SemicolonInSummary, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapPost(\"  \", Handler).WithSummary(\"              \");");
    }

    [Fact]
    public void The_UnbalancedParenInSummary_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.UnbalancedParenInSummary, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).WithSummary(\"see foo(\");");

        SourceMask.Apply(SourceScanFixtures.UnbalancedParenInSummary, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).WithSummary(\"see foo(\");");

        SourceMask.Apply(SourceScanFixtures.UnbalancedParenInSummary, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapPost(\"  \", Handler).WithSummary(\"        \");");

        SourceMask.Apply(SourceScanFixtures.UnbalancedParenInSummary, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapPost(\"  \", Handler).WithSummary(\"        \");");
    }

    [Fact]
    public void The_VerbatimString_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.VerbatimString, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(@\"a \"\"quoted\"\" path\");");

        SourceMask.Apply(SourceScanFixtures.VerbatimString, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(@\"a \"\"quoted\"\" path\");");

        SourceMask.Apply(SourceScanFixtures.VerbatimString, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(@\"                 \");");

        SourceMask.Apply(SourceScanFixtures.VerbatimString, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(@\"                 \");");
    }

    [Fact]
    public void The_EscapedQuote_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.EscapedQuote, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(\"he said \\\"no\\\"\");");

        SourceMask.Apply(SourceScanFixtures.EscapedQuote, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(\"he said \\\"no\\\"\");");

        SourceMask.Apply(SourceScanFixtures.EscapedQuote, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(\"              \");");

        SourceMask.Apply(SourceScanFixtures.EscapedQuote, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(\"              \");");
    }

    [Fact]
    public void The_QuoteAsCharLiteral_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.QuoteAsCharLiteral, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler.Split('\"'));");

        SourceMask.Apply(SourceScanFixtures.QuoteAsCharLiteral, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler.Split('\"'));");

        SourceMask.Apply(SourceScanFixtures.QuoteAsCharLiteral, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler.Split(' '));");

        SourceMask.Apply(SourceScanFixtures.QuoteAsCharLiteral, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler.Split(' '));");
    }

    [Fact]
    public void The_OpenParenAsCharLiteral_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.OpenParenAsCharLiteral, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler.IndexOf('('));");

        SourceMask.Apply(SourceScanFixtures.OpenParenAsCharLiteral, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler.IndexOf('('));");

        SourceMask.Apply(SourceScanFixtures.OpenParenAsCharLiteral, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler.IndexOf(' '));");

        SourceMask.Apply(SourceScanFixtures.OpenParenAsCharLiteral, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler.IndexOf(' '));");
    }

    [Fact]
    public void The_RawStringLiteral_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.RawStringLiteral, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(\"\"\"raw \"quoted\" text\"\"\");");

        SourceMask.Apply(SourceScanFixtures.RawStringLiteral, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapGet(\"/x\", Handler).WithSummary(\"\"\"raw \"quoted\" text\"\"\");");

        SourceMask.Apply(SourceScanFixtures.RawStringLiteral, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(\"\"\"    \"quoted\"     \"\"\");");

        SourceMask.Apply(SourceScanFixtures.RawStringLiteral, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapGet(\"  \", Handler).WithSummary(\"\"\"    \"quoted\"     \"\"\");");
    }

    [Fact]
    public void The_StatementBodiedLambdaInChain_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.StatementBodiedLambdaInChain, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); }).WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.StatementBodiedLambdaInChain, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("app.MapPost(\"/x\", Handler).AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); }).WithSummary(\"ok\");");

        SourceMask.Apply(SourceScanFixtures.StatementBodiedLambdaInChain, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("app.MapPost(\"  \", Handler).AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); }).WithSummary(\"  \");");

        SourceMask.Apply(SourceScanFixtures.StatementBodiedLambdaInChain, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("app.MapPost(\"  \", Handler).AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); }).WithSummary(\"  \");");
    }

    [Fact]
    public void The_ExpressionBodiedHandler_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.ExpressionBodiedHandler, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("private static IResult Get(HttpContext context) => Results.Ok();");

        SourceMask.Apply(SourceScanFixtures.ExpressionBodiedHandler, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("private static IResult Get(HttpContext context) => Results.Ok();");

        SourceMask.Apply(SourceScanFixtures.ExpressionBodiedHandler, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("private static IResult Get(HttpContext context) => Results.Ok();");

        SourceMask.Apply(SourceScanFixtures.ExpressionBodiedHandler, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("private static IResult Get(HttpContext context) => Results.Ok();");
    }

    [Fact]
    public void The_HandlerInSecondPartialFileFirstFile_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileFirstFile, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static IResult Get(HttpContext context) => Results.Ok();\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileFirstFile, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static IResult Get(HttpContext context) => Results.Ok();\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileFirstFile, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static IResult Get(HttpContext context) => Results.Ok();\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileFirstFile, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static IResult Get(HttpContext context) => Results.Ok();\n}");
    }

    [Fact]
    public void The_HandlerInSecondPartialFileSecondFile_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileSecondFile, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static void Handle(HttpContext context)\n    {\n        context.Response.StatusCode = 200;\n    }\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileSecondFile, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static void Handle(HttpContext context)\n    {\n        context.Response.StatusCode = 200;\n    }\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileSecondFile, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static void Handle(HttpContext context)\n    {\n        context.Response.StatusCode = 200;\n    }\n}");

        SourceMask.Apply(SourceScanFixtures.HandlerInSecondPartialFileSecondFile, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("public sealed partial class WidgetEndpoints\n{\n    private static void Handle(HttpContext context)\n    {\n        context.Response.StatusCode = 200;\n    }\n}");
    }

    [Fact]
    public void The_MapGroupChain_fixture_masks_as_expected_under_every_strictness()
    {
        SourceMask.Apply(SourceScanFixtures.MapGroupChain, MaskStrictness.CommentsOnlyLiteralsIntact)
            .ShouldBe("var g = app.MapGroup(\"/x\").RequireAuthorization();");

        SourceMask.Apply(SourceScanFixtures.MapGroupChain, MaskStrictness.CommentsBlankedLiteralsIntact)
            .ShouldBe("var g = app.MapGroup(\"/x\").RequireAuthorization();");

        SourceMask.Apply(SourceScanFixtures.MapGroupChain, MaskStrictness.LiteralInteriorsOnly)
            .ShouldBe("var g = app.MapGroup(\"  \").RequireAuthorization();");

        SourceMask.Apply(SourceScanFixtures.MapGroupChain, MaskStrictness.CommentsAndLiteralInteriors)
            .ShouldBe("var g = app.MapGroup(\"  \").RequireAuthorization();");
    }

    /// <summary>
    /// AS-2's second half, and the part a naive characterisation would omit:
    /// the three masker strictnesses do not merely happen to agree on today's
    /// corpus — on these three fixtures they positively disagree. A test that
    /// only asserted agreement would pass just as well against a masker
    /// someone had already collapsed to one behaviour (spec 190 §1.3, §9's
    /// first risk row).
    /// </summary>
    [Fact]
    public void The_three_masker_strictnesses_disagree_on_the_verbatim_string_the_escaped_quote_and_the_char_literal_quote()
    {
        AssertStrictnessesDisagree(SourceScanFixtures.VerbatimString);
        AssertStrictnessesDisagree(SourceScanFixtures.EscapedQuote);
        AssertStrictnessesDisagree(SourceScanFixtures.QuoteAsCharLiteral);
    }

    /// <summary>
    /// The conceptual "three behaviours" of spec.md §1.3: the weakest mode
    /// (<see cref="MaskStrictness.CommentsOnlyLiteralsIntact"/>), the one-pass
    /// mode (<see cref="MaskStrictness.CommentsAndLiteralInteriors"/>), and the
    /// two-stage mode composed the way <c>EndpointScopeDeclarationTests</c>
    /// actually calls it
    /// (<see cref="MaskStrictness.CommentsBlankedLiteralsIntact"/> then
    /// <see cref="MaskStrictness.LiteralInteriorsOnly"/>). Not all three need
    /// to differ from each other — only that they do not all collapse to one
    /// output.
    /// </summary>
    private static void AssertStrictnessesDisagree(string fixture)
    {
        string weakest = SourceMask.Apply(fixture, MaskStrictness.CommentsOnlyLiteralsIntact);
        string onePass = SourceMask.Apply(fixture, MaskStrictness.CommentsAndLiteralInteriors);
        string twoStage = SourceMask.Apply(
            SourceMask.Apply(fixture, MaskStrictness.CommentsBlankedLiteralsIntact),
            MaskStrictness.LiteralInteriorsOnly);

        HashSet<string> outputs = [weakest, onePass, twoStage];
        outputs.Count.ShouldBeGreaterThan(
            1, $"the three strictnesses all produced the same output for '{fixture}' — one of them has been collapsed.");
    }
}
