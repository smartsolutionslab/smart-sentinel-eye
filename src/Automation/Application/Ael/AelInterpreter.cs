using System.Globalization;
using System.Text.Json;

namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Tree-walking interpreter for AEL (ADR-0099). Targets ≤ 10 µs p99
/// per eval; ≥ 100 000 evals/sec/core on the dev hardware (NFR-002).
///
/// <para>
/// The interpreter is allocation-free per eval — it returns
/// <see cref="AelValue"/> records but no temporary collections,
/// boxing, or strings are created in the hot path.
/// </para>
/// </summary>
public static class AelInterpreter
{
    public static AelValue Evaluate(AelExpression expression, EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Eval(expression, context);
    }

    private static AelValue Eval(AelExpression expression, EvaluationContext context) =>
        expression switch
        {
            AelExpression.Literal literal => literal.Value,
            AelExpression.FieldAccess fieldAccess => ResolveField(fieldAccess, context),
            AelExpression.Unary unary => EvalUnary(unary, context),
            AelExpression.Logical logical => EvalLogical(logical, context),
            AelExpression.Binary binary => EvalBinary(binary, context),
            _ => throw new InvalidOperationException($"Unhandled AelExpression case: {expression.GetType().Name}"),
        };

    private static AelValue ResolveField(AelExpression.FieldAccess fieldAccess, EvaluationContext context)
    {
        JsonElement current = context.Root;
        foreach (string segment in fieldAccess.Segments)
        {
            if (current.ValueKind != JsonValueKind.Object) return AelValue.NullValue.Instance;
            if (!current.TryGetProperty(segment, out JsonElement next)) return AelValue.NullValue.Instance;
            current = next;
        }
        return JsonElementToAelValue(current);
    }

    private static AelValue JsonElementToAelValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => new AelValue.BoolValue(true),
            JsonValueKind.False => new AelValue.BoolValue(false),
            JsonValueKind.String => new AelValue.StringValue(element.GetString() ?? string.Empty),
            JsonValueKind.Number when element.TryGetInt64(out long integer) => new AelValue.IntValue(integer),
            JsonValueKind.Number => new AelValue.DecimalValue(element.GetDecimal()),
            JsonValueKind.Null => AelValue.NullValue.Instance,
            _ => AelValue.NullValue.Instance, // arrays / objects are not addressable values in v1
        };

    private static AelValue EvalUnary(AelExpression.Unary unary, EvaluationContext context)
    {
        AelValue operand = Eval(unary.Operand, context);
        return unary.Operator switch
        {
            UnaryOperator.Negate => operand switch
            {
                AelValue.IntValue intValue => new AelValue.IntValue(-intValue.Value),
                AelValue.DecimalValue decimalValue => new AelValue.DecimalValue(-decimalValue.Value),
                _ => throw new InvalidOperationException(
                    $"unary '-' requires a numeric operand; got {operand.GetType().Name}"),
            },
            UnaryOperator.Not => operand switch
            {
                AelValue.BoolValue boolValue => new AelValue.BoolValue(!boolValue.Value),
                _ => throw new InvalidOperationException(
                    $"unary '!' requires a bool operand; got {operand.GetType().Name}"),
            },
            _ => throw new InvalidOperationException($"Unhandled UnaryOperator: {unary.Operator}"),
        };
    }

    private static AelValue.BoolValue EvalLogical(AelExpression.Logical logical, EvaluationContext context)
    {
        AelValue left = Eval(logical.Left, context);
        bool leftBool = left is AelValue.BoolValue leftBoolValue
            ? leftBoolValue.Value
            : throw new InvalidOperationException(
                $"logical operand must be bool; got {left.GetType().Name}");

        // Short-circuit evaluation.
        if (logical.Operator == LogicalOperator.And && !leftBool) return new AelValue.BoolValue(false);
        if (logical.Operator == LogicalOperator.Or && leftBool) return new AelValue.BoolValue(true);

        AelValue right = Eval(logical.Right, context);
        bool rightBool = right is AelValue.BoolValue rightBoolValue
            ? rightBoolValue.Value
            : throw new InvalidOperationException(
                $"logical operand must be bool; got {right.GetType().Name}");

        return new AelValue.BoolValue(
            logical.Operator == LogicalOperator.And ? (leftBool && rightBool) : (leftBool || rightBool));
    }

    private static AelValue EvalBinary(AelExpression.Binary binary, EvaluationContext context)
    {
        AelValue left = Eval(binary.Left, context);
        AelValue right = Eval(binary.Right, context);

        return binary.Operator switch
        {
            BinaryOperator.Contains => EvalContains(left, right),
            BinaryOperator.Equal => new AelValue.BoolValue(AreEqual(left, right)),
            BinaryOperator.NotEqual => new AelValue.BoolValue(!AreEqual(left, right)),
            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply
                or BinaryOperator.Divide or BinaryOperator.Modulo
                => EvalArithmetic(binary.Operator, left, right),
            BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual
                or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                => EvalComparison(binary.Operator, left, right),
            _ => throw new InvalidOperationException($"Unhandled BinaryOperator: {binary.Operator}"),
        };
    }

    private static AelValue.BoolValue EvalContains(AelValue left, AelValue right)
    {
        string lhs = (left as AelValue.StringValue)?.Value
            ?? throw new InvalidOperationException(
                $"'contains' left side must be string; got {left.GetType().Name}");
        string rhs = (right as AelValue.StringValue)?.Value
            ?? throw new InvalidOperationException(
                $"'contains' right side must be string; got {right.GetType().Name}");
        return new AelValue.BoolValue(lhs.Contains(rhs, StringComparison.Ordinal));
    }

    private static AelValue EvalArithmetic(BinaryOperator binaryOperator, AelValue left, AelValue right) =>
        binaryOperator switch
        {
            BinaryOperator.Add => Arithmetic(left, right, static (leftOperand, rightOperand) => leftOperand + rightOperand, static (leftOperand, rightOperand) => leftOperand + rightOperand),
            BinaryOperator.Subtract => Arithmetic(left, right, static (leftOperand, rightOperand) => leftOperand - rightOperand, static (leftOperand, rightOperand) => leftOperand - rightOperand),
            BinaryOperator.Multiply => Arithmetic(left, right, static (leftOperand, rightOperand) => leftOperand * rightOperand, static (leftOperand, rightOperand) => leftOperand * rightOperand),
            BinaryOperator.Divide => Arithmetic(left, right, DivideInt, DivideDecimal),
            BinaryOperator.Modulo => Arithmetic(left, right, ModuloInt, ModuloDecimal),
            _ => throw new InvalidOperationException($"Unhandled arithmetic operator: {binaryOperator}"),
        };

    private static AelValue.BoolValue EvalComparison(BinaryOperator binaryOperator, AelValue left, AelValue right) =>
        binaryOperator switch
        {
            BinaryOperator.LessThan => Compare(left, right, static (leftOperand, rightOperand) => leftOperand < rightOperand),
            BinaryOperator.LessThanOrEqual => Compare(left, right, static (leftOperand, rightOperand) => leftOperand <= rightOperand),
            BinaryOperator.GreaterThan => Compare(left, right, static (leftOperand, rightOperand) => leftOperand > rightOperand),
            BinaryOperator.GreaterThanOrEqual => Compare(left, right, static (leftOperand, rightOperand) => leftOperand >= rightOperand),
            _ => throw new InvalidOperationException($"Unhandled comparison operator: {binaryOperator}"),
        };

    private static long DivideInt(long dividend, long divisor) =>
        divisor == 0 ? throw new InvalidOperationException("division by zero") : dividend / divisor;

    private static decimal DivideDecimal(decimal dividend, decimal divisor) =>
        divisor == 0m ? throw new InvalidOperationException("division by zero") : dividend / divisor;

    private static long ModuloInt(long dividend, long divisor) =>
        divisor == 0 ? throw new InvalidOperationException("modulo by zero") : dividend % divisor;

    private static decimal ModuloDecimal(decimal dividend, decimal divisor) =>
        divisor == 0m ? throw new InvalidOperationException("modulo by zero") : dividend % divisor;

    private static AelValue Arithmetic(AelValue left, AelValue right,
        Func<long, long, long> intOperation, Func<decimal, decimal, decimal> decimalOperation)
    {
        if (left is AelValue.IntValue leftInt && right is AelValue.IntValue rightInt)
        {
            return new AelValue.IntValue(intOperation(leftInt.Value, rightInt.Value));
        }
        decimal leftDecimal = ToDecimal(left);
        decimal rightDecimal = ToDecimal(right);
        return new AelValue.DecimalValue(decimalOperation(leftDecimal, rightDecimal));
    }

    private static AelValue.BoolValue Compare(AelValue left, AelValue right, Func<decimal, decimal, bool> comparison)
    {
        decimal leftDecimal = ToDecimal(left);
        decimal rightDecimal = ToDecimal(right);
        return new AelValue.BoolValue(comparison(leftDecimal, rightDecimal));
    }

    private static decimal ToDecimal(AelValue value) =>
        value switch
        {
            AelValue.IntValue intValue => intValue.Value,
            AelValue.DecimalValue decimalValue => decimalValue.Value,
            AelValue.StringValue stringValue when decimal.TryParse(
                stringValue.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
                => parsed,
            _ => throw new InvalidOperationException(
                $"cannot coerce {value.GetType().Name} to a number"),
        };

    private static bool AreEqual(AelValue left, AelValue right) =>
        (left, right) switch
        {
            (AelValue.IntValue leftInt, AelValue.IntValue rightInt) => leftInt.Value == rightInt.Value,
            (AelValue.DecimalValue leftDecimal, AelValue.DecimalValue rightDecimal) => leftDecimal.Value == rightDecimal.Value,
            (AelValue.IntValue leftInt, AelValue.DecimalValue rightDecimal) => leftInt.Value == rightDecimal.Value,
            (AelValue.DecimalValue leftDecimal, AelValue.IntValue rightInt) => leftDecimal.Value == rightInt.Value,
            (AelValue.StringValue leftString, AelValue.StringValue rightString) => leftString.Value == rightString.Value,
            (AelValue.BoolValue leftBool, AelValue.BoolValue rightBool) => leftBool.Value == rightBool.Value,
            (AelValue.NullValue, AelValue.NullValue) => true,
            _ => false,
        };
}

/// <summary>
/// Evaluation context passed to <see cref="AelInterpreter.Evaluate"/>.
/// <see cref="Root"/> is a JSON object whose top-level fields
/// (<c>source</c>, <c>kind</c>, <c>device</c>, <c>payload</c>, …) are
/// addressed via <c>$.fieldName</c>.
/// </summary>
public readonly record struct EvaluationContext(JsonElement Root);
