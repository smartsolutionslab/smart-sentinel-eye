using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// The version CameraCatalog's camera was at when it made the address change a
/// stream last applied (spec 403). Versions strictly increase across committed
/// changes, so this orders them where a clock cannot.
///
/// <para>
/// Guarded for non-negativity and nothing more: how high a version may climb is
/// a fact about the camera's history, not an invariant of this type.
/// </para>
/// </summary>
public sealed record SourceCameraVersion(int Value) : IValueObject<int>, IComparable<SourceCameraVersion>
{
    /// <summary>
    /// Nothing orders a value object for free: Comparer&lt;T&gt;.Default throws
    /// the moment a list of these is sorted in memory.
    /// </summary>
    public int CompareTo(SourceCameraVersion? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(SourceCameraVersion left, SourceCameraVersion right) =>
        Comparer<SourceCameraVersion>.Default.Compare(left, right) < 0;

    public static bool operator >(SourceCameraVersion left, SourceCameraVersion right) =>
        Comparer<SourceCameraVersion>.Default.Compare(left, right) > 0;

    public static bool operator <=(SourceCameraVersion left, SourceCameraVersion right) =>
        Comparer<SourceCameraVersion>.Default.Compare(left, right) <= 0;

    public static bool operator >=(SourceCameraVersion left, SourceCameraVersion right) =>
        Comparer<SourceCameraVersion>.Default.Compare(left, right) >= 0;

    public static SourceCameraVersion From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new(value);
    }

    public static implicit operator int(SourceCameraVersion value) => value.Value;

    public sealed override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
