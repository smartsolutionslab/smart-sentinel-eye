using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Automation's own copy of the fab identifier (spec 013). VOs are not
/// shared across contexts per ADR-0044, so this mirrors
/// <c>Identity.Domain.RegisteredClient.FabIdentifier</c> and
/// <c>EventIngestion.Domain.Event.FabIdentifier</c> without any project
/// reference.
///
/// <para>
/// The grammar is deliberately identical to those two. The same fab string
/// arrives here from an ingested event and from a caller's
/// <c>/fabs/&lt;fabId&gt;</c> group, and a value one context accepts while
/// another rejects would strand rules that can never be matched.
/// </para>
/// </summary>
public sealed record FabIdentifier : StringValueObject, IComparable<FabIdentifier>
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 32;

    private FabIdentifier(string value) : base(value) { }

    /// <summary>
    /// Ordinal, on <see cref="StringValueObject.Value"/> directly.
    ///
    /// <para>
    /// <c>CameraName</c> compares its <c>NormalizedValue</c>, and the difference
    /// is deliberate rather than an oversight here: that type preserves display
    /// casing, so its own <c>Equals</c> compares a normalised form and its
    /// ordering has to agree with it. A fab identifier's grammar admits
    /// lowercase letters, digits and <c>-</c> only, so there is exactly one
    /// spelling of any value and nothing to normalise. A normalisation step here
    /// would be a rule with no input that exercises it.
    /// </para>
    ///
    /// <para>
    /// Ordinal rather than culture-sensitive because the ordering must be the
    /// same everywhere it runs. ICU's behaviour varies by operating system and
    /// library version, so a culture-sensitive comparison could order two fabs
    /// one way on a developer's machine and another on a CI runner — and the
    /// caller that consults this is a database tie-break whose whole purpose is
    /// a stable page boundary (spec 039).
    /// </para>
    /// </summary>
    public int CompareTo(FabIdentifier? other)
    {
        if (other is null)
        {
            return 1;
        }

        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }

    public static bool operator <(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) < 0;

    public static bool operator >(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) > 0;

    public static bool operator <=(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) <= 0;

    public static bool operator >=(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) >= 0;

    public static FabIdentifier From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMinLength(MinimumLength)
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must be lowercase letters, digits, or '-' and start with a letter");
        return new FabIdentifier(value);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsAsciiLetterLower(s[0]))
        {
            return false;
        }

        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }
        return true;
    }
}
