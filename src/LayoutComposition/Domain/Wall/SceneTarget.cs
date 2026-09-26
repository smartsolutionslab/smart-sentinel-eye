using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// What a switch request asks a <see cref="Wall"/> to show next (spec 258
/// plan.md §2): either the next scene in the ordered set, cycling and
/// skipping unpublishable scenes (PD-6), or a named scene. Discriminated
/// value object, mirroring <c>RuleAction</c>'s own two-variant shape — parsed
/// at the API edge (plan.md §4.4) from the request body's <c>target</c>
/// field, and again (a separate, context-local type) inside Automation for
/// US2.
/// </summary>
public abstract record SceneTarget
{
    private SceneTarget() { }

    public sealed record Next : SceneTarget;

    public sealed record Layout(LayoutIdentifier Value) : SceneTarget;
}
