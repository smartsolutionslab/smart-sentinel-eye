using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Identity's own copy of the fab identifier (spec 008). We
/// don't share VOs across contexts per ADR-0044, so this
/// mirrors <c>EventIngestion.Domain.Event.FabIdentifier</c> /
/// <c>Automation.Domain.Rule.*</c> shape without any project
/// reference. Carried into the Keycloak group path
/// <c>/fabs/&lt;fabId&gt;</c>.
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
