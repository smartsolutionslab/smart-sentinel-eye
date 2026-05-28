using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// Stable, URL-safe name of an automation rule (spec 007 FR-002).
/// Grammar: <c>^[a-z][a-z0-9-]{1,62}$</c> — kebab-lowercase, 2-63
/// chars. The pair (<c>fabId</c>, <c>name</c>) is unique across
/// non-archived rules; archived names are released for re-use.
/// </summary>
public sealed record RuleName : StringValueObject
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 63;

    private RuleName(string value) : base(value) { }

    public static RuleName From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMinLength(MinimumLength)
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must start with a lowercase letter and contain only lowercase letters, digits, or '-'")
            .AndReturn();
        return new RuleName(validated);
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
