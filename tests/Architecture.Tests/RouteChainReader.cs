using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// What the walk returns when a statement has no terminating semicolon. There
/// is no default. The two answers are not interchangeable.
/// </summary>
internal enum ChainEndSentinel
{
    /// <summary>
    /// <c>-1</c>. <c>PreconditionDeclarationTests</c>,
    /// <c>RouteValueRefusalDeclarationTests</c>,
    /// <c>StatusProducerDeclarationTests</c> and
    /// <c>ConcurrencyConflictDeclarationTests</c> — every one of their call
    /// sites checks for it.
    /// </summary>
    NotFound,

    /// <summary>
    /// <c>masked.Length</c>. <c>EndpointScopeDeclarationTests</c> only, and
    /// deliberately: its three call sites use the result DIRECTLY as a slice
    /// bound, and <c>masked[x..(-1)]</c> lowers to <c>Substring(x, -1)</c>,
    /// which throws naming the <c>length</c> parameter, not the index.
    /// <c>masked[x..masked.Length]</c> is a valid identity slice. Do not "fix"
    /// this to <see cref="NotFound"/> to match the siblings; that reintroduces
    /// the throw at every call site the moment a chain has no trailing
    /// semicolon.
    /// </summary>
    EndOfText,
}

/// <summary>Whether the walk must step over literals itself.</summary>
internal enum ChainLiteralHandling
{
    /// <summary>
    /// The text is already literal-masked, so a bracket inside a literal cannot
    /// exist. Pairs with <see cref="MaskStrictness.CommentsAndLiteralInteriors"/>
    /// and with <c>EndpointScope</c>'s stage two.
    /// </summary>
    AlreadyMasked,

    /// <summary>
    /// The text carries live literals — it pairs with
    /// <see cref="MaskStrictness.CommentsOnlyLiteralsIntact"/>. String AND char
    /// literals are stepped over while bracket depth is counted.
    ///
    /// <para>Both halves were real holes, reached separately: an unbalanced
    /// <c>(</c> inside a <c>WithSummary("…")</c>, and one written as the char
    /// literal <c>'('</c>. Each left depth permanently positive, so the chain
    /// ran past its own <c>;</c> into the next mapping and inherited whatever
    /// that one declared — a route could be made to look compliant by
    /// borrowing its neighbour's 409. The char literal was found only after
    /// the string fix was believed to have closed it. Issue #2183.</para>
    /// </summary>
    StepOverStringAndCharLiterals,
}

/// <summary>
/// The <c>Map…</c> chain reader five of the six spec 190 guards each carried a
/// copy of, unified at the two axes that actually differ — the not-found
/// sentinel and whether literals must be stepped over — rather than at the
/// third axis the issue names (how far out the antecedent is resolved), which
/// stays each guard's own composition over these primitives
/// (specs/190-one-reader-the-next-guard-finds/plan.md §3.3).
///
/// <para>
/// <b>The group-of-a-mapping resolver is NOT here.</b> Plan §3.3 makes its
/// extraction conditional on <c>EndpointScopeDeclarationTests</c>' and
/// <c>StatusProducerDeclarationTests</c>' walks proving byte-identical (task
/// T27). They do not: <c>EndpointScopeDeclarationTests</c> resolves a group
/// from any <c>var</c>/<c>RouteGroupBuilder</c> local declaration whose
/// statement contains a <c>MapGroup</c> call, refuses a name declared twice,
/// and separately registers routes mapped outside the readable chain shapes.
/// <c>StatusProducerDeclarationTests</c> resolves a group with one regex
/// anchored to <c>&lt;variable&gt; = &lt;builder&gt;.MapGroup("…")</c> and does
/// none of that bookkeeping. Different shapes, different failure modes — both
/// stay local to their own guard.
/// </para>
/// </summary>
internal static class RouteChainReader
{
    private static readonly Regex MethodGroupName = new(
        @"^[A-Za-z_]\w*$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The index of the semicolon ending the statement that starts at
    /// <paramref name="from"/>, ignoring semicolons nested inside brackets — a
    /// chain may carry a lambda or a collection initialiser.
    /// </summary>
    internal static int StatementEnd(
        string text, int from, ChainEndSentinel sentinel, ChainLiteralHandling literals) =>
        literals == ChainLiteralHandling.StepOverStringAndCharLiterals
            ? LiteralAwareStatementEnd(text, from, sentinel)
            : AlreadyMaskedStatementEnd(text, from, sentinel);

    private static int AlreadyMaskedStatementEnd(string masked, int from, ChainEndSentinel sentinel)
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

        return sentinel == ChainEndSentinel.EndOfText ? masked.Length : -1;
    }

    private static int LiteralAwareStatementEnd(string text, int from, ChainEndSentinel sentinel)
    {
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

        return sentinel == ChainEndSentinel.EndOfText ? text.Length : -1;
    }

    private static int EndOfStringLiteral(string t, int open)
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

    private static int EndOfCharLiteral(string t, int open)
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

    /// <summary>
    /// The index of the delimiter matching the one at
    /// <paramref name="openIndex"/>, or -1.
    /// </summary>
    internal static int Balanced(string text, int openIndex, char open, char close)
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

    /// <summary>
    /// The half-open spans of the top-level arguments between
    /// <paramref name="from"/> and <paramref name="close"/>.
    /// </summary>
    internal static List<(int Start, int End)> SplitArguments(string masked, int from, int close)
    {
        List<(int Start, int End)> arguments = [];
        int depth = 0;
        int start = from;
        for (int i = from; i < close; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' or '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                arguments.Add((start, i));
                start = i + 1;
            }
        }

        if (close > start)
        {
            arguments.Add((start, close));
        }

        return arguments;
    }

    internal static string Unquote(string value) =>
        value.Length > 1 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    internal static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Resolves the handler a mapping names: a bare method group searched
    /// across every file declaring that partial class within the same
    /// <c>src/&lt;Context&gt;/Api</c> project.
    ///
    /// <para>
    /// Byte-identical in <c>PreconditionDeclarationTests</c> (072) and
    /// <c>RouteValueRefusalDeclarationTests</c> (091) today, folding what each
    /// called <c>MethodBodies</c> + <c>BodyAfter</c> plus the "exactly one
    /// candidate" check each guard's own <c>Resolve</c> made around them. This
    /// method resolves; it does not word a failure. Each guard keeps its own
    /// three-shape failure message — "not a bare method-group name", "resolves
    /// to no method declaration", "resolves to N method declarations" — over
    /// <see cref="HandlerResolution"/>'s fields, because that wording (and, for
    /// the ambiguous case, <c>Ellipsis</c>) is what each guard's own tests pin,
    /// and is currently unreached on this corpus.
    /// </para>
    /// </summary>
    internal static HandlerResolution HandlerBodyFor(
        string containingClass,
        string handlerArgument,
        string file,
        IReadOnlyList<ClassSpan> classes,
        IReadOnlyDictionary<string, string> masked)
    {
        if (!MethodGroupName.IsMatch(handlerArgument))
        {
            return new HandlerResolution(null, false, []);
        }

        string project = ProjectOf(file);
        List<HandlerBody> candidates = classes
            .Where(c => string.Equals(c.Name, containingClass, StringComparison.Ordinal)
                && string.Equals(ProjectOf(c.File), project, StringComparison.Ordinal))
            .SelectMany(c => MethodBodies(c, masked[c.File], handlerArgument))
            .ToList();

        return new HandlerResolution(
            candidates.Count == 1 ? candidates[0] : null, true, candidates);
    }

    /// <summary>
    /// The Api project a file belongs to — <c>src/&lt;Context&gt;/Api</c>. Two
    /// contexts may each declare a class of the same name, and Identity
    /// declares three <c>List</c> handlers in three classes of its own.
    /// </summary>
    private static string ProjectOf(string file)
    {
        int marker = file.IndexOf("/Api/", StringComparison.Ordinal);
        return marker < 0 ? file : file[..(marker + 4)];
    }

    /// <summary>
    /// Every method of the given name declared directly in one class body. The
    /// leading accessibility keyword is what separates a declaration from a
    /// call site: a call has no modifier between it and the punctuation before
    /// it.
    /// </summary>
    private static IEnumerable<HandlerBody> MethodBodies(ClassSpan span, string masked, string name)
    {
        Regex declaration = new(
            @"(?<!\w)(?:private|public|internal|protected)[^;{}()\n]*?\b" + Regex.Escape(name) + @"\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        foreach (Match match in declaration.Matches(masked))
        {
            if (match.Index <= span.Start || match.Index >= span.End)
            {
                continue;
            }

            int close = Balanced(masked, match.Index + match.Length - 1, '(', ')');
            if (close < 0)
            {
                continue;
            }

            HandlerBody? body = BodyAfter(span.File, masked, match.Index, close);
            if (body is not null)
            {
                yield return body;
            }
        }
    }

    /// <summary>
    /// The body of a method whose parameter list ends at
    /// <paramref name="close"/> — block or expression-bodied. A declaration
    /// with neither has no body to read and is not a candidate.
    /// </summary>
    private static HandlerBody? BodyAfter(string file, string masked, int declaration, int close)
    {
        int brace = masked.IndexOf('{', close + 1);
        int semicolon = masked.IndexOf(';', close + 1);
        int arrow = masked.IndexOf("=>", close + 1, StringComparison.Ordinal);
        int line = LineOf(masked, declaration);

        if (brace >= 0 && (semicolon < 0 || brace < semicolon) && (arrow < 0 || brace < arrow))
        {
            int end = Balanced(masked, brace, '{', '}');
            return end < 0 ? null : new HandlerBody(file, line, brace + 1, masked[(brace + 1)..end]);
        }

        if (arrow >= 0 && (semicolon < 0 || arrow < semicolon))
        {
            int end = StatementEnd(masked, arrow + 2, ChainEndSentinel.NotFound, ChainLiteralHandling.AlreadyMasked);
            return end < 0 ? null : new HandlerBody(file, line, arrow + 2, masked[(arrow + 2)..end]);
        }

        return null;
    }

    internal sealed record HandlerBody(string File, int Line, int BodyStart, string Body);

    /// <summary>
    /// What <see cref="HandlerBodyFor"/> found. <see cref="Body"/> is set only
    /// when resolution succeeded (exactly one candidate); otherwise the caller
    /// distinguishes its own three failure shapes from
    /// <see cref="IsBareMethodGroupName"/> and <see cref="Candidates"/>' count:
    /// <c>false</c> means the argument never reached candidate search at all,
    /// an empty <see cref="Candidates"/> means it did and found none, and two
    /// or more means it found too many — each of those is one of the three
    /// messages a guard's own tests pin, worded by the guard, not here.
    /// </summary>
    internal sealed record HandlerResolution(
        HandlerBody? Body,
        bool IsBareMethodGroupName,
        IReadOnlyList<HandlerBody> Candidates);

    /// <summary>
    /// Every class declaration in one file, with the extent of its body. A
    /// nested class works out because the innermost containing span is chosen —
    /// finding these spans stays each guard's own <c>ClassSpans</c> (identical
    /// between guards 1 and 4, but not part of this shared surface: plan §3.3
    /// stops the shared surface at the primitives).
    /// </summary>
    internal sealed record ClassSpan(string File, string Name, int Start, int End);
}
