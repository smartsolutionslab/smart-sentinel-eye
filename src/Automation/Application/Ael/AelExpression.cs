using System.Collections.Immutable;

namespace SmartSentinelEye.Automation.Application.Ael;

/// <summary>
/// Discriminated parse-tree node for AEL (ADR-0099). Built once at
/// rule-publish time and cached inside
/// <c>Application.Evaluation.CompiledRule</c>. Walking the tree is
/// allocation-free via the pooled <c>Span&lt;AelValue&gt;</c> stack
/// in <see cref="AelInterpreter"/>.
/// </summary>
public abstract record AelExpression
{
    public sealed record Literal(AelValue Value) : AelExpression;

    /// <summary>
    /// JSONPath-subset field access. <c>$.payload.cycleTime</c> →
    /// <c>Segments = ["payload", "cycleTime"]</c>. The leading
    /// <c>$</c> is implicit (it's the evaluation context root).
    /// </summary>
    public sealed record FieldAccess(ImmutableArray<string> Segments) : AelExpression;

    public sealed record Binary(BinaryOperator Operator, AelExpression Left, AelExpression Right) : AelExpression;

    public sealed record Logical(LogicalOperator Operator, AelExpression Left, AelExpression Right) : AelExpression;

    public sealed record Unary(UnaryOperator Operator, AelExpression Operand) : AelExpression;
}

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, Modulo,
    Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    Contains,
}

public enum LogicalOperator
{
    And, Or,
}

public enum UnaryOperator
{
    Negate, Not,
}
