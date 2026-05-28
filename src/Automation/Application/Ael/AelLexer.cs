using System.Globalization;
using System.Text;

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
        ArgumentNullException.ThrowIfNull(source);

        List<AelToken> tokens = new();
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '$': tokens.Add(new AelToken(AelTokenKind.Dollar,     "$", i)); i++; continue;
                case '.': tokens.Add(new AelToken(AelTokenKind.Dot,        ".", i)); i++; continue;
                case '(': tokens.Add(new AelToken(AelTokenKind.LeftParen,  "(", i)); i++; continue;
                case ')': tokens.Add(new AelToken(AelTokenKind.RightParen, ")", i)); i++; continue;
                case '+': tokens.Add(new AelToken(AelTokenKind.Plus,       "+", i)); i++; continue;
                case '-': tokens.Add(new AelToken(AelTokenKind.Minus,      "-", i)); i++; continue;
                case '*': tokens.Add(new AelToken(AelTokenKind.Star,       "*", i)); i++; continue;
                case '/': tokens.Add(new AelToken(AelTokenKind.Slash,      "/", i)); i++; continue;
                case '%': tokens.Add(new AelToken(AelTokenKind.Percent,    "%", i)); i++; continue;
            }

            if (c == '<')
            {
                if (i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new AelToken(AelTokenKind.LessThanOrEqual, "<=", i)); i += 2; continue;
                }
                tokens.Add(new AelToken(AelTokenKind.LessThan, "<", i)); i++; continue;
            }
            if (c == '>')
            {
                if (i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new AelToken(AelTokenKind.GreaterThanOrEqual, ">=", i)); i += 2; continue;
                }
                tokens.Add(new AelToken(AelTokenKind.GreaterThan, ">", i)); i++; continue;
            }
            if (c == '=')
            {
                if (i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new AelToken(AelTokenKind.Equal, "==", i)); i += 2; continue;
                }
                throw new AelParseException("expected '==' (single '=' is not an operator)", i);
            }
            if (c == '!')
            {
                if (i + 1 < source.Length && source[i + 1] == '=')
                {
                    tokens.Add(new AelToken(AelTokenKind.NotEqual, "!=", i)); i += 2; continue;
                }
                tokens.Add(new AelToken(AelTokenKind.Bang, "!", i)); i++; continue;
            }
            if (c == '&')
            {
                if (i + 1 < source.Length && source[i + 1] == '&')
                {
                    tokens.Add(new AelToken(AelTokenKind.AndAnd, "&&", i)); i += 2; continue;
                }
                throw new AelParseException("expected '&&'", i);
            }
            if (c == '|')
            {
                if (i + 1 < source.Length && source[i + 1] == '|')
                {
                    tokens.Add(new AelToken(AelTokenKind.OrOr, "||", i)); i += 2; continue;
                }
                throw new AelParseException("expected '||'", i);
            }

            if (c == '"' || c == '\'')
            {
                int start = i;
                char quote = c;
                i++;
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
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                int start = i;
                bool isDecimal = false;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                if (i < source.Length && source[i] == '.'
                    && i + 1 < source.Length && char.IsAsciiDigit(source[i + 1]))
                {
                    isDecimal = true;
                    i++;
                    while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                }
                string lexeme = source[start..i];
                tokens.Add(new AelToken(
                    isDecimal ? AelTokenKind.DecimalLiteral : AelTokenKind.IntLiteral,
                    lexeme,
                    start));
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length &&
                       (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }
                string lexeme = source[start..i];
                AelTokenKind kind = lexeme switch
                {
                    "true" => AelTokenKind.TrueLiteral,
                    "false" => AelTokenKind.FalseLiteral,
                    "contains" => AelTokenKind.Contains,
                    _ => AelTokenKind.Identifier,
                };
                tokens.Add(new AelToken(kind, lexeme, start));
                continue;
            }

            throw new AelParseException($"unexpected character '{c}'", i);
        }

        tokens.Add(new AelToken(AelTokenKind.EndOfFile, string.Empty, source.Length));
        return tokens;
    }

    internal static long ParseInt(string lexeme) =>
        long.Parse(lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture);

    internal static decimal ParseDecimal(string lexeme) =>
        decimal.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture);
}
