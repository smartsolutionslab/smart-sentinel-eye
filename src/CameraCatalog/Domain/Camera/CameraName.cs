using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Human-readable camera name. Trimmed, 1–200 characters. Uniqueness compared
/// case-insensitively (via NormalizedValue); original casing preserved for
/// display.
/// </summary>
public sealed record CameraName : StringValueObject, IComparable<CameraName>
{
    public const int MaximumLength = 200;

    public string NormalizedValue { get; }

    private CameraName(string value, string normalizedValue)
        : base(value)
    {
        NormalizedValue = normalizedValue;
    }

    public int CompareTo(CameraName? other)
    {
        if (other is null) return 1;
        return string.Compare(NormalizedValue, other.NormalizedValue, StringComparison.Ordinal);
    }

    public static bool operator <(CameraName left, CameraName right) =>
        Comparer<CameraName>.Default.Compare(left, right) < 0;

    public static bool operator >(CameraName left, CameraName right) =>
        Comparer<CameraName>.Default.Compare(left, right) > 0;

    public static bool operator <=(CameraName left, CameraName right) =>
        Comparer<CameraName>.Default.Compare(left, right) <= 0;

    public static bool operator >=(CameraName left, CameraName right) =>
        Comparer<CameraName>.Default.Compare(left, right) >= 0;

    public static CameraName From(string value)
    {
        string trimmed = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .AndReturn()
            .Trim();

        return new CameraName(trimmed, trimmed.ToUpperInvariant());
    }

    public bool Equals(CameraName? other) =>
        other is not null && string.Equals(NormalizedValue, other.NormalizedValue, StringComparison.Ordinal);

    public override int GetHashCode() => NormalizedValue.GetHashCode(StringComparison.Ordinal);
}
