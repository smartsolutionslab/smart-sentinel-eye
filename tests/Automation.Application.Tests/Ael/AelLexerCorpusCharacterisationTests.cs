using System.Globalization;
using System.Text;
using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AelLexer.Tokenize</c> is split by token family.
/// <c>AelLexerTests</c> pins nine behaviours by kind and, for refusals, by
/// <c>ShouldContain</c>; a split that moved a position, a lexeme or a word of
/// a message by one would pass all nine.
///
/// <para>
/// Each source below is paired with the <b>complete</b> token stream (kind,
/// lexeme, position, in order) or the <see cref="AelParseException"/> (position
/// and full message) the unrefactored lexer produced. The rendering is a
/// test-local ASCII form, so a lexeme the lexer got wrong cannot hide behind
/// an unprintable character. The expected strings are literals captured from
/// the unrefactored code, never computed by it.
/// </para>
/// </summary>
public class AelLexerCorpusCharacterisationTests
{
    private static readonly (string Source, string Expected)[] Cases =
    [
        // Nothing to lex: the end-of-file token sits at the source length.
        ("", "EndOfFile '' @0"),
        (" ", "EndOfFile '' @1"),
        ("\t\r\n ", "EndOfFile '' @4"),
        (" ", "EndOfFile '' @1"),
        (" ", "EndOfFile '' @1"),
        ("　", "EndOfFile '' @1"),
        ("\u0085", "EndOfFile '' @1"),
        ("\v\f", "EndOfFile '' @2"),
        ("a b", "Identifier 'a' @0 | Identifier 'b' @2 | EndOfFile '' @3"),

        // Single-character tokens.
        ("$", "Dollar '$' @0 | EndOfFile '' @1"),
        (".", "Dot '.' @0 | EndOfFile '' @1"),
        ("(", "LeftParen '(' @0 | EndOfFile '' @1"),
        (")", "RightParen ')' @0 | EndOfFile '' @1"),
        ("+", "Plus '+' @0 | EndOfFile '' @1"),
        ("-", "Minus '-' @0 | EndOfFile '' @1"),
        ("*", "Star '*' @0 | EndOfFile '' @1"),
        ("/", "Slash '/' @0 | EndOfFile '' @1"),
        ("%", "Percent '%' @0 | EndOfFile '' @1"),
        ("  $  ", "Dollar '$' @2 | EndOfFile '' @5"),
        ("()+-*/%", "LeftParen '(' @0 | RightParen ')' @1 | Plus '+' @2 | Minus '-' @3 | Star '*' @4 | Slash '/' @5 | Percent '%' @6 | EndOfFile '' @7"),

        // Comparison and logical operators, alone and adjacent.
        ("<", "LessThan '<' @0 | EndOfFile '' @1"),
        ("<=", "LessThanOrEqual '<=' @0 | EndOfFile '' @2"),
        (">", "GreaterThan '>' @0 | EndOfFile '' @1"),
        (">=", "GreaterThanOrEqual '>=' @0 | EndOfFile '' @2"),
        ("==", "Equal '==' @0 | EndOfFile '' @2"),
        ("!=", "NotEqual '!=' @0 | EndOfFile '' @2"),
        ("!", "Bang '!' @0 | EndOfFile '' @1"),
        ("&&", "AndAnd '&&' @0 | EndOfFile '' @2"),
        ("||", "OrOr '||' @0 | EndOfFile '' @2"),
        ("<==", "AelParseException @2: AEL parse error at position 2: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("<>", "LessThan '<' @0 | GreaterThan '>' @1 | EndOfFile '' @2"),
        ("><", "GreaterThan '>' @0 | LessThan '<' @1 | EndOfFile '' @2"),
        ("!==", "AelParseException @2: AEL parse error at position 2: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("!!", "Bang '!' @0 | Bang '!' @1 | EndOfFile '' @2"),
        ("!!=", "Bang '!' @0 | NotEqual '!=' @1 | EndOfFile '' @3"),
        ("&&&", "AelParseException @2: AEL parse error at position 2: expected \\'&&\\'"),
        ("|||", "AelParseException @2: AEL parse error at position 2: expected \\'||\\'"),
        ("1 <= 2", "IntLiteral '1' @0 | LessThanOrEqual '<=' @2 | IntLiteral '2' @5 | EndOfFile '' @6"),
        ("1<=2", "IntLiteral '1' @0 | LessThanOrEqual '<=' @1 | IntLiteral '2' @3 | EndOfFile '' @4"),
        ("a>=b", "Identifier 'a' @0 | GreaterThanOrEqual '>=' @1 | Identifier 'b' @3 | EndOfFile '' @4"),
        ("a==b!=c", "Identifier 'a' @0 | Equal '==' @1 | Identifier 'b' @3 | NotEqual '!=' @4 | Identifier 'c' @6 | EndOfFile '' @7"),

        // Operators that are not operators on their own.
        ("=", "AelParseException @0: AEL parse error at position 0: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("= =", "AelParseException @0: AEL parse error at position 0: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("a =", "AelParseException @2: AEL parse error at position 2: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("x = 1", "AelParseException @2: AEL parse error at position 2: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("&", "AelParseException @0: AEL parse error at position 0: expected \\'&&\\'"),
        ("a & b", "AelParseException @2: AEL parse error at position 2: expected \\'&&\\'"),
        ("a &", "AelParseException @2: AEL parse error at position 2: expected \\'&&\\'"),
        ("|", "AelParseException @0: AEL parse error at position 0: expected \\'||\\'"),
        ("a | b", "AelParseException @2: AEL parse error at position 2: expected \\'||\\'"),
        ("a || b | c", "AelParseException @7: AEL parse error at position 7: expected \\'||\\'"),

        // Keywords are case-sensitive and end at the identifier boundary.
        ("true", "TrueLiteral 'true' @0 | EndOfFile '' @4"),
        ("false", "FalseLiteral 'false' @0 | EndOfFile '' @5"),
        ("contains", "Contains 'contains' @0 | EndOfFile '' @8"),
        ("True", "Identifier 'True' @0 | EndOfFile '' @4"),
        ("False", "Identifier 'False' @0 | EndOfFile '' @5"),
        ("Contains", "Identifier 'Contains' @0 | EndOfFile '' @8"),
        ("TRUE", "Identifier 'TRUE' @0 | EndOfFile '' @4"),
        ("true1", "Identifier 'true1' @0 | EndOfFile '' @5"),
        ("truex", "Identifier 'truex' @0 | EndOfFile '' @5"),
        ("_true", "Identifier '_true' @0 | EndOfFile '' @5"),
        ("falsey", "Identifier 'falsey' @0 | EndOfFile '' @6"),
        ("contains_x", "Identifier 'contains_x' @0 | EndOfFile '' @10"),
        ("a contains b", "Identifier 'a' @0 | Contains 'contains' @2 | Identifier 'b' @11 | EndOfFile '' @12"),
        ("true&&false", "TrueLiteral 'true' @0 | AndAnd '&&' @4 | FalseLiteral 'false' @6 | EndOfFile '' @11"),

        // Identifiers and field paths.
        ("_", "Identifier '_' @0 | EndOfFile '' @1"),
        ("__", "Identifier '__' @0 | EndOfFile '' @2"),
        ("_a1", "Identifier '_a1' @0 | EndOfFile '' @3"),
        ("a_b", "Identifier 'a_b' @0 | EndOfFile '' @3"),
        ("abc", "Identifier 'abc' @0 | EndOfFile '' @3"),
        ("A1b2", "Identifier 'A1b2' @0 | EndOfFile '' @4"),
        ("a-b", "Identifier 'a' @0 | Minus '-' @1 | Identifier 'b' @2 | EndOfFile '' @3"),
        ("a.b", "Identifier 'a' @0 | Dot '.' @1 | Identifier 'b' @2 | EndOfFile '' @3"),
        ("$.payload.cycleTime", "Dollar '$' @0 | Dot '.' @1 | Identifier 'payload' @2 | Dot '.' @9 | Identifier 'cycleTime' @10 | EndOfFile '' @19"),
        ("$.a.b.c", "Dollar '$' @0 | Dot '.' @1 | Identifier 'a' @2 | Dot '.' @3 | Identifier 'b' @4 | Dot '.' @5 | Identifier 'c' @6 | EndOfFile '' @7"),
        ("$ . a", "Dollar '$' @0 | Dot '.' @2 | Identifier 'a' @4 | EndOfFile '' @5"),
        ("$a", "Dollar '$' @0 | Identifier 'a' @1 | EndOfFile '' @2"),

        // String literals: both quotes, no escapes, no other terminator.
        ("\"abc\"", "StringLiteral 'abc' @0 | EndOfFile '' @5"),
        ("'abc'", "StringLiteral 'abc' @0 | EndOfFile '' @5"),
        ("\"\"", "StringLiteral '' @0 | EndOfFile '' @2"),
        ("''", "StringLiteral '' @0 | EndOfFile '' @2"),
        ("\"it's\"", "StringLiteral 'it\\'s' @0 | EndOfFile '' @6"),
        ("'say \"hi\"'", "StringLiteral 'say \"hi\"' @0 | EndOfFile '' @10"),
        ("\"a\\nb\"", "StringLiteral 'a\\\\nb' @0 | EndOfFile '' @6"),
        ("\"a\nb\"", "StringLiteral 'a\\nb' @0 | EndOfFile '' @5"),
        ("\"a\tb\"", "StringLiteral 'a\\tb' @0 | EndOfFile '' @5"),
        ("\"héllo ☃\"", "StringLiteral 'h\\u00E9llo \\u2603' @0 | EndOfFile '' @9"),
        ("\"smile 😀\"", "StringLiteral 'smile \\uD83D\\uDE00' @0 | EndOfFile '' @10"),
        ("\"a\\\"", "StringLiteral 'a\\\\' @0 | EndOfFile '' @4"),
        ("\"a\\\"b\"", "AelParseException @5: AEL parse error at position 5: unterminated string literal (started at 5)"),
        ("\"abc\" \"def\"", "StringLiteral 'abc' @0 | StringLiteral 'def' @6 | EndOfFile '' @11"),
        ("\"a\"+'b'", "StringLiteral 'a' @0 | Plus '+' @3 | StringLiteral 'b' @4 | EndOfFile '' @7"),
        ("x==\"abc\"", "Identifier 'x' @0 | Equal '==' @1 | StringLiteral 'abc' @3 | EndOfFile '' @8"),
        ("\"  padded  \"", "StringLiteral '  padded  ' @0 | EndOfFile '' @12"),

        // Unterminated strings report where they started.
        ("\"abc", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("'abc", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("\"", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("'", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("x == \"abc", "AelParseException @5: AEL parse error at position 5: unterminated string literal (started at 5)"),
        ("\"a'", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("'a\"", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("\"a\\\"b", "StringLiteral 'a\\\\' @0 | Identifier 'b' @4 | EndOfFile '' @5"),

        // Integers.
        ("0", "IntLiteral '0' @0 | EndOfFile '' @1"),
        ("007", "IntLiteral '007' @0 | EndOfFile '' @3"),
        ("42", "IntLiteral '42' @0 | EndOfFile '' @2"),
        ("9223372036854775807", "IntLiteral '9223372036854775807' @0 | EndOfFile '' @19"),
        ("9223372036854775808", "IntLiteral '9223372036854775808' @0 | EndOfFile '' @19"),
        ("12abc", "IntLiteral '12' @0 | Identifier 'abc' @2 | EndOfFile '' @5"),
        ("1_000", "IntLiteral '1' @0 | Identifier '_000' @1 | EndOfFile '' @5"),
        ("1 2", "IntLiteral '1' @0 | IntLiteral '2' @2 | EndOfFile '' @3"),
        ("1+2", "IntLiteral '1' @0 | Plus '+' @1 | IntLiteral '2' @2 | EndOfFile '' @3"),

        // Decimals: a '.' counts only when a digit follows.
        ("3.14", "DecimalLiteral '3.14' @0 | EndOfFile '' @4"),
        ("0.0", "DecimalLiteral '0.0' @0 | EndOfFile '' @3"),
        ("12.5.3", "DecimalLiteral '12.5' @0 | Dot '.' @4 | IntLiteral '3' @5 | EndOfFile '' @6"),
        ("1.2.3.4", "DecimalLiteral '1.2' @0 | Dot '.' @3 | DecimalLiteral '3.4' @4 | EndOfFile '' @7"),
        ("1.", "IntLiteral '1' @0 | Dot '.' @1 | EndOfFile '' @2"),
        ("100.", "IntLiteral '100' @0 | Dot '.' @3 | EndOfFile '' @4"),
        (".5", "Dot '.' @0 | IntLiteral '5' @1 | EndOfFile '' @2"),
        ("1..2", "IntLiteral '1' @0 | Dot '.' @1 | Dot '.' @2 | IntLiteral '2' @3 | EndOfFile '' @4"),
        ("1.x", "IntLiteral '1' @0 | Dot '.' @1 | Identifier 'x' @2 | EndOfFile '' @3"),
        ("1.5x", "DecimalLiteral '1.5' @0 | Identifier 'x' @3 | EndOfFile '' @4"),
        ("3 .14", "IntLiteral '3' @0 | Dot '.' @2 | IntLiteral '14' @3 | EndOfFile '' @5"),
        ("1.e5", "IntLiteral '1' @0 | Dot '.' @1 | Identifier 'e5' @2 | EndOfFile '' @4"),
        ("$.a.5", "Dollar '$' @0 | Dot '.' @1 | Identifier 'a' @2 | Dot '.' @3 | IntLiteral '5' @4 | EndOfFile '' @5"),

        // Exponents are refused at the 'e' or 'E'.
        ("1e5", "AelParseException @1: AEL parse error at position 1: exponent notation is not supported; write the number out in full"),
        ("1E", "AelParseException @1: AEL parse error at position 1: exponent notation is not supported; write the number out in full"),
        ("1e", "AelParseException @1: AEL parse error at position 1: exponent notation is not supported; write the number out in full"),
        ("1.5e3", "AelParseException @3: AEL parse error at position 3: exponent notation is not supported; write the number out in full"),
        ("1.5E", "AelParseException @3: AEL parse error at position 3: exponent notation is not supported; write the number out in full"),
        ("5e", "AelParseException @1: AEL parse error at position 1: exponent notation is not supported; write the number out in full"),
        ("1 e5", "IntLiteral '1' @0 | Identifier 'e5' @2 | EndOfFile '' @4"),
        ("e5", "Identifier 'e5' @0 | EndOfFile '' @2"),
        ("x + 12e3", "AelParseException @6: AEL parse error at position 6: exponent notation is not supported; write the number out in full"),

        // Characters the language does not have.
        ("@", "AelParseException @0: AEL parse error at position 0: unexpected character \\'@\\'"),
        ("#", "AelParseException @0: AEL parse error at position 0: unexpected character \\'#\\'"),
        ("^", "AelParseException @0: AEL parse error at position 0: unexpected character \\'^\\'"),
        ("~", "AelParseException @0: AEL parse error at position 0: unexpected character \\'~\\'"),
        ("`", "AelParseException @0: AEL parse error at position 0: unexpected character \\'`\\'"),
        ("\\", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\\\\\'"),
        (";", "AelParseException @0: AEL parse error at position 0: unexpected character \\';\\'"),
        (",", "AelParseException @0: AEL parse error at position 0: unexpected character \\',\\'"),
        (":", "AelParseException @0: AEL parse error at position 0: unexpected character \\':\\'"),
        ("?", "AelParseException @0: AEL parse error at position 0: unexpected character \\'?\\'"),
        ("{", "AelParseException @0: AEL parse error at position 0: unexpected character \\'{\\'"),
        ("}", "AelParseException @0: AEL parse error at position 0: unexpected character \\'}\\'"),
        ("[", "AelParseException @0: AEL parse error at position 0: unexpected character \\'[\\'"),
        ("]", "AelParseException @0: AEL parse error at position 0: unexpected character \\']\\'"),
        ("a @ b", "AelParseException @2: AEL parse error at position 2: unexpected character \\'@\\'"),
        ("1 ; 2", "AelParseException @2: AEL parse error at position 2: unexpected character \\';\\'"),
        ("é", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\u00E9\\'"),
        ("café", "AelParseException @3: AEL parse error at position 3: unexpected character \\'\\u00E9\\'"),
        ("٣", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\u0663\\'"),
        ("１", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\uFF11\\'"),
        ("​", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\u200B\\'"),
        ("a\0b", "AelParseException @1: AEL parse error at position 1: unexpected character \\'\\u0000\\'"),
        ("😀", "AelParseException @0: AEL parse error at position 0: unexpected character \\'\\uD83D\\'"),
        ("a 😀", "AelParseException @2: AEL parse error at position 2: unexpected character \\'\\uD83D\\'"),

        // Realistic rule text.
        ("$.payload.cycleTime <= 30 && $.kind == \"PlcCycleStart\"", "Dollar '$' @0 | Dot '.' @1 | Identifier 'payload' @2 | Dot '.' @9 | Identifier 'cycleTime' @10 | LessThanOrEqual '<=' @20 | IntLiteral '30' @23 | AndAnd '&&' @26 | Dollar '$' @29 | Dot '.' @30 | Identifier 'kind' @31 | Equal '==' @36 | StringLiteral 'PlcCycleStart' @39 | EndOfFile '' @54"),
        ("100 - $.payload.cycleTime * 2 / 3 % 4", "IntLiteral '100' @0 | Minus '-' @4 | Dollar '$' @6 | Dot '.' @7 | Identifier 'payload' @8 | Dot '.' @15 | Identifier 'cycleTime' @16 | Star '*' @26 | IntLiteral '2' @28 | Slash '/' @30 | IntLiteral '3' @32 | Percent '%' @34 | IntLiteral '4' @36 | EndOfFile '' @37"),
        ("($.payload.x > 10 || $.payload.x < -10) && !$.payload.suppressed", "LeftParen '(' @0 | Dollar '$' @1 | Dot '.' @2 | Identifier 'payload' @3 | Dot '.' @10 | Identifier 'x' @11 | GreaterThan '>' @13 | IntLiteral '10' @15 | OrOr '||' @18 | Dollar '$' @21 | Dot '.' @22 | Identifier 'payload' @23 | Dot '.' @30 | Identifier 'x' @31 | LessThan '<' @33 | Minus '-' @35 | IntLiteral '10' @36 | RightParen ')' @38 | AndAnd '&&' @40 | Bang '!' @43 | Dollar '$' @44 | Dot '.' @45 | Identifier 'payload' @46 | Dot '.' @53 | Identifier 'suppressed' @54 | EndOfFile '' @64"),
        (
            "$.payload.cycleTime <= 30\n  && $.kind == \"PlcCycleStart\"\r\n  || ($.payload.x > 10.5 && !$.payload.suppressed)\n\t&& $.payload.note contains 'defect'",
            "Dollar '$' @0 | Dot '.' @1 | Identifier 'payload' @2 | Dot '.' @9 | Identifier 'cycleTime' @10 | LessThanOrEqual '<=' @20 | IntLiteral '30' @23 | AndAnd '&&' @28 | Dollar '$' @31 | Dot '.' @32 | Identifier 'kind' @33 | Equal '==' @38 | StringLiteral 'PlcCycleStart' @41 | OrOr '||' @60 | LeftParen '(' @63 | Dollar '$' @64 | Dot '.' @65 | Identifier 'payload' @66 | Dot '.' @73 | Identifier 'x' @74 | GreaterThan '>' @76 | DecimalLiteral '10.5' @78 | AndAnd '&&' @83 | Bang '!' @86 | Dollar '$' @87 | Dot '.' @88 | Identifier 'payload' @89 | Dot '.' @96 | Identifier 'suppressed' @97 | RightParen ')' @107 | AndAnd '&&' @110 | Dollar '$' @113 | Dot '.' @114 | Identifier 'payload' @115 | Dot '.' @122 | Identifier 'note' @123 | Contains 'contains' @128 | StringLiteral 'defect' @137 | EndOfFile '' @145"),
        ("$.payload.cycleTime <= 30 && $.payload.note ? 'x'", "AelParseException @44: AEL parse error at position 44: unexpected character \\'?\\'"),
    ];

    public static TheoryData<int> CaseIndexes()
    {
        TheoryData<int> data = [];
        for (var index = 0; index < Cases.Length; index++)
        {
            data.Add(index);
        }

        return data;
    }

    internal static string Render(string source)
    {
        try
        {
            var tokens = AelLexer.Tokenize(source);
            return string.Join(
                " | ",
                tokens.Select(token => $"{token.Kind} '{Escape(token.Lexeme)}' @{token.Position}"));
        }
        catch (AelParseException exception)
        {
            return $"AelParseException @{exception.Position}: {Escape(exception.Message)}";
        }
    }

    internal static string Escape(string text)
    {
        StringBuilder builder = new();
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\'':
                    builder.Append("\\'");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case < ' ' or > '~':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Tokenize_renders_exactly_the_captured_stream_or_refusal(int index)
    {
        var (source, expected) = Cases[index];

        var actual = Render(source);

        actual.ShouldBe(expected, $"source: {Escape(source)}");
    }

    [Fact]
    public void The_corpus_contains_every_token_kind()
    {
        foreach (var kind in Enum.GetNames<AelTokenKind>())
        {
            Cases.ShouldContain(
                @case => @case.Expected.Contains($"{kind} '", StringComparison.Ordinal),
                $"no corpus case lexes to {kind}");
        }
    }

    [Fact]
    public void A_null_source_is_refused_by_the_guard()
    {
        var exception = Should.Throw<ArgumentNullException>(() => AelLexer.Tokenize(null!));

        exception.ParamName.ShouldBe("value");
    }
}
