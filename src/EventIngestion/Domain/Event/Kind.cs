using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Application-level event kind tag (e.g. <c>PlcCycleStart</c>,
/// <c>PersonInRestrictedZone</c>). Per spec 006 the field is required
/// on every event but never validated against a schema; downstream
/// consumers match on it. Grammar:
/// <c>^[A-Z][A-Za-z0-9]{0,127}$</c> (PascalCase, 1-128 chars).
/// </summary>
public sealed record Kind : StringValueObject
{
    public const int MaximumLength = 128;

    private Kind(string value) : base(value) { }

    public static Kind From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must start with an uppercase letter and contain only letters or digits")
            .AndReturn();
        return new Kind(validated);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsAsciiLetterUpper(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i])) return false;
        }
        return true;
    }
}
