using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Human-readable name of a logical Layout chain (spec 003 FR-006).
/// Non-empty, ≤ 80 chars, no newlines. Stricter slug/locale rules are
/// deferred — the kiosk picker shows the name verbatim.
/// </summary>
public sealed record LayoutName : StringValueObject
{
    public const int MaximumLength = 80;

    private LayoutName(string value) : base(value) { }

    public static LayoutName From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(s => !s.Contains('\n') && !s.Contains('\r'), "must not contain a line break")
            .AndReturn();
        return new LayoutName(validated.Trim());
    }
}
