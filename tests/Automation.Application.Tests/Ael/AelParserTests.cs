using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

public class AelParserTests
{
    private static readonly string[] ExpectedPayloadCycleTimeSegments = ["payload", "cycleTime"];

    [Fact]
    public void Parses_an_integer_literal()
    {
        AelExpression expr = AelParser.Parse("42");
        AelExpression.Literal lit = expr.ShouldBeOfType<AelExpression.Literal>();
        AelValue.IntValue iv = lit.Value.ShouldBeOfType<AelValue.IntValue>();
        iv.Value.ShouldBe(42);
    }

    [Fact]
    public void Parses_a_decimal_literal()
    {
        AelExpression expr = AelParser.Parse("3.14");
        AelValue.DecimalValue dv = expr.ShouldBeOfType<AelExpression.Literal>().Value
            .ShouldBeOfType<AelValue.DecimalValue>();
        dv.Value.ShouldBe(3.14m);
    }

    [Fact]
    public void Parses_a_field_access_path_into_segments()
    {
        AelExpression expr = AelParser.Parse("$.payload.cycleTime");
        AelExpression.FieldAccess fa = expr.ShouldBeOfType<AelExpression.FieldAccess>();
        fa.Segments.ShouldBe(ExpectedPayloadCycleTimeSegments);
    }

    [Fact]
    public void Mul_binds_tighter_than_add()
    {
        AelExpression expr = AelParser.Parse("1 + 2 * 3");
        AelExpression.Binary add = expr.ShouldBeOfType<AelExpression.Binary>();
        add.Operator.ShouldBe(BinaryOperator.Add);
        add.Right.ShouldBeOfType<AelExpression.Binary>().Operator.ShouldBe(BinaryOperator.Multiply);
    }

    [Fact]
    public void Comparison_binds_looser_than_arithmetic()
    {
        AelExpression expr = AelParser.Parse("1 + 2 == 3");
        AelExpression.Binary eq = expr.ShouldBeOfType<AelExpression.Binary>();
        eq.Operator.ShouldBe(BinaryOperator.Equal);
        eq.Left.ShouldBeOfType<AelExpression.Binary>().Operator.ShouldBe(BinaryOperator.Add);
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        AelExpression expr = AelParser.Parse("true || true && false");
        AelExpression.Logical or = expr.ShouldBeOfType<AelExpression.Logical>();
        or.Operator.ShouldBe(LogicalOperator.Or);
        or.Right.ShouldBeOfType<AelExpression.Logical>().Operator.ShouldBe(LogicalOperator.And);
    }

    [Fact]
    public void Parentheses_override_precedence()
    {
        AelExpression expr = AelParser.Parse("(1 + 2) * 3");
        AelExpression.Binary mul = expr.ShouldBeOfType<AelExpression.Binary>();
        mul.Operator.ShouldBe(BinaryOperator.Multiply);
        mul.Left.ShouldBeOfType<AelExpression.Binary>().Operator.ShouldBe(BinaryOperator.Add);
    }

    [Fact]
    public void Unary_negate_and_not_are_recognised()
    {
        AelExpression negate = AelParser.Parse("-5");
        negate.ShouldBeOfType<AelExpression.Unary>().Operator.ShouldBe(UnaryOperator.Negate);

        AelExpression not = AelParser.Parse("!true");
        not.ShouldBeOfType<AelExpression.Unary>().Operator.ShouldBe(UnaryOperator.Not);
    }

    [Fact]
    public void Contains_keyword_parses_as_a_binary_operator()
    {
        AelExpression expr = AelParser.Parse("\"abcdef\" contains \"cd\"");
        AelExpression.Binary bin = expr.ShouldBeOfType<AelExpression.Binary>();
        bin.Operator.ShouldBe(BinaryOperator.Contains);
    }

    [Theory]
    [InlineData("1 +", "expected literal")]
    [InlineData("(1 + 2", "expected ')'")]
    [InlineData("$", "expected '.'")]
    [InlineData("$.", "expected identifier after '.'")]
    public void Rejects_malformed_input(string source, string fragment)
    {
        AelParseException ex = Should.Throw<AelParseException>(() => AelParser.Parse(source));
        ex.Message.ShouldContain(fragment);
    }

    [Fact]
    public void Parses_the_PLC_predicate_fixture_end_to_end()
    {
        // smoke test: parses without throwing and returns a logical-and root.
        AelExpression expr = AelParser.Parse(AelFixtures.SimplePlcPredicate);
        expr.ShouldBeOfType<AelExpression.Logical>().Operator.ShouldBe(LogicalOperator.And);
    }
}
