using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Lifecycle state of a single Revision within a Layout chain
/// (spec 003 FR-003). Transitions enforced by the <see cref="Revision"/>
/// entity:
///
///   Draft     -> Published  (Publish)
///   Draft     -> Archived   (Abandon a draft)
///   Published -> Draft      (Revert)
///   Published -> Archived   (Archive, or auto on new revision publish)
///
/// Archived is terminal per revision; the logical chain stays addressable
/// for audit even after every revision is Archived.
/// </summary>
public sealed record LayoutRevisionState(string Value) : IValueObject<string>
{
    public static LayoutRevisionState Draft { get; } = new("Draft");

    public static LayoutRevisionState Published { get; } = new("Published");

    public static LayoutRevisionState Archived { get; } = new("Archived");

    public static LayoutRevisionState From(string value) =>
        value switch
        {
            "Draft" => Draft,
            "Published" => Published,
            "Archived" => Archived,
            _ => throw new ArgumentException(
                $"Unknown LayoutRevisionState '{value}'.", nameof(value)),
        };

    public sealed override string ToString() => Value;
}
