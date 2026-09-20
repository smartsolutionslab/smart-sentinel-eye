namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 190 (issue #2257) — the characterisation harness for the extraction of
/// <c>RepositorySource</c>, <c>SourceMask</c> and <c>RouteChainReader</c> out
/// of the six source-scanning guards. ADR-0144: this was the GREEN colour —
/// behaviour-preserving — and this file is not part of what is preserved. It
/// is the proof that the extraction preserved it.
///
/// <para>
/// <b>Two mechanisms were used, because one was not enough (plan.md §7).</b>
/// M1 (below) is the permanent one. M2 — a frozen, throwaway verbatim copy of
/// each pre-refactor masker and chain-end walk, swept over every <c>.cs</c>
/// under <c>src/*/Api</c> and, once <c>SourceMask</c>/<c>RouteChainReader</c>
/// existed (tasks.md T13), compared against them char-for-char and
/// index-for-index — was the transitional one, and tasks.md T29 deleted it
/// once that comparison was green: a permanent frozen copy of the thing just
/// deduplicated would have been the defect this spec closed, kept.
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
/// </summary>
public sealed class SourceScanCharacterisationTests
{
    // =====================================================================
    // M1 — the fixture golden test (permanent; plan.md §7, tasks.md T2).
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
