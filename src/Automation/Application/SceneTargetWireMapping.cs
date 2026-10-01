using SmartSentinelEye.Automation.Domain.Rule;

namespace SmartSentinelEye.Automation.Application;

/// <summary>
/// <see cref="SceneTarget"/> → wire shape, shared by <c>RuleEvaluator</c>
/// (the fan-out effect) and <c>RuleMapper</c> (the query DTO) so the
/// projection is written once rather than once per caller.
/// </summary>
internal static class SceneTargetWireMapping
{
    public static string Literal(SceneTarget target) =>
        target switch
        {
            SceneTarget.Next => SceneTarget.NextLiteral,
            SceneTarget.Layout => SceneTarget.LayoutLiteral,
            _ => throw new InvalidOperationException($"Unhandled SceneTarget case: {target.GetType().Name}"),
        };

    public static Guid? TargetLayout(SceneTarget target) =>
        target is SceneTarget.Layout layout ? layout.Value.Value : null;
}
