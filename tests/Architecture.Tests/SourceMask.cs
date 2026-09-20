namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// What a masker blanks. There is no default: the four behaviours below
/// disagree on real forms (a verbatim string, an escaped quote, a quote written
/// as a char literal), and picking one by inheritance is how a guard silently
/// widens or narrows what it sees. State the choice at the call site
/// (spec 190, issue #2257).
///
/// <para>NONE of the four understands a raw string literal (three quotes).
/// Teaching one is a behaviour change and needs its own issue
/// (specs/190-one-reader-the-next-guard-finds/spec.md §7).</para>
/// </summary>
internal enum MaskStrictness
{
    /// <summary>
    /// Comments blanked; literal CONTENT LEFT INTACT. Steps over a plain
    /// <c>"…"</c> only so a <c>//</c> inside one is not read as a comment. Does
    /// NOT understand <c>@"…"</c>, a backslash escape, or a char literal — the
    /// caller must assert those forms are absent from its corpus, as
    /// <c>ConcurrencyConflictDeclarationTests</c> does
    /// (<c>The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask</c>).
    ///
    /// <para>Used by <c>ConcurrencyConflictDeclarationTests</c> (spec 075),
    /// which reads route literals and <c>MapGroup</c> prefixes straight off the
    /// masked text. Giving it a stronger mode blanks the very content it
    /// reads.</para>
    /// </summary>
    CommentsOnlyLiteralsIntact,

    /// <summary>
    /// Comments blanked; literals SKIPPED WHOLE, content intact. Understands
    /// <c>@"…"</c> (doubled-quote escape), <c>'…'</c> and backslash escapes.
    ///
    /// <para>Stage one of <c>EndpointScopeDeclarationTests</c>' two-stage reader
    /// (specs 070/085). That guard needs both stages: structure is searched on
    /// the fully masked text, and literal contents — a scope constant, a
    /// summary sentence — are read from this one at the same offsets.</para>
    /// </summary>
    CommentsBlankedLiteralsIntact,

    /// <summary>
    /// Literal INTERIORS blanked, delimiters and length kept. Comments are NOT
    /// touched — this is stage two, applied to
    /// <see cref="CommentsBlankedLiteralsIntact"/>'s output. Used by
    /// <c>EndpointScopeDeclarationTests</c> only.
    /// </summary>
    LiteralInteriorsOnly,

    /// <summary>
    /// One pass: comments blanked AND literal interiors blanked, delimiters and
    /// length kept so every offset still names the same character. Understands
    /// <c>@"…"</c>, <c>'…'</c> and backslash escapes.
    ///
    /// <para>Used by <c>PreconditionDeclarationTests</c> (072),
    /// <c>RouteValueRefusalDeclarationTests</c> (091) and
    /// <c>StatusProducerDeclarationTests</c> (130), whose three copies were
    /// byte-identical.</para>
    ///
    /// <para>This is NOT the same as applying
    /// <see cref="LiteralInteriorsOnly"/> after
    /// <see cref="CommentsBlankedLiteralsIntact"/>: the two were written
    /// separately, walk literals differently, and are kept separate for that
    /// reason rather than because anyone has proved they diverge.</para>
    /// </summary>
    CommentsAndLiteralInteriors,
}

/// <summary>
/// The masker three specs each wrote a copy of before this one, unified as the
/// choice they always were rather than the accident their agreement looked like
/// (spec 190, issue #2257). Length is always preserved and newlines are always
/// kept, so every index, offset and line number a caller already computed still
/// refers to the same character of the original text.
/// </summary>
internal static class SourceMask
{
    /// <summary>
    /// <c>ConcurrencyConflictDeclarationTests.UnmaskableLiteralForms</c> (spec
    /// 075), moved here verbatim as data. The assertion that the corpus is free
    /// of them stays in that guard's own file — which corpus must avoid them is
    /// the guard's business, not the masker's.
    /// </summary>
    private static readonly (string Form, string Why)[] CommentsOnlyLiteralsIntactUnhandled =
    [
        ("@\"", "a verbatim string, in which a doubled quote closes nothing"),
        ("\"\"\"", "a raw string literal, whose delimiter is longer than one quote"),
        ("\\\"", "an escaped quote, which this reader steps over but which changes where a literal ends"),
        ("'\"'", "a quote as a char literal, which would open a string that never closes"),
    ];

    /// <summary>
    /// The one form every other strictness fails to read, named in prose by each
    /// of their guards but never asserted until now.
    /// </summary>
    private static readonly (string Form, string Why)[] RawStringLiteralOnlyUnhandled =
    [
        ("\"\"\"", "a raw string literal, whose delimiter is longer than one quote"),
    ];

    /// <summary>
    /// The text with <paramref name="strictness"/> applied.
    /// </summary>
    internal static string Apply(string text, MaskStrictness strictness) => strictness switch
    {
        MaskStrictness.CommentsOnlyLiteralsIntact => CommentsOnlyLiteralsIntactMask(text),
        MaskStrictness.CommentsBlankedLiteralsIntact => CommentsBlankedLiteralsIntactMask(text),
        MaskStrictness.LiteralInteriorsOnly => LiteralInteriorsOnlyMask(text),
        MaskStrictness.CommentsAndLiteralInteriors => CommentsAndLiteralInteriorsMask(text),
        _ => throw new ArgumentOutOfRangeException(nameof(strictness), strictness, "no such mask strictness"),
    };

    /// <summary>
    /// The literal forms <paramref name="strictness"/> cannot read, each with a
    /// one-line reason. Shared as DATA so the next guard inherits the list; the
    /// assertion stays in the guard that owns the corpus.
    /// </summary>
    internal static IReadOnlyList<(string Form, string Why)> UnhandledForms(MaskStrictness strictness) =>
        strictness == MaskStrictness.CommentsOnlyLiteralsIntact
            ? CommentsOnlyLiteralsIntactUnhandled
            : RawStringLiteralOnlyUnhandled;

    // =====================================================================
    // CommentsAndLiteralInteriors — PreconditionDeclarationTests (072),
    // RouteValueRefusalDeclarationTests (091), StatusProducerDeclarationTests
    // (130). Byte-identical in all three before this extraction.
    // =====================================================================

    private static string CommentsAndLiteralInteriorsMask(string text)
    {
        char[] masked = text.ToCharArray();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && Next(text, i) == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    masked[i++] = ' ';
                }
            }
            else if (text[i] == '/' && Next(text, i) == '*')
            {
                i = MaskBlockComment(text, masked, i);
            }
            else if (text[i] == '@' && Next(text, i) == '"')
            {
                i = MaskVerbatim(text, masked, i);
            }
            else if (text[i] is '"' or '\'')
            {
                i = MaskLiteral(text, masked, i);
            }
            else
            {
                i++;
            }
        }

        return new string(masked);
    }

    private static int MaskBlockComment(string text, char[] masked, int from)
    {
        int i = from;
        while (i < text.Length && !(text[i] == '*' && Next(text, i) == '/'))
        {
            masked[i] = text[i] == '\n' ? '\n' : ' ';
            i++;
        }

        return Blank(masked, i, 2);
    }

    private static int MaskVerbatim(string text, char[] masked, int from)
    {
        int i = from + 2;
        while (i < text.Length)
        {
            if (text[i] == '"' && Next(text, i) == '"')
            {
                masked[i] = ' ';
                masked[i + 1] = ' ';
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                return i + 1;
            }

            masked[i] = text[i] == '\n' ? '\n' : ' ';
            i++;
        }

        return i;
    }

    private static int MaskLiteral(string text, char[] masked, int from)
    {
        char quote = text[from];
        int i = from + 1;
        while (i < text.Length && text[i] != quote && text[i] != '\n')
        {
            masked[i] = ' ';
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                masked[i + 1] = ' ';
                i++;
            }

            i++;
        }

        return i + 1;
    }

    private static int Blank(char[] masked, int from, int count)
    {
        for (int i = from; i < from + count && i < masked.Length; i++)
        {
            masked[i] = ' ';
        }

        return from + count;
    }

    // =====================================================================
    // CommentsOnlyLiteralsIntact — ConcurrencyConflictDeclarationTests
    // (075)'s MaskComments.
    // =====================================================================

    private static string CommentsOnlyLiteralsIntactMask(string text)
    {
        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                index = EndOfStringLiteralCommentsOnly(text, index) + 1;
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
                index = BlankBlockCommentCommentsOnly(text, masked, index) + 1;
            }
            else
            {
                index++;
            }
        }

        return new string(masked);
    }

    private static int EndOfStringLiteralCommentsOnly(string t, int open)
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

    private static int BlankBlockCommentCommentsOnly(string t, char[] masked, int start)
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

    private static bool Starts(string t, int index, string token) =>
        index + token.Length <= t.Length
        && string.CompareOrdinal(t, index, token, 0, token.Length) == 0;

    // =====================================================================
    // CommentsBlankedLiteralsIntact / LiteralInteriorsOnly —
    // EndpointScopeDeclarationTests' two-stage reader (070/085):
    // WithoutComments then MaskLiterals, sharing one EndOfLiteral.
    // =====================================================================

    private static string CommentsBlankedLiteralsIntactMask(string source)
    {
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

    private static string LiteralInteriorsOnlyMask(string text)
    {
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

    private static int EndOfLiteral(string t, int start, bool verbatim)
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

    private static char Next(string t, int i) => i + 1 < t.Length ? t[i + 1] : '\0';
}
