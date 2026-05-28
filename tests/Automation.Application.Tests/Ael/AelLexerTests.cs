using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

public class AelLexerTests
{
    [Fact]
    public void Recognises_int_decimal_string_bool_literals()
    {
        IReadOnlyList<AelToken> tokens =
            AelLexer.Tokenize("42 3.14 \"hello\" 'world' true false");

        tokens.Select(t => t.Kind).ShouldBe(
        [
            AelTokenKind.IntLiteral,
            AelTokenKind.DecimalLiteral,
            AelTokenKind.StringLiteral,
            AelTokenKind.StringLiteral,
            AelTokenKind.TrueLiteral,
            AelTokenKind.FalseLiteral,
            AelTokenKind.EndOfFile,
        ]);
        tokens[2].Lexeme.ShouldBe("hello");
        tokens[3].Lexeme.ShouldBe("world");
    }

    [Fact]
    public void Recognises_arithmetic_comparison_logical_and_structural_operators()
    {
        IReadOnlyList<AelToken> tokens = AelLexer.Tokenize(
            "+ - * / % == != < <= > >= && || ! ( ) $ . contains");

        tokens.Select(t => t.Kind).ShouldBe(
        [
            AelTokenKind.Plus, AelTokenKind.Minus, AelTokenKind.Star,
            AelTokenKind.Slash, AelTokenKind.Percent,
            AelTokenKind.Equal, AelTokenKind.NotEqual,
            AelTokenKind.LessThan, AelTokenKind.LessThanOrEqual,
            AelTokenKind.GreaterThan, AelTokenKind.GreaterThanOrEqual,
            AelTokenKind.AndAnd, AelTokenKind.OrOr, AelTokenKind.Bang,
            AelTokenKind.LeftParen, AelTokenKind.RightParen,
            AelTokenKind.Dollar, AelTokenKind.Dot,
            AelTokenKind.Contains,
            AelTokenKind.EndOfFile,
        ]);
    }

    [Fact]
    public void Tokenizes_a_field_access_path()
    {
        IReadOnlyList<AelToken> tokens = AelLexer.Tokenize("$.payload.cycleTime");
        tokens.Select(t => t.Kind).ShouldBe(
        [
            AelTokenKind.Dollar, AelTokenKind.Dot, AelTokenKind.Identifier,
            AelTokenKind.Dot, AelTokenKind.Identifier, AelTokenKind.EndOfFile,
        ]);
    }

    [Theory]
    [InlineData("=", "expected '=='")]
    [InlineData("&", "expected '&&'")]
    [InlineData("|", "expected '||'")]
    public void Rejects_single_char_operators_that_must_be_doubled(string input, string fragment)
    {
        AelParseException ex = Should.Throw<AelParseException>(() => AelLexer.Tokenize(input));
        ex.Message.ShouldContain(fragment);
    }

    [Fact]
    public void Rejects_unterminated_string_literal()
    {
        AelParseException ex = Should.Throw<AelParseException>(
            () => AelLexer.Tokenize("\"unterminated"));
        ex.Message.ShouldContain("unterminated string literal");
    }

    [Fact]
    public void Rejects_unexpected_character()
    {
        AelParseException ex = Should.Throw<AelParseException>(
            () => AelLexer.Tokenize("@"));
        ex.Position.ShouldBe(0);
    }

    [Fact]
    public void Skips_whitespace_between_tokens()
    {
        IReadOnlyList<AelToken> tokens = AelLexer.Tokenize("  1\t+\n2  ");
        tokens.Select(t => t.Kind).ShouldBe(
        [
            AelTokenKind.IntLiteral, AelTokenKind.Plus,
            AelTokenKind.IntLiteral, AelTokenKind.EndOfFile,
        ]);
    }

    [Fact]
    public void Tokenizes_the_PLC_predicate_fixture_end_to_end()
    {
        IReadOnlyList<AelToken> tokens = AelLexer.Tokenize(AelFixtures.SimplePlcPredicate);
        tokens[^1].Kind.ShouldBe(AelTokenKind.EndOfFile);
        // 13 meaningful tokens + EOF.
        tokens.Count.ShouldBe(14);
    }
}
