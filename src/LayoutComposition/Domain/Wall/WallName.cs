using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Human-readable name of a <see cref="Wall"/> (spec 258 FR-001), unique per
/// fab among live walls. Mirrors <c>LayoutName</c>'s length and blank/newline
/// rules exactly — same trust boundary, same picker surface.
/// </summary>
public sealed record WallName : StringValueObject
{
    public const int MaximumLength = 80;

    private WallName(string value) : base(value) { }

    public static WallName From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(candidate => !candidate.Contains('\n') && !candidate.Contains('\r'), "must not contain a line break");
        return new WallName(value.Trim());
    }
}
