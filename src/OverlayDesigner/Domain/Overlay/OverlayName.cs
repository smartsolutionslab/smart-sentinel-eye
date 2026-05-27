using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Human-readable name of a logical Overlay chain (spec 004 FR-006).
/// Non-empty, ≤ 80 chars, no newlines. Mirrors LayoutName's rules so
/// the picker UIs share validation.
/// </summary>
public sealed record OverlayName : StringValueObject
{
    public const int MaximumLength = 80;

    private OverlayName(string value) : base(value) { }

    public static OverlayName From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(s => !s.Contains('\n') && !s.Contains('\r'), "must not contain a line break")
            .AndReturn();
        return new OverlayName(validated.Trim());
    }
}
