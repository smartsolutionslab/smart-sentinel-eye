using System.Globalization;
using System.Text;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Tokenizer for AEL (ADR-0099 / spec 007 FR-013). Walks the source
/// string once and produces a flat <c>IReadOnlyList&lt;AelToken&gt;</c>.
/// String literals support both single and double quotes; no escape
/// sequences in v1 (admins paste plain text).
/// </summary>
public static class AelLexer
{
    public static IReadOnlyList<AelToken> Tokenize(string source)
    {
        Ensure.That(source).IsNotNull();

        List<AelToken> tokens = [];
        var i = 0;
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i]))
            { i++; continue; }

            i = LexToken(source, i, tokens);
        }

        tokens.Add(new AelToken(AelTokenKind.EndOfFile, string.Empty, source.Length));
        return tokens;
    }

    private static int LexToken(string source, int position, List<AelToken> tokens)
    {
        var c = source[position];

        if (TryLexSingleCharacter(c, position, tokens))
        {
            return position + 1;
        }
        if (IsRelationalStart(c))
        {
            return LexRelational(source, position, tokens);
        }
        if (IsEqualityStart(c))
        {
            return LexEquality(source, position, tokens);
        }
        if (IsLogicalStart(c))
        {
            return LexLogical(source, position, tokens);
        }
        if (IsQuote(c))
        {
            return LexString(source, position, tokens);
        }
        if (char.IsAsciiDigit(c))
        {
            return LexNumber(source, position, tokens);
        }
        if (IsWordStart(c))
        {
            return LexWord(source, position, tokens);
        }

        throw new AelParseException($"unexpected character '{c}'", position);
    }

    private static bool IsRelationalStart(char c) => c is '<' or '>';

    private static bool IsEqualityStart(char c) => c is '=' or '!';

    private static bool IsLogicalStart(char c) => c is '&' or '|';

    private static bool IsQuote(char c) => c is '"' or '\'';

    private static bool IsWordStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool NextIs(string source, int position, char expected) =>
        position + 1 < source.Length && source[position + 1] == expected;

    private static bool TryLexSingleCharacter(char c, int position, List<AelToken> tokens) =>
        TryLexPunctuation(c, position, tokens) || TryLexArithmetic(c, position, tokens);

    private static bool TryLexPunctuation(char c, int position, List<AelToken> tokens)
    {
        switch (c)
        {
            case '$':
                tokens.Add(new AelToken(AelTokenKind.Dollar, "$", position));
                return true;
            case '.':
                tokens.Add(new AelToken(AelTokenKind.Dot, ".", position));
                return true;
            case '(':
                tokens.Add(new AelToken(AelTokenKind.LeftParen, "(", position));
                return true;
            case ')':
                tokens.Add(new AelToken(AelTokenKind.RightParen, ")", position));
                return true;
            default:
                return false;
        }
    }

    private static bool TryLexArithmetic(char c, int position, List<AelToken> tokens)
    {
        switch (c)
        {
            case '+':
                tokens.Add(new AelToken(AelTokenKind.Plus, "+", position));
                return true;
            case '-':
                tokens.Add(new AelToken(AelTokenKind.Minus, "-", position));
                return true;
            case '*':
                tokens.Add(new AelToken(AelTokenKind.Star, "*", position));
                return true;
            case '/':
                tokens.Add(new AelToken(AelTokenKind.Slash, "/", position));
                return true;
            case '%':
                tokens.Add(new AelToken(AelTokenKind.Percent, "%", position));
                return true;
            default:
                return false;
        }
    }

    private static int LexRelational(string source, int position, List<AelToken> tokens)
    {
        var c = source[position];

        if (c == '<')
        {
            if (NextIs(source, position, '='))
            {
                tokens.Add(new AelToken(AelTokenKind.LessThanOrEqual, "<=", position));
                return position + 2;
            }
            tokens.Add(new AelToken(AelTokenKind.LessThan, "<", position));
            return position + 1;
        }

        if (NextIs(source, position, '='))
        {
            tokens.Add(new AelToken(AelTokenKind.GreaterThanOrEqual, ">=", position));
            return position + 2;
        }
        tokens.Add(new AelToken(AelTokenKind.GreaterThan, ">", position));
        return position + 1;
    }

    private static int LexEquality(string source, int position, List<AelToken> tokens)
    {
        var c = source[position];

        if (c == '=')
        {
            if (NextIs(source, position, '='))
            {
                tokens.Add(new AelToken(AelTokenKind.Equal, "==", position));
                return position + 2;
            }
            throw new AelParseException("expected '==' (single '=' is not an operator)", position);
        }

        if (NextIs(source, position, '='))
        {
            tokens.Add(new AelToken(AelTokenKind.NotEqual, "!=", position));
            return position + 2;
        }
        tokens.Add(new AelToken(AelTokenKind.Bang, "!", position));
        return position + 1;
    }

    private static int LexLogical(string source, int position, List<AelToken> tokens)
    {
        var c = source[position];

        if (c == '&')
        {
            if (NextIs(source, position, '&'))
            {
                tokens.Add(new AelToken(AelTokenKind.AndAnd, "&&", position));
                return position + 2;
            }
            throw new AelParseException("expected '&&'", position);
        }

        if (NextIs(source, position, '|'))
        {
            tokens.Add(new AelToken(AelTokenKind.OrOr, "||", position));
            return position + 2;
        }
        throw new AelParseException("expected '||'", position);
    }

    private static int LexString(string source, int position, List<AelToken> tokens)
    {
        var start = position;
        var quote = source[position];
        var i = position + 1;
        StringBuilder buffer = new();
        while (i < source.Length && source[i] != quote)
        {
            buffer.Append(source[i]);
            i++;
        }
        if (i >= source.Length)
        {
            throw new AelParseException($"unterminated string literal (started at {start})", start);
        }
        i++; // closing quote
        tokens.Add(new AelToken(AelTokenKind.StringLiteral, buffer.ToString(), start));
        return i;
    }

    private static int LexNumber(string source, int position, List<AelToken> tokens)
    {
        var start = position;
        var isDecimal = false;
        var i = SkipDigits(source, position);

        if (i < source.Length && source[i] == '.'
            && i + 1 < source.Length && char.IsAsciiDigit(source[i + 1]))
        {
            isDecimal = true;
            i = SkipDigits(source, i + 1);
        }
        if (i < source.Length && source[i] is 'e' or 'E')
        {
            throw new AelParseException(
                "exponent notation is not supported; write the number out in full", i);
        }

        var lexeme = source[start..i];
        tokens.Add(new AelToken(
            isDecimal ? AelTokenKind.DecimalLiteral : AelTokenKind.IntLiteral,
            lexeme,
            start));
        return i;
    }

    private static int SkipDigits(string source, int position)
    {
        var i = position;
        while (i < source.Length && char.IsAsciiDigit(source[i]))
        {
            i++;
        }
        return i;
    }

    private static int LexWord(string source, int position, List<AelToken> tokens)
    {
        var start = position;
        var i = position;
        while (i < source.Length &&
               (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_'))
        {
            i++;
        }
        var lexeme = source[start..i];
        var kind = lexeme switch
        {
            "true" => AelTokenKind.TrueLiteral,
            "false" => AelTokenKind.FalseLiteral,
            "contains" => AelTokenKind.Contains,
            _ => AelTokenKind.Identifier,
        };
        tokens.Add(new AelToken(kind, lexeme, start));
        return i;
    }

    internal static long ParseInt(AelToken token)
    {
        if (!long.TryParse(token.Lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new AelParseException(
                $"integer literal '{token.Lexeme}' is out of range; " +
                "add a decimal point to allow values up to decimal's range",
                token.Position);
        }
        return value;
    }

    internal static decimal ParseDecimal(AelToken token)
    {
        if (!decimal.TryParse(token.Lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new AelParseException(
                $"decimal literal '{token.Lexeme}' is out of range",
                token.Position);
        }
        return value;
    }
}
