namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The corpus spec 190 (issue #2257) builds the shared masker and chain reader
/// against, and the executable version of spec.md §1.3's disagreement table —
/// this is what the seventh guard's author reads, not the prose.
///
/// <para>
/// Thirteen cases, one per row of plan.md §6. Each is an <c>internal const
/// string</c> raw string literal (ADR-0054: hand-written fixtures, no
/// AutoFixture; spec 190 §5: in-file raw string literals, no new csproj item).
/// None of these fixtures is valid enough C# to compile as a member — they are
/// text the masker and the chain reader read as bytes, never as code the
/// compiler sees as anything but a string.
/// </para>
///
/// <para>
/// <b>The delimiter trap.</b> <see cref="RawStringLiteral"/>'s content itself
/// contains a three-quote raw-string delimiter (<c>"""</c>), so wrapping it in a
/// three-quote delimiter here would close early on the first <c>"""</c> it
/// contains. It needs a <em>four</em>-quote delimiter (<c>""""</c>). Do not
/// "simplify" it back to three quotes — that is a compile error, not a style
/// choice.
/// </para>
/// </summary>
internal static class SourceScanFixtures
{
    /// <summary>
    /// A line comment after a mapping call, before the chain's <c>;</c>. All
    /// three masker strictnesses blank the comment; they differ only on the two
    /// literals it also contains — see <see cref="EscapedQuote"/> and friends for
    /// where that split actually shows up.
    /// </summary>
    internal const string LineComment =
        """"
        app.MapGet("/x", Handler) // .ProducesProblem(Status409Conflict)
            .WithSummary("ok");
        """";

    /// <summary>A block comment spanning lines; newlines must survive masking so line numbers stay true.</summary>
    internal const string BlockComment =
        """"
        app.MapPost("/x", Handler)
            /* spans
               lines */
            .WithSummary("ok");
        """";

    /// <summary>
    /// A semicolon inside a string literal. The three strictnesses agree in
    /// effect (none of them lets this semicolon end the statement — two because
    /// they blank it, one because it reads it as ordinary text) but their masked
    /// <em>output</em> differs, which is why this fixture is asserted per
    /// strictness rather than assumed identical.
    /// </summary>
    internal const string SemicolonInSummary =
        """"
        app.MapPost("/x", Handler).WithSummary("does x; then y");
        """";

    /// <summary>
    /// An unbalanced <c>(</c> inside a string literal — half of issue #2183's
    /// hole. A masker that blanks the interior removes it from the bracket-depth
    /// count entirely; a masker that leaves the literal intact hands it straight
    /// to a depth-counting walk that has no way to know it is not real code.
    /// </summary>
    internal const string UnbalancedParenInSummary =
        """"
        app.MapPost("/x", Handler).WithSummary("see foo(");
        """";

    /// <summary>
    /// A verbatim string with a doubled-quote escape. Handled by
    /// <c>CommentsAndLiteralInteriors</c> and by the two-stage reader; NOT
    /// understood by <c>CommentsOnlyLiteralsIntact</c>, which has no <c>@"</c>
    /// branch at all — the exact gap
    /// <c>UnmaskableLiteralForms</c>/<c>UnhandledForms</c> exists to document.
    /// </summary>
    internal const string VerbatimString =
        """"
        app.MapGet("/x", Handler).WithSummary(@"a ""quoted"" path");
        """";

    /// <summary>
    /// A backslash-escaped quote inside an ordinary string. The weakest masker's
    /// <c>EndOfStringLiteral</c> does not honour the escape, so it closes the
    /// "string" one character early and reads what follows as ordinary code.
    /// </summary>
    internal const string EscapedQuote =
        """"
        app.MapGet("/x", Handler).WithSummary("he said \"no\"");
        """";

    /// <summary>
    /// A double quote written as a char literal (<c>Split('"')</c>) — the other
    /// half of the trap: a masker that does not know about char literals reads
    /// this <c>'</c> as nothing special and the following <c>"</c> as the start
    /// of a new string, which then runs to the next real quote in the file.
    /// </summary>
    internal const string QuoteAsCharLiteral =
        """"
        app.MapGet("/x", Handler.Split('"'));
        """";

    /// <summary>
    /// An open paren written as a char literal (<c>IndexOf('(')</c>) — issue
    /// #2183's second hole, found only after the string-literal fix was believed
    /// to have closed it. A chain-end walk that does not step over char literals
    /// counts this <c>(</c> as real and never lets its depth return to zero.
    /// </summary>
    internal const string OpenParenAsCharLiteral =
        """"
        app.MapGet("/x", Handler.IndexOf('('));
        """";

    /// <summary>
    /// A raw string literal (triple-quote). NONE of the three masker
    /// strictnesses understands this form — each mis-reads it in a different
    /// way, and that shared blind spot is exactly what
    /// <see cref="SourceScanCharacterisationTests"/> asserts rather than hides.
    /// Teaching a masker this form is a behaviour change and is out of scope
    /// (spec 190 §7).
    /// </summary>
    internal const string RawStringLiteral =
        """"
        app.MapGet("/x", Handler).WithSummary("""raw "quoted" text""");
        """";

    /// <summary>
    /// A statement-bodied lambda passed as a chain argument, with an internal
    /// <c>;</c> that must not be mistaken for the chain's own terminator (issue
    /// #2183's constructed proof case).
    /// </summary>
    internal const string StatementBodiedLambdaInChain =
        """"
        app.MapPost("/x", Handler).AddEndpointFilter(async (c, n) => { int p = 1; return await n(c); }).WithSummary("ok");
        """";

    /// <summary>An expression-bodied handler — the arrow-to-semicolon shape <c>HandlerBodyFor</c> must read as a body.</summary>
    internal const string ExpressionBodiedHandler =
        """"
        private static IResult Get(HttpContext context) => Results.Ok();
        """";

    /// <summary>
    /// The first of two files declaring one partial class. Paired with
    /// <see cref="HandlerInSecondPartialFileSecondFile"/> — together they are
    /// the two <c>ClassSpan</c>s <c>HandlerBodyFor</c> must resolve across to
    /// find a handler declared in the other file from the one that maps it.
    /// This file declares the class but not the handler the fixture pair is
    /// built to find.
    /// </summary>
    internal const string HandlerInSecondPartialFileFirstFile =
        """"
        public sealed partial class WidgetEndpoints
        {
            private static IResult Get(HttpContext context) => Results.Ok();
        }
        """";

    /// <summary>
    /// The second file declaring the same partial class, with the handler a
    /// mapping in the first file names. See
    /// <see cref="HandlerInSecondPartialFileFirstFile"/>.
    /// </summary>
    internal const string HandlerInSecondPartialFileSecondFile =
        """"
        public sealed partial class WidgetEndpoints
        {
            private static void Handle(HttpContext context)
            {
                context.Response.StatusCode = 200;
            }
        }
        """";

    /// <summary>A <c>MapGroup</c> declaration a mapping's chain-plus-group antecedent must resolve back to.</summary>
    internal const string MapGroupChain =
        """"
        var g = app.MapGroup("/x").RequireAuthorization();
        """";
}
