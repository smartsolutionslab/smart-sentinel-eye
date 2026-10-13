using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>AelInterpreter.EvalBinary</c> routes its ordered
/// operators through a helper. <c>AelInterpreterTests</c> exercises a handful
/// of operators on well-typed operands; the dispatch has twelve operators and
/// three families (<c>contains</c>/equality, arithmetic, ordering), and a
/// helper that routed one operator to the wrong family would pass them all.
///
/// <para>
/// The matrix evaluates every <see cref="BinaryOperator"/> over every ordered
/// pair of six operands (int, decimal, numeric string, non-numeric string,
/// bool, null) and pins the value and its type, or the exception type and
/// full message. Each expected row is a literal captured from the unrefactored
/// interpreter. Both operands are evaluated before the operator is looked at,
/// so a division by zero on either side beats every other outcome; that, the
/// order of the two evaluations and the out-of-range operator text are pinned
/// separately below.
/// </para>
/// </summary>
public class AelBinaryOperatorCharacterisationTests
{
    private static readonly (string Label, AelExpression Expression)[] Operands =
    [
        ("7", new AelExpression.Literal(new AelValue.IntValue(7))),
        ("2.5", new AelExpression.Literal(new AelValue.DecimalValue(2.5m))),
        ("'3'", new AelExpression.Literal(new AelValue.StringValue("3"))),
        ("'abc'", new AelExpression.Literal(new AelValue.StringValue("abc"))),
        ("true", new AelExpression.Literal(new AelValue.BoolValue(true))),
        ("null", new AelExpression.FieldAccess(["missing"])),
    ];

    private static readonly (BinaryOperator Operator, string Expected)[] Matrix =
    [
        (BinaryOperator.Add, """
            7 Add 7 => int 14
            7 Add 2.5 => dec 9.5
            7 Add '3' => dec 10
            7 Add 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 Add true => InvalidOperationException: cannot coerce BoolValue to a number
            7 Add null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 Add 7 => dec 9.5
            2.5 Add 2.5 => dec 5.0
            2.5 Add '3' => dec 5.5
            2.5 Add 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 Add true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 Add null => InvalidOperationException: cannot coerce NullValue to a number
            '3' Add 7 => dec 10
            '3' Add 2.5 => dec 5.5
            '3' Add '3' => dec 6
            '3' Add 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' Add true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' Add null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' Add 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Add 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Add '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Add 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Add true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Add null => InvalidOperationException: cannot coerce StringValue to a number
            true Add 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true Add 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true Add '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true Add 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true Add true => InvalidOperationException: cannot coerce BoolValue to a number
            true Add null => InvalidOperationException: cannot coerce BoolValue to a number
            null Add 7 => InvalidOperationException: cannot coerce NullValue to a number
            null Add 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null Add '3' => InvalidOperationException: cannot coerce NullValue to a number
            null Add 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null Add true => InvalidOperationException: cannot coerce NullValue to a number
            null Add null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Subtract, """
            7 Subtract 7 => int 0
            7 Subtract 2.5 => dec 4.5
            7 Subtract '3' => dec 4
            7 Subtract 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 Subtract true => InvalidOperationException: cannot coerce BoolValue to a number
            7 Subtract null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 Subtract 7 => dec -4.5
            2.5 Subtract 2.5 => dec 0.0
            2.5 Subtract '3' => dec -0.5
            2.5 Subtract 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 Subtract true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 Subtract null => InvalidOperationException: cannot coerce NullValue to a number
            '3' Subtract 7 => dec -4
            '3' Subtract 2.5 => dec 0.5
            '3' Subtract '3' => dec 0
            '3' Subtract 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' Subtract true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' Subtract null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' Subtract 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Subtract 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Subtract '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Subtract 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Subtract true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Subtract null => InvalidOperationException: cannot coerce StringValue to a number
            true Subtract 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true Subtract 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true Subtract '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true Subtract 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true Subtract true => InvalidOperationException: cannot coerce BoolValue to a number
            true Subtract null => InvalidOperationException: cannot coerce BoolValue to a number
            null Subtract 7 => InvalidOperationException: cannot coerce NullValue to a number
            null Subtract 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null Subtract '3' => InvalidOperationException: cannot coerce NullValue to a number
            null Subtract 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null Subtract true => InvalidOperationException: cannot coerce NullValue to a number
            null Subtract null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Multiply, """
            7 Multiply 7 => int 49
            7 Multiply 2.5 => dec 17.5
            7 Multiply '3' => dec 21
            7 Multiply 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 Multiply true => InvalidOperationException: cannot coerce BoolValue to a number
            7 Multiply null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 Multiply 7 => dec 17.5
            2.5 Multiply 2.5 => dec 6.25
            2.5 Multiply '3' => dec 7.5
            2.5 Multiply 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 Multiply true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 Multiply null => InvalidOperationException: cannot coerce NullValue to a number
            '3' Multiply 7 => dec 21
            '3' Multiply 2.5 => dec 7.5
            '3' Multiply '3' => dec 9
            '3' Multiply 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' Multiply true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' Multiply null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' Multiply 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Multiply 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Multiply '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Multiply 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Multiply true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Multiply null => InvalidOperationException: cannot coerce StringValue to a number
            true Multiply 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true Multiply 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true Multiply '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true Multiply 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true Multiply true => InvalidOperationException: cannot coerce BoolValue to a number
            true Multiply null => InvalidOperationException: cannot coerce BoolValue to a number
            null Multiply 7 => InvalidOperationException: cannot coerce NullValue to a number
            null Multiply 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null Multiply '3' => InvalidOperationException: cannot coerce NullValue to a number
            null Multiply 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null Multiply true => InvalidOperationException: cannot coerce NullValue to a number
            null Multiply null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Divide, """
            7 Divide 7 => int 1
            7 Divide 2.5 => dec 2.8
            7 Divide '3' => dec 2.3333333333333333333333333333
            7 Divide 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 Divide true => InvalidOperationException: cannot coerce BoolValue to a number
            7 Divide null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 Divide 7 => dec 0.3571428571428571428571428571
            2.5 Divide 2.5 => dec 1
            2.5 Divide '3' => dec 0.8333333333333333333333333333
            2.5 Divide 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 Divide true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 Divide null => InvalidOperationException: cannot coerce NullValue to a number
            '3' Divide 7 => dec 0.4285714285714285714285714286
            '3' Divide 2.5 => dec 1.2
            '3' Divide '3' => dec 1
            '3' Divide 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' Divide true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' Divide null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' Divide 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Divide 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Divide '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Divide 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Divide true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Divide null => InvalidOperationException: cannot coerce StringValue to a number
            true Divide 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true Divide 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true Divide '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true Divide 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true Divide true => InvalidOperationException: cannot coerce BoolValue to a number
            true Divide null => InvalidOperationException: cannot coerce BoolValue to a number
            null Divide 7 => InvalidOperationException: cannot coerce NullValue to a number
            null Divide 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null Divide '3' => InvalidOperationException: cannot coerce NullValue to a number
            null Divide 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null Divide true => InvalidOperationException: cannot coerce NullValue to a number
            null Divide null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Modulo, """
            7 Modulo 7 => int 0
            7 Modulo 2.5 => dec 2.0
            7 Modulo '3' => dec 1
            7 Modulo 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 Modulo true => InvalidOperationException: cannot coerce BoolValue to a number
            7 Modulo null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 Modulo 7 => dec 2.5
            2.5 Modulo 2.5 => dec 0.0
            2.5 Modulo '3' => dec 2.5
            2.5 Modulo 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 Modulo true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 Modulo null => InvalidOperationException: cannot coerce NullValue to a number
            '3' Modulo 7 => dec 3
            '3' Modulo 2.5 => dec 0.5
            '3' Modulo '3' => dec 0
            '3' Modulo 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' Modulo true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' Modulo null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' Modulo 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Modulo 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Modulo '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Modulo 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Modulo true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' Modulo null => InvalidOperationException: cannot coerce StringValue to a number
            true Modulo 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true Modulo 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true Modulo '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true Modulo 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true Modulo true => InvalidOperationException: cannot coerce BoolValue to a number
            true Modulo null => InvalidOperationException: cannot coerce BoolValue to a number
            null Modulo 7 => InvalidOperationException: cannot coerce NullValue to a number
            null Modulo 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null Modulo '3' => InvalidOperationException: cannot coerce NullValue to a number
            null Modulo 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null Modulo true => InvalidOperationException: cannot coerce NullValue to a number
            null Modulo null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Equal, """
            7 Equal 7 => bool true
            7 Equal 2.5 => bool false
            7 Equal '3' => bool false
            7 Equal 'abc' => bool false
            7 Equal true => bool false
            7 Equal null => bool false
            2.5 Equal 7 => bool false
            2.5 Equal 2.5 => bool true
            2.5 Equal '3' => bool false
            2.5 Equal 'abc' => bool false
            2.5 Equal true => bool false
            2.5 Equal null => bool false
            '3' Equal 7 => bool false
            '3' Equal 2.5 => bool false
            '3' Equal '3' => bool true
            '3' Equal 'abc' => bool false
            '3' Equal true => bool false
            '3' Equal null => bool false
            'abc' Equal 7 => bool false
            'abc' Equal 2.5 => bool false
            'abc' Equal '3' => bool false
            'abc' Equal 'abc' => bool true
            'abc' Equal true => bool false
            'abc' Equal null => bool false
            true Equal 7 => bool false
            true Equal 2.5 => bool false
            true Equal '3' => bool false
            true Equal 'abc' => bool false
            true Equal true => bool true
            true Equal null => bool false
            null Equal 7 => bool false
            null Equal 2.5 => bool false
            null Equal '3' => bool false
            null Equal 'abc' => bool false
            null Equal true => bool false
            null Equal null => bool true
            """),
        (BinaryOperator.NotEqual, """
            7 NotEqual 7 => bool false
            7 NotEqual 2.5 => bool true
            7 NotEqual '3' => bool true
            7 NotEqual 'abc' => bool true
            7 NotEqual true => bool true
            7 NotEqual null => bool true
            2.5 NotEqual 7 => bool true
            2.5 NotEqual 2.5 => bool false
            2.5 NotEqual '3' => bool true
            2.5 NotEqual 'abc' => bool true
            2.5 NotEqual true => bool true
            2.5 NotEqual null => bool true
            '3' NotEqual 7 => bool true
            '3' NotEqual 2.5 => bool true
            '3' NotEqual '3' => bool false
            '3' NotEqual 'abc' => bool true
            '3' NotEqual true => bool true
            '3' NotEqual null => bool true
            'abc' NotEqual 7 => bool true
            'abc' NotEqual 2.5 => bool true
            'abc' NotEqual '3' => bool true
            'abc' NotEqual 'abc' => bool false
            'abc' NotEqual true => bool true
            'abc' NotEqual null => bool true
            true NotEqual 7 => bool true
            true NotEqual 2.5 => bool true
            true NotEqual '3' => bool true
            true NotEqual 'abc' => bool true
            true NotEqual true => bool false
            true NotEqual null => bool true
            null NotEqual 7 => bool true
            null NotEqual 2.5 => bool true
            null NotEqual '3' => bool true
            null NotEqual 'abc' => bool true
            null NotEqual true => bool true
            null NotEqual null => bool false
            """),
        (BinaryOperator.LessThan, """
            7 LessThan 7 => bool false
            7 LessThan 2.5 => bool false
            7 LessThan '3' => bool false
            7 LessThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 LessThan true => InvalidOperationException: cannot coerce BoolValue to a number
            7 LessThan null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 LessThan 7 => bool true
            2.5 LessThan 2.5 => bool false
            2.5 LessThan '3' => bool true
            2.5 LessThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 LessThan true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 LessThan null => InvalidOperationException: cannot coerce NullValue to a number
            '3' LessThan 7 => bool true
            '3' LessThan 2.5 => bool false
            '3' LessThan '3' => bool false
            '3' LessThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' LessThan true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' LessThan null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' LessThan 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThan 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThan '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThan true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThan null => InvalidOperationException: cannot coerce StringValue to a number
            true LessThan 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThan 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThan '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThan 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThan true => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThan null => InvalidOperationException: cannot coerce BoolValue to a number
            null LessThan 7 => InvalidOperationException: cannot coerce NullValue to a number
            null LessThan 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null LessThan '3' => InvalidOperationException: cannot coerce NullValue to a number
            null LessThan 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null LessThan true => InvalidOperationException: cannot coerce NullValue to a number
            null LessThan null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.LessThanOrEqual, """
            7 LessThanOrEqual 7 => bool true
            7 LessThanOrEqual 2.5 => bool false
            7 LessThanOrEqual '3' => bool false
            7 LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 LessThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            7 LessThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 LessThanOrEqual 7 => bool true
            2.5 LessThanOrEqual 2.5 => bool true
            2.5 LessThanOrEqual '3' => bool true
            2.5 LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 LessThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 LessThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            '3' LessThanOrEqual 7 => bool true
            '3' LessThanOrEqual 2.5 => bool false
            '3' LessThanOrEqual '3' => bool true
            '3' LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' LessThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' LessThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' LessThanOrEqual 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThanOrEqual 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThanOrEqual '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThanOrEqual true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' LessThanOrEqual null => InvalidOperationException: cannot coerce StringValue to a number
            true LessThanOrEqual 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThanOrEqual 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThanOrEqual '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            true LessThanOrEqual null => InvalidOperationException: cannot coerce BoolValue to a number
            null LessThanOrEqual 7 => InvalidOperationException: cannot coerce NullValue to a number
            null LessThanOrEqual 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null LessThanOrEqual '3' => InvalidOperationException: cannot coerce NullValue to a number
            null LessThanOrEqual 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null LessThanOrEqual true => InvalidOperationException: cannot coerce NullValue to a number
            null LessThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.GreaterThan, """
            7 GreaterThan 7 => bool false
            7 GreaterThan 2.5 => bool true
            7 GreaterThan '3' => bool true
            7 GreaterThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 GreaterThan true => InvalidOperationException: cannot coerce BoolValue to a number
            7 GreaterThan null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 GreaterThan 7 => bool false
            2.5 GreaterThan 2.5 => bool false
            2.5 GreaterThan '3' => bool false
            2.5 GreaterThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 GreaterThan true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 GreaterThan null => InvalidOperationException: cannot coerce NullValue to a number
            '3' GreaterThan 7 => bool false
            '3' GreaterThan 2.5 => bool true
            '3' GreaterThan '3' => bool false
            '3' GreaterThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' GreaterThan true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' GreaterThan null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' GreaterThan 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThan 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThan '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThan 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThan true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThan null => InvalidOperationException: cannot coerce StringValue to a number
            true GreaterThan 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThan 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThan '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThan 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThan true => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThan null => InvalidOperationException: cannot coerce BoolValue to a number
            null GreaterThan 7 => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThan 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThan '3' => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThan 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThan true => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThan null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.GreaterThanOrEqual, """
            7 GreaterThanOrEqual 7 => bool true
            7 GreaterThanOrEqual 2.5 => bool true
            7 GreaterThanOrEqual '3' => bool true
            7 GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            7 GreaterThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            7 GreaterThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            2.5 GreaterThanOrEqual 7 => bool false
            2.5 GreaterThanOrEqual 2.5 => bool true
            2.5 GreaterThanOrEqual '3' => bool false
            2.5 GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            2.5 GreaterThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            2.5 GreaterThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            '3' GreaterThanOrEqual 7 => bool false
            '3' GreaterThanOrEqual 2.5 => bool true
            '3' GreaterThanOrEqual '3' => bool true
            '3' GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            '3' GreaterThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            '3' GreaterThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            'abc' GreaterThanOrEqual 7 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThanOrEqual 2.5 => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThanOrEqual '3' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThanOrEqual true => InvalidOperationException: cannot coerce StringValue to a number
            'abc' GreaterThanOrEqual null => InvalidOperationException: cannot coerce StringValue to a number
            true GreaterThanOrEqual 7 => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThanOrEqual 2.5 => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThanOrEqual '3' => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThanOrEqual true => InvalidOperationException: cannot coerce BoolValue to a number
            true GreaterThanOrEqual null => InvalidOperationException: cannot coerce BoolValue to a number
            null GreaterThanOrEqual 7 => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThanOrEqual 2.5 => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThanOrEqual '3' => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThanOrEqual 'abc' => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThanOrEqual true => InvalidOperationException: cannot coerce NullValue to a number
            null GreaterThanOrEqual null => InvalidOperationException: cannot coerce NullValue to a number
            """),
        (BinaryOperator.Contains, """
            7 Contains 7 => InvalidOperationException: 'contains' left side must be string; got IntValue
            7 Contains 2.5 => InvalidOperationException: 'contains' left side must be string; got IntValue
            7 Contains '3' => InvalidOperationException: 'contains' left side must be string; got IntValue
            7 Contains 'abc' => InvalidOperationException: 'contains' left side must be string; got IntValue
            7 Contains true => InvalidOperationException: 'contains' left side must be string; got IntValue
            7 Contains null => InvalidOperationException: 'contains' left side must be string; got IntValue
            2.5 Contains 7 => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            2.5 Contains 2.5 => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            2.5 Contains '3' => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            2.5 Contains 'abc' => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            2.5 Contains true => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            2.5 Contains null => InvalidOperationException: 'contains' left side must be string; got DecimalValue
            '3' Contains 7 => InvalidOperationException: 'contains' right side must be string; got IntValue
            '3' Contains 2.5 => InvalidOperationException: 'contains' right side must be string; got DecimalValue
            '3' Contains '3' => bool true
            '3' Contains 'abc' => bool false
            '3' Contains true => InvalidOperationException: 'contains' right side must be string; got BoolValue
            '3' Contains null => InvalidOperationException: 'contains' right side must be string; got NullValue
            'abc' Contains 7 => InvalidOperationException: 'contains' right side must be string; got IntValue
            'abc' Contains 2.5 => InvalidOperationException: 'contains' right side must be string; got DecimalValue
            'abc' Contains '3' => bool false
            'abc' Contains 'abc' => bool true
            'abc' Contains true => InvalidOperationException: 'contains' right side must be string; got BoolValue
            'abc' Contains null => InvalidOperationException: 'contains' right side must be string; got NullValue
            true Contains 7 => InvalidOperationException: 'contains' left side must be string; got BoolValue
            true Contains 2.5 => InvalidOperationException: 'contains' left side must be string; got BoolValue
            true Contains '3' => InvalidOperationException: 'contains' left side must be string; got BoolValue
            true Contains 'abc' => InvalidOperationException: 'contains' left side must be string; got BoolValue
            true Contains true => InvalidOperationException: 'contains' left side must be string; got BoolValue
            true Contains null => InvalidOperationException: 'contains' left side must be string; got BoolValue
            null Contains 7 => InvalidOperationException: 'contains' left side must be string; got NullValue
            null Contains 2.5 => InvalidOperationException: 'contains' left side must be string; got NullValue
            null Contains '3' => InvalidOperationException: 'contains' left side must be string; got NullValue
            null Contains 'abc' => InvalidOperationException: 'contains' left side must be string; got NullValue
            null Contains true => InvalidOperationException: 'contains' left side must be string; got NullValue
            null Contains null => InvalidOperationException: 'contains' left side must be string; got NullValue
            """),
    ];

    private static readonly (string Source, string Expected)[] Sources =
    [
        // Zero divisors, in every numeric pairing.
        ("7 / 0", "InvalidOperationException: division by zero"),
        ("7 / 0.0", "InvalidOperationException: division by zero"),
        ("2.5 / 0", "InvalidOperationException: division by zero"),
        ("2.5 / 0.0", "InvalidOperationException: division by zero"),
        ("0 / 7", "int 0"),
        ("0.0 / 7", "dec 0.0"),
        ("7 % 0", "InvalidOperationException: modulo by zero"),
        ("7 % 0.0", "InvalidOperationException: modulo by zero"),
        ("2.5 % 0", "InvalidOperationException: modulo by zero"),
        ("'3' / 0", "InvalidOperationException: division by zero"),
        ("'3' % 0", "InvalidOperationException: modulo by zero"),

        // Both operands are evaluated before the operator is dispatched.
        ("1 == 1 / 0", "InvalidOperationException: division by zero"),
        ("1 != 1 / 0", "InvalidOperationException: division by zero"),
        ("1 < 1 % 0", "InvalidOperationException: modulo by zero"),
        ("1 + 1 / 0", "InvalidOperationException: division by zero"),
        ("'a' contains (1 / 0)", "InvalidOperationException: division by zero"),
        ("(1 / 0) == 1", "InvalidOperationException: division by zero"),
        ("true == (1 / 0)", "InvalidOperationException: division by zero"),
        ("(1 / 0) == (1 % 0)", "InvalidOperationException: division by zero"),
        ("(1 % 0) + (1 / 0)", "InvalidOperationException: modulo by zero"),

        // Integer and decimal arithmetic, promotion and scale.
        ("1 / 3", "int 0"),
        ("1.0 / 3", "dec 0.3333333333333333333333333333"),
        ("7 % 3", "int 1"),
        ("-7 % 3", "int -1"),
        ("7 % -3", "int 1"),
        ("7.5 % 2", "dec 1.5"),
        ("1.10 + 1.2", "dec 2.30"),
        ("2 * 3.5", "dec 7.0"),
        ("1 + '2'", "dec 3"),
        ("'1' + '2'", "dec 3"),
        ("'1.50' * 2", "dec 3.00"),
        ("9223372036854775807 + 0.5", "dec 9223372036854775807.5"),
        ("9223372036854775807 + 1", "int -9223372036854775808"),
        ("9223372036854775807 * 2", "int -2"),
        ("-9223372036854775807 - 2", "int 9223372036854775807"),
        ("79228162457264337593543950335.0 * 2", "OverflowException"),

        // Ordering coerces numeric strings; equality does not.
        ("'10' < 9", "bool false"),
        ("9 < '10'", "bool true"),
        ("'abc' < 1", "InvalidOperationException: cannot coerce StringValue to a number"),
        ("1 < 'abc'", "InvalidOperationException: cannot coerce StringValue to a number"),
        ("true < 1", "InvalidOperationException: cannot coerce BoolValue to a number"),
        ("1 <= 1.0", "bool true"),
        ("2.5 >= 2.5", "bool true"),
        ("1 == 1.0", "bool true"),
        ("1.0 == 1", "bool true"),
        ("1 == '1'", "bool false"),
        ("'1' == '1'", "bool true"),
        ("true == true", "bool true"),
        ("true != false", "bool true"),
        ("$.missing == $.alsoMissing", "bool true"),
        ("$.missing != $.alsoMissing", "bool false"),
        ("$.missing == 1", "bool false"),

        // contains is ordinal and string-only.
        ("'abc' contains 'b'", "bool true"),
        ("'abc' contains 'B'", "bool false"),
        ("'abc' contains ''", "bool true"),
        ("'' contains 'a'", "bool false"),
        ("'abc' contains 1", "InvalidOperationException: 'contains' right side must be string; got IntValue"),
        ("1 contains 'a'", "InvalidOperationException: 'contains' left side must be string; got IntValue"),
        ("$.missing contains 'a'", "InvalidOperationException: 'contains' left side must be string; got NullValue"),
        ("'a' contains $.missing", "InvalidOperationException: 'contains' right side must be string; got NullValue"),
    ];

    public static TheoryData<int> MatrixIndexes()
    {
        TheoryData<int> data = [];
        for (var index = 0; index < Matrix.Length; index++)
        {
            data.Add(index);
        }

        return data;
    }

    public static TheoryData<int> SourceIndexes()
    {
        TheoryData<int> data = [];
        for (var index = 0; index < Sources.Length; index++)
        {
            data.Add(index);
        }

        return data;
    }

    // The interpreter authors the text of its own InvalidOperationExceptions, so those are pinned in full;
    // anything else (an OverflowException from decimal arithmetic) carries wording the runtime owns, so
    // only its type is pinned.
    internal static string Outcome(Func<AelValue> evaluate)
    {
        try
        {
            return evaluate() switch
            {
                AelValue.IntValue intValue => $"int {intValue.Value.ToString(CultureInfo.InvariantCulture)}",
                AelValue.DecimalValue decimalValue => $"dec {decimalValue.Value.ToString(CultureInfo.InvariantCulture)}",
                AelValue.StringValue stringValue => $"str '{stringValue.Value}'",
                AelValue.BoolValue boolValue => boolValue.Value ? "bool true" : "bool false",
                AelValue.NullValue => "null",
                var other => $"unprintable {other.GetType().Name}",
            };
        }
        catch (Exception exception)
        {
            return exception is InvalidOperationException
                ? $"{exception.GetType().Name}: {exception.Message}"
                : exception.GetType().Name;
        }
    }

    private static AelValue EvaluateSource(string source) =>
        AelInterpreter.Evaluate(AelParser.Parse(source), ContextFor("{}"));

    private static EvaluationContext ContextFor(string json) => new(JsonDocument.Parse(json).RootElement);

    internal static string RenderMatrixRows(BinaryOperator binaryOperator)
    {
        List<string> rows = [];
        foreach (var (leftLabel, leftExpression) in Operands)
        {
            foreach (var (rightLabel, rightExpression) in Operands)
            {
                var expression = new AelExpression.Binary(binaryOperator, leftExpression, rightExpression);
                var outcome = Outcome(() => AelInterpreter.Evaluate(expression, ContextFor("{}")));
                rows.Add($"{leftLabel} {binaryOperator} {rightLabel} => {outcome}");
            }
        }

        return string.Join("\n", rows);
    }

    internal static string RenderSource(string source) => Outcome(() => EvaluateSource(source));

    [Theory]
    [MemberData(nameof(MatrixIndexes))]
    public void Every_operand_pair_evaluates_exactly_as_captured(int index)
    {
        var (binaryOperator, expected) = Matrix[index];

        var actual = RenderMatrixRows(binaryOperator);

        actual.ShouldBe(expected.ReplaceLineEndings("\n"), $"operator: {binaryOperator}");
    }

    [Theory]
    [MemberData(nameof(SourceIndexes))]
    public void Source_expressions_evaluate_exactly_as_captured(int index)
    {
        var (source, expected) = Sources[index];

        var actual = RenderSource(source);

        actual.ShouldBe(expected, $"source: {source}");
    }

    [Fact]
    public void The_matrix_covers_every_binary_operator()
    {
        Matrix.Select(row => row.Operator)
            .OrderBy(binaryOperator => binaryOperator)
            .ShouldBe(Enum.GetValues<BinaryOperator>());
    }

    [Fact]
    public void An_operator_outside_the_enumeration_reports_Unhandled_BinaryOperator_after_both_operands_evaluate()
    {
        var literal = new AelExpression.Literal(new AelValue.IntValue(1));
        var expression = new AelExpression.Binary((BinaryOperator)99, literal, literal);

        var actual = Outcome(() => AelInterpreter.Evaluate(expression, ContextFor("{}")));

        actual.ShouldBe("InvalidOperationException: Unhandled BinaryOperator: 99");
    }

    [Fact]
    public void An_operator_outside_the_enumeration_still_fails_on_a_throwing_left_operand_first()
    {
        var divideByZero = AelParser.Parse("1 / 0");
        var literal = new AelExpression.Literal(new AelValue.IntValue(1));
        var expression = new AelExpression.Binary((BinaryOperator)99, divideByZero, literal);

        var actual = Outcome(() => AelInterpreter.Evaluate(expression, ContextFor("{}")));

        actual.ShouldBe("InvalidOperationException: division by zero");
    }
}
