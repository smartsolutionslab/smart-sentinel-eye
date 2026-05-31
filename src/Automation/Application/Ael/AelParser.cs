using System.Collections.Immutable;

namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Recursive-descent parser over the AEL token stream (ADR-0099).
/// Operator precedence (low → high): <c>||</c>, <c>&amp;&amp;</c>,
/// equality, comparison, additive, multiplicative, unary, primary.
///
/// <para>
/// The parser builds an immutable <see cref="AelExpression"/> tree.
/// Parse failures throw <see cref="AelParseException"/> carrying the
/// position of the failing token.
/// </para>
/// </summary>
public static class AelParser
{
    public static AelExpression Parse(string source)
    {
        IReadOnlyList<AelToken> tokens = AelLexer.Tokenize(source);
        int cursor = 0;
        AelExpression expression = ParseOr(tokens, ref cursor);
        if (tokens[cursor].Kind != AelTokenKind.EndOfFile)
        {
            throw new AelParseException(
                $"unexpected trailing token '{tokens[cursor].Lexeme}'",
                tokens[cursor].Position);
        }
        return expression;
    }

    private static AelExpression ParseOr(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelExpression left = ParseAnd(tokens, ref cursor);
        while (tokens[cursor].Kind == AelTokenKind.OrOr)
        {
            cursor++;
            AelExpression right = ParseAnd(tokens, ref cursor);
            left = new AelExpression.Logical(LogicalOperator.Or, left, right);
        }
        return left;
    }

    private static AelExpression ParseAnd(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelExpression left = ParseNot(tokens, ref cursor);
        while (tokens[cursor].Kind == AelTokenKind.AndAnd)
        {
            cursor++;
            AelExpression right = ParseNot(tokens, ref cursor);
            left = new AelExpression.Logical(LogicalOperator.And, left, right);
        }
        return left;
    }

    private static AelExpression ParseNot(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        if (tokens[cursor].Kind == AelTokenKind.Bang)
        {
            cursor++;
            AelExpression operand = ParseNot(tokens, ref cursor);
            return new AelExpression.Unary(UnaryOperator.Not, operand);
        }
        return ParseComparison(tokens, ref cursor);
    }

    private static AelExpression ParseComparison(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelExpression left = ParseAdditive(tokens, ref cursor);
        BinaryOperator? binaryOperator = tokens[cursor].Kind switch
        {
            AelTokenKind.Equal              => BinaryOperator.Equal,
            AelTokenKind.NotEqual           => BinaryOperator.NotEqual,
            AelTokenKind.LessThan           => BinaryOperator.LessThan,
            AelTokenKind.LessThanOrEqual    => BinaryOperator.LessThanOrEqual,
            AelTokenKind.GreaterThan        => BinaryOperator.GreaterThan,
            AelTokenKind.GreaterThanOrEqual => BinaryOperator.GreaterThanOrEqual,
            AelTokenKind.Contains           => BinaryOperator.Contains,
            _ => null,
        };
        if (binaryOperator is null) return left;
        cursor++;
        AelExpression right = ParseAdditive(tokens, ref cursor);
        return new AelExpression.Binary(binaryOperator.Value, left, right);
    }

    private static AelExpression ParseAdditive(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelExpression left = ParseMultiplicative(tokens, ref cursor);
        while (tokens[cursor].Kind is AelTokenKind.Plus or AelTokenKind.Minus)
        {
            BinaryOperator binaryOperator = tokens[cursor].Kind == AelTokenKind.Plus
                ? BinaryOperator.Add
                : BinaryOperator.Subtract;
            cursor++;
            AelExpression right = ParseMultiplicative(tokens, ref cursor);
            left = new AelExpression.Binary(binaryOperator, left, right);
        }
        return left;
    }

    private static AelExpression ParseMultiplicative(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelExpression left = ParseUnary(tokens, ref cursor);
        while (tokens[cursor].Kind is AelTokenKind.Star or AelTokenKind.Slash or AelTokenKind.Percent)
        {
            BinaryOperator binaryOperator = tokens[cursor].Kind switch
            {
                AelTokenKind.Star => BinaryOperator.Multiply,
                AelTokenKind.Slash => BinaryOperator.Divide,
                _ => BinaryOperator.Modulo,
            };
            cursor++;
            AelExpression right = ParseUnary(tokens, ref cursor);
            left = new AelExpression.Binary(binaryOperator, left, right);
        }
        return left;
    }

    private static AelExpression ParseUnary(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        if (tokens[cursor].Kind == AelTokenKind.Minus)
        {
            cursor++;
            AelExpression operand = ParseUnary(tokens, ref cursor);
            return new AelExpression.Unary(UnaryOperator.Negate, operand);
        }
        return ParsePrimary(tokens, ref cursor);
    }

    private static AelExpression ParsePrimary(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelToken token = tokens[cursor];
        switch (token.Kind)
        {
            case AelTokenKind.IntLiteral:
                cursor++;
                return new AelExpression.Literal(
                    new AelValue.IntValue(AelLexer.ParseInt(token.Lexeme)));

            case AelTokenKind.DecimalLiteral:
                cursor++;
                return new AelExpression.Literal(
                    new AelValue.DecimalValue(AelLexer.ParseDecimal(token.Lexeme)));

            case AelTokenKind.StringLiteral:
                cursor++;
                return new AelExpression.Literal(new AelValue.StringValue(token.Lexeme));

            case AelTokenKind.TrueLiteral:
                cursor++;
                return new AelExpression.Literal(new AelValue.BoolValue(true));

            case AelTokenKind.FalseLiteral:
                cursor++;
                return new AelExpression.Literal(new AelValue.BoolValue(false));

            case AelTokenKind.LeftParen:
                cursor++;
                AelExpression inner = ParseOr(tokens, ref cursor);
                if (tokens[cursor].Kind != AelTokenKind.RightParen)
                {
                    throw new AelParseException("expected ')'", tokens[cursor].Position);
                }
                cursor++;
                return inner;

            case AelTokenKind.Dollar:
                return ParseFieldAccess(tokens, ref cursor);

            default:
                throw new AelParseException(
                    $"expected literal, field access, or '('; got '{token.Lexeme}'",
                    token.Position);
        }
    }

    private static AelExpression.FieldAccess ParseFieldAccess(IReadOnlyList<AelToken> tokens, ref int cursor)
    {
        AelToken dollar = tokens[cursor];
        cursor++; // consume $
        if (tokens[cursor].Kind != AelTokenKind.Dot)
        {
            throw new AelParseException(
                "expected '.' after '$' to begin a field path",
                tokens[cursor].Position);
        }

        ImmutableArray<string>.Builder segments = ImmutableArray.CreateBuilder<string>();
        while (tokens[cursor].Kind == AelTokenKind.Dot)
        {
            cursor++; // consume '.'
            if (tokens[cursor].Kind != AelTokenKind.Identifier)
            {
                throw new AelParseException(
                    $"expected identifier after '.' in field path; got '{tokens[cursor].Lexeme}'",
                    tokens[cursor].Position);
            }
            segments.Add(tokens[cursor].Lexeme);
            cursor++;
        }

        if (segments.Count == 0)
        {
            throw new AelParseException("field path must contain at least one segment", dollar.Position);
        }
        return new AelExpression.FieldAccess(segments.ToImmutable());
    }
}
