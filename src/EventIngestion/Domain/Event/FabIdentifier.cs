using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Identifies a single fab (spec 006). Grammar:
/// <c>^[a-z][a-z0-9-]{1,31}$</c> — lowercase kebab-style, 2-32
/// chars. Carried in MQTT topic segments and in every event row's
/// <c>fab_id</c> column.
/// </summary>
public sealed record FabIdentifier : StringValueObject
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 32;

    private FabIdentifier(string value) : base(value) { }

    public static FabIdentifier From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMinLength(MinimumLength)
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must be lowercase letters, digits, or '-' and start with a letter")
            .AndReturn();
        return new FabIdentifier(validated);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsAsciiLetterLower(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-') return false;
        }
        return true;
    }
}
