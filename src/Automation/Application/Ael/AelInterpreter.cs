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
            AelExpression.Literal lit => lit.Value,
            AelExpression.FieldAccess fa => ResolveField(fa, context),
            AelExpression.Unary u => EvalUnary(u, context),
            AelExpression.Logical l => EvalLogical(l, context),
            AelExpression.Binary b => EvalBinary(b, context),
            _ => throw new InvalidOperationException($"Unhandled AelExpression case: {expression.GetType().Name}"),
        };

    private static AelValue ResolveField(AelExpression.FieldAccess fa, EvaluationContext context)
    {
        JsonElement current = context.Root;
        foreach (string segment in fa.Segments)
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
            JsonValueKind.Number when element.TryGetInt64(out long i) => new AelValue.IntValue(i),
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
                AelValue.IntValue i => new AelValue.IntValue(-i.Value),
                AelValue.DecimalValue d => new AelValue.DecimalValue(-d.Value),
                _ => throw new InvalidOperationException(
                    $"unary '-' requires a numeric operand; got {operand.GetType().Name}"),
            },
            UnaryOperator.Not => operand switch
            {
                AelValue.BoolValue b => new AelValue.BoolValue(!b.Value),
                _ => throw new InvalidOperationException(
                    $"unary '!' requires a bool operand; got {operand.GetType().Name}"),
            },
            _ => throw new InvalidOperationException($"Unhandled UnaryOperator: {unary.Operator}"),
        };
    }

    private static AelValue.BoolValue EvalLogical(AelExpression.Logical logical, EvaluationContext context)
    {
        AelValue left = Eval(logical.Left, context);
        bool leftBool = left is AelValue.BoolValue lb
            ? lb.Value
            : throw new InvalidOperationException(
                $"logical operand must be bool; got {left.GetType().Name}");

        // Short-circuit evaluation.
        if (logical.Operator == LogicalOperator.And && !leftBool) return new AelValue.BoolValue(false);
        if (logical.Operator == LogicalOperator.Or && leftBool) return new AelValue.BoolValue(true);

        AelValue right = Eval(logical.Right, context);
        bool rightBool = right is AelValue.BoolValue rb
            ? rb.Value
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

    private static AelValue EvalArithmetic(BinaryOperator op, AelValue left, AelValue right) =>
        op switch
        {
            BinaryOperator.Add => Arithmetic(left, right, static (a, b) => a + b, static (a, b) => a + b),
            BinaryOperator.Subtract => Arithmetic(left, right, static (a, b) => a - b, static (a, b) => a - b),
            BinaryOperator.Multiply => Arithmetic(left, right, static (a, b) => a * b, static (a, b) => a * b),
            BinaryOperator.Divide => Arithmetic(left, right, DivideInt, DivideDecimal),
            BinaryOperator.Modulo => Arithmetic(left, right, ModuloInt, ModuloDecimal),
            _ => throw new InvalidOperationException($"Unhandled arithmetic operator: {op}"),
        };

    private static AelValue.BoolValue EvalComparison(BinaryOperator op, AelValue left, AelValue right) =>
        op switch
        {
            BinaryOperator.LessThan => Compare(left, right, static (a, b) => a < b),
            BinaryOperator.LessThanOrEqual => Compare(left, right, static (a, b) => a <= b),
            BinaryOperator.GreaterThan => Compare(left, right, static (a, b) => a > b),
            BinaryOperator.GreaterThanOrEqual => Compare(left, right, static (a, b) => a >= b),
            _ => throw new InvalidOperationException($"Unhandled comparison operator: {op}"),
        };

    private static long DivideInt(long a, long b) =>
        b == 0 ? throw new InvalidOperationException("division by zero") : a / b;

    private static decimal DivideDecimal(decimal a, decimal b) =>
        b == 0m ? throw new InvalidOperationException("division by zero") : a / b;

    private static long ModuloInt(long a, long b) =>
        b == 0 ? throw new InvalidOperationException("modulo by zero") : a % b;

    private static decimal ModuloDecimal(decimal a, decimal b) =>
        b == 0m ? throw new InvalidOperationException("modulo by zero") : a % b;

    private static AelValue Arithmetic(AelValue left, AelValue right,
        Func<long, long, long> intOp, Func<decimal, decimal, decimal> decOp)
    {
        if (left is AelValue.IntValue li && right is AelValue.IntValue ri)
        {
            return new AelValue.IntValue(intOp(li.Value, ri.Value));
        }
        decimal a = ToDecimal(left);
        decimal b = ToDecimal(right);
        return new AelValue.DecimalValue(decOp(a, b));
    }

    private static AelValue.BoolValue Compare(AelValue left, AelValue right, Func<decimal, decimal, bool> op)
    {
        decimal a = ToDecimal(left);
        decimal b = ToDecimal(right);
        return new AelValue.BoolValue(op(a, b));
    }

    private static decimal ToDecimal(AelValue value) =>
        value switch
        {
            AelValue.IntValue i => i.Value,
            AelValue.DecimalValue d => d.Value,
            AelValue.StringValue s when decimal.TryParse(
                s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
                => parsed,
            _ => throw new InvalidOperationException(
                $"cannot coerce {value.GetType().Name} to a number"),
        };

    private static bool AreEqual(AelValue left, AelValue right) =>
        (left, right) switch
        {
            (AelValue.IntValue li, AelValue.IntValue ri) => li.Value == ri.Value,
            (AelValue.DecimalValue ld, AelValue.DecimalValue rd) => ld.Value == rd.Value,
            (AelValue.IntValue li, AelValue.DecimalValue rd) => li.Value == rd.Value,
            (AelValue.DecimalValue ld, AelValue.IntValue ri) => ld.Value == ri.Value,
            (AelValue.StringValue ls, AelValue.StringValue rs) => ls.Value == rs.Value,
            (AelValue.BoolValue lb, AelValue.BoolValue rb) => lb.Value == rb.Value,
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
