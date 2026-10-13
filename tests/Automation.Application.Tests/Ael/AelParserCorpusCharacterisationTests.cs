using System.Globalization;
using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AelParser.ParsePrimary</c> gets its parenthesised
/// case extracted. <c>AelParserTests</c> pins fourteen behaviours; none of them
/// pins the position or the words of a refusal, and the extraction has to keep
/// the cursor exactly where it is today because those positions depend on it.
///
/// <para>
/// Each source is paired with the tree the unrefactored parser built, written
/// as an S-expression by the printer in this class, or with the
/// <see cref="AelParseException"/> (position and full message) it threw. The
/// printer names the operator, the literal kind and the literal value; it is
/// written here and shares nothing with the parser. Expected strings are
/// literals captured from the unrefactored code.
/// </para>
/// </summary>
public class AelParserCorpusCharacterisationTests
{
    private static readonly string OverRangeDecimal = "1" + new string('0', 30) + ".0";

    private static readonly string OverRangeInteger = "1" + new string('0', 30);

    private static readonly (string Source, string Expected)[] Cases =
    [
        // Literals.
        ("1", "(int 1)"),
        ("0", "(int 0)"),
        ("007", "(int 7)"),
        ("3.14", "(dec 3.14)"),
        ("3.140", "(dec 3.140)"),
        ("0.0", "(dec 0.0)"),
        ("'x'", "(str 'x')"),
        ("\"\"", "(str '')"),
        ("\"a b\"", "(str 'a b')"),
        ("true", "(bool true)"),
        ("false", "(bool false)"),
        ("9223372036854775807", "(int 9223372036854775807)"),
        ("79228162457264337593543950335", "AelParseException @0: AEL parse error at position 0: integer literal \\'79228162457264337593543950335\\' is out of range; add a decimal point to allow values up to decimal\\'s range"),
        ("79228162457264337593543950335.0", "(dec 79228162457264337593543950335)"),

        // Out-of-range literals fail at the literal, with the parser's own words.
        ("9223372036854775808", "AelParseException @0: AEL parse error at position 0: integer literal \\'9223372036854775808\\' is out of range; add a decimal point to allow values up to decimal\\'s range"),
        ("-9223372036854775808", "AelParseException @1: AEL parse error at position 1: integer literal \\'9223372036854775808\\' is out of range; add a decimal point to allow values up to decimal\\'s range"),
        ("1 + 9223372036854775808", "AelParseException @4: AEL parse error at position 4: integer literal \\'9223372036854775808\\' is out of range; add a decimal point to allow values up to decimal\\'s range"),
        (OverRangeInteger, "AelParseException @0: AEL parse error at position 0: integer literal \\'1000000000000000000000000000000\\' is out of range; add a decimal point to allow values up to decimal\\'s range"),
        (OverRangeDecimal, "AelParseException @0: AEL parse error at position 0: decimal literal \\'1000000000000000000000000000000.0\\' is out of range"),

        // Unary operators.
        ("-1", "(Negate (int 1))"),
        ("- -1", "(Negate (Negate (int 1)))"),
        ("--1", "(Negate (Negate (int 1)))"),
        ("-(1)", "(Negate (int 1))"),
        ("-1.5", "(Negate (dec 1.5))"),
        ("-true", "(Negate (bool true))"),
        ("!true", "(Not (bool true))"),
        ("!!true", "(Not (Not (bool true)))"),
        ("! true", "(Not (bool true))"),
        ("!(true)", "(Not (bool true))"),
        ("!1", "(Not (int 1))"),
        ("!1 == 2", "(Not (Equal (int 1) (int 2)))"),
        ("-1 + 2", "(Add (Negate (int 1)) (int 2))"),
        ("-1 * 2", "(Multiply (Negate (int 1)) (int 2))"),
        ("1 - -1", "(Subtract (int 1) (Negate (int 1)))"),

        // Parentheses.
        ("(1)", "(int 1)"),
        ("((1))", "(int 1)"),
        ("(1 + 2) * 3", "(Multiply (Add (int 1) (int 2)) (int 3))"),
        ("(true)", "(bool true)"),
        ("(1", "AelParseException @2: AEL parse error at position 2: expected \\')\\'"),
        ("((1)", "AelParseException @4: AEL parse error at position 4: expected \\')\\'"),
        ("(", "AelParseException @1: AEL parse error at position 1: expected literal, field access, or \\'(\\'; got \\'\\'"),
        (")", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\')\\'"),
        ("()", "AelParseException @1: AEL parse error at position 1: expected literal, field access, or \\'(\\'; got \\')\\'"),
        ("( )", "AelParseException @2: AEL parse error at position 2: expected literal, field access, or \\'(\\'; got \\')\\'"),
        ("(1))", "AelParseException @3: AEL parse error at position 3: unexpected trailing token \\')\\'"),
        ("(1 2)", "AelParseException @3: AEL parse error at position 3: expected \\')\\'"),
        ("(1 + )", "AelParseException @5: AEL parse error at position 5: expected literal, field access, or \\'(\\'; got \\')\\'"),
        ("  (1  ", "AelParseException @6: AEL parse error at position 6: expected \\')\\'"),

        // Nothing to parse, and what follows a complete expression.
        ("", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("   ", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 2", "AelParseException @2: AEL parse error at position 2: unexpected trailing token \\'2\\'"),
        ("1 )", "AelParseException @2: AEL parse error at position 2: unexpected trailing token \\')\\'"),
        ("1 true", "AelParseException @2: AEL parse error at position 2: unexpected trailing token \\'true\\'"),
        ("1 + 2 )", "AelParseException @6: AEL parse error at position 6: unexpected trailing token \\')\\'"),
        ("'a' 'b'", "AelParseException @4: AEL parse error at position 4: unexpected trailing token \\'b\\'"),
        ("a", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'a\\'"),
        ("a b", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'a\\'"),
        ("contains", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'contains\\'"),
        ("1 contains", "AelParseException @10: AEL parse error at position 10: expected literal, field access, or \\'(\\'; got \\'\\'"),

        // Field access.
        ("$.a", "(field a)"),
        ("$.a.b.c", "(field a b c)"),
        ("$ .a", "(field a)"),
        ("$ . a", "(field a)"),
        ("$", "AelParseException @1: AEL parse error at position 1: expected \\'.\\' after \\'$\\' to begin a field path"),
        ("$.", "AelParseException @2: AEL parse error at position 2: expected identifier after \\'.\\' in field path; got \\'\\'"),
        ("$.1", "AelParseException @2: AEL parse error at position 2: expected identifier after \\'.\\' in field path; got \\'1\\'"),
        ("$.a.", "AelParseException @4: AEL parse error at position 4: expected identifier after \\'.\\' in field path; got \\'\\'"),
        ("$.a..b", "AelParseException @4: AEL parse error at position 4: expected identifier after \\'.\\' in field path; got \\'.\\'"),
        ("$a", "AelParseException @1: AEL parse error at position 1: expected \\'.\\' after \\'$\\' to begin a field path"),
        ("$.a b", "AelParseException @4: AEL parse error at position 4: unexpected trailing token \\'b\\'"),
        ("$.true", "AelParseException @2: AEL parse error at position 2: expected identifier after \\'.\\' in field path; got \\'true\\'"),
        ("$.contains", "AelParseException @2: AEL parse error at position 2: expected identifier after \\'.\\' in field path; got \\'contains\\'"),
        ("$.a.contains", "AelParseException @4: AEL parse error at position 4: expected identifier after \\'.\\' in field path; got \\'contains\\'"),
        ("$.'a'", "AelParseException @2: AEL parse error at position 2: expected identifier after \\'.\\' in field path; got \\'a\\'"),
        ("1 + $", "AelParseException @5: AEL parse error at position 5: expected \\'.\\' after \\'$\\' to begin a field path"),
        ("$ + 1", "AelParseException @2: AEL parse error at position 2: expected \\'.\\' after \\'$\\' to begin a field path"),

        // Precedence and associativity, lowest to highest.
        ("1 + 2 * 3", "(Add (int 1) (Multiply (int 2) (int 3)))"),
        ("1 * 2 + 3", "(Add (Multiply (int 1) (int 2)) (int 3))"),
        ("1 - 2 - 3", "(Subtract (Subtract (int 1) (int 2)) (int 3))"),
        ("1 / 2 / 3", "(Divide (Divide (int 1) (int 2)) (int 3))"),
        ("8 % 3 * 2", "(Multiply (Modulo (int 8) (int 3)) (int 2))"),
        ("8 * 3 % 2", "(Modulo (Multiply (int 8) (int 3)) (int 2))"),
        ("1 + 2 == 3", "(Equal (Add (int 1) (int 2)) (int 3))"),
        ("1 + 2 < 3 * 4", "(LessThan (Add (int 1) (int 2)) (Multiply (int 3) (int 4)))"),
        ("1 == 2 == 3", "AelParseException @7: AEL parse error at position 7: unexpected trailing token \\'==\\'"),
        ("1 < 2 < 3", "AelParseException @6: AEL parse error at position 6: unexpected trailing token \\'<\\'"),
        ("1 == 1 && 2 == 2", "(And (Equal (int 1) (int 1)) (Equal (int 2) (int 2)))"),
        ("true || false && false", "(Or (bool true) (And (bool false) (bool false)))"),
        ("true && false || true", "(Or (And (bool true) (bool false)) (bool true))"),
        ("true || false || true", "(Or (Or (bool true) (bool false)) (bool true))"),
        ("true && false && true", "(And (And (bool true) (bool false)) (bool true))"),
        ("!true && false", "(And (Not (bool true)) (bool false))"),
        ("!true || !false", "(Or (Not (bool true)) (Not (bool false)))"),
        ("1 < 2", "(LessThan (int 1) (int 2))"),
        ("1 <= 2", "(LessThanOrEqual (int 1) (int 2))"),
        ("1 > 2", "(GreaterThan (int 1) (int 2))"),
        ("1 >= 2", "(GreaterThanOrEqual (int 1) (int 2))"),
        ("1 == 2", "(Equal (int 1) (int 2))"),
        ("1 != 2", "(NotEqual (int 1) (int 2))"),
        ("1 contains 2", "(Contains (int 1) (int 2))"),
        ("'a' contains 'b'", "(Contains (str 'a') (str 'b'))"),
        ("'a' contains 'b' contains 'c'", "AelParseException @17: AEL parse error at position 17: unexpected trailing token \\'contains\\'"),
        ("$.a contains 'x' && $.b > 1", "(And (Contains (field a) (str 'x')) (GreaterThan (field b) (int 1)))"),

        // A dangling or leading operator.
        ("1 +", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 -", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 *", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 /", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 %", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("+ 1", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'+\\'"),
        ("* 2", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'*\\'"),
        ("1 * * 2", "AelParseException @4: AEL parse error at position 4: expected literal, field access, or \\'(\\'; got \\'*\\'"),
        ("1 &&", "AelParseException @4: AEL parse error at position 4: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("&& 1", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'&&\\'"),
        ("|| true", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'||\\'"),
        ("true ||", "AelParseException @7: AEL parse error at position 7: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("true &&", "AelParseException @7: AEL parse error at position 7: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("!", "AelParseException @1: AEL parse error at position 1: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("-", "AelParseException @1: AEL parse error at position 1: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 ==", "AelParseException @4: AEL parse error at position 4: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("== 1", "AelParseException @0: AEL parse error at position 0: expected literal, field access, or \\'(\\'; got \\'==\\'"),
        ("1 !=", "AelParseException @4: AEL parse error at position 4: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 <", "AelParseException @3: AEL parse error at position 3: expected literal, field access, or \\'(\\'; got \\'\\'"),
        ("1 >=", "AelParseException @4: AEL parse error at position 4: expected literal, field access, or \\'(\\'; got \\'\\'"),

        // The lexer's refusals surface unchanged.
        ("1e5", "AelParseException @1: AEL parse error at position 1: exponent notation is not supported; write the number out in full"),
        ("a = 1", "AelParseException @2: AEL parse error at position 2: expected \\'==\\' (single \\'=\\' is not an operator)"),
        ("'abc", "AelParseException @0: AEL parse error at position 0: unterminated string literal (started at 0)"),
        ("@", "AelParseException @0: AEL parse error at position 0: unexpected character \\'@\\'"),
        ("1 & 2", "AelParseException @2: AEL parse error at position 2: expected \\'&&\\'"),
        ("1 | 2", "AelParseException @2: AEL parse error at position 2: expected \\'||\\'"),

        // Realistic rule text.
        ("$.payload.cycleTime <= 30 && $.kind == \"PlcCycleStart\"", "(And (LessThanOrEqual (field payload cycleTime) (int 30)) (Equal (field kind) (str 'PlcCycleStart')))"),
        ("100 - $.payload.cycleTime * 2", "(Subtract (int 100) (Multiply (field payload cycleTime) (int 2)))"),
        ("$.payload.note contains \"defect\"", "(Contains (field payload note) (str 'defect'))"),
        ("1 + 2 * 3 == 7", "(Equal (Add (int 1) (Multiply (int 2) (int 3))) (int 7))"),
        ("($.payload.x > 10 || $.payload.x < -10) && !$.payload.suppressed", "(And (Or (GreaterThan (field payload x) (int 10)) (LessThan (field payload x) (Negate (int 10)))) (Not (field payload suppressed)))"),
        ("$.payload.cycleTime <= 30\n  && ($.kind == 'a'\r\n || $.kind == 'b')", "(And (LessThanOrEqual (field payload cycleTime) (int 30)) (Or (Equal (field kind) (str 'a')) (Equal (field kind) (str 'b'))))"),
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
            return Print(AelParser.Parse(source));
        }
        catch (AelParseException exception)
        {
            return $"AelParseException @{exception.Position}: {AelLexerCorpusCharacterisationTests.Escape(exception.Message)}";
        }
    }

    private static string Print(AelExpression expression) =>
        expression switch
        {
            AelExpression.Literal literal => $"({PrintValue(literal.Value)})",
            AelExpression.FieldAccess fieldAccess => $"(field {string.Join(' ', fieldAccess.Segments)})",
            AelExpression.Binary binary => $"({binary.Operator} {Print(binary.Left)} {Print(binary.Right)})",
            AelExpression.Logical logical => $"({logical.Operator} {Print(logical.Left)} {Print(logical.Right)})",
            AelExpression.Unary unary => $"({unary.Operator} {Print(unary.Operand)})",
            _ => throw new InvalidOperationException($"unprintable node {expression.GetType().Name}"),
        };

    private static string PrintValue(AelValue value) =>
        value switch
        {
            AelValue.IntValue intValue => $"int {intValue.Value.ToString(CultureInfo.InvariantCulture)}",
            AelValue.DecimalValue decimalValue => $"dec {decimalValue.Value.ToString(CultureInfo.InvariantCulture)}",
            AelValue.StringValue stringValue => $"str '{AelLexerCorpusCharacterisationTests.Escape(stringValue.Value)}'",
            AelValue.BoolValue boolValue => boolValue.Value ? "bool true" : "bool false",
            _ => throw new InvalidOperationException($"unprintable value {value.GetType().Name}"),
        };

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Parse_renders_exactly_the_captured_tree_or_refusal(int index)
    {
        var (source, expected) = Cases[index];

        var actual = Render(source);

        actual.ShouldBe(expected, $"source: {AelLexerCorpusCharacterisationTests.Escape(source)}");
    }
}
