using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// SystemVariables' own copy of the fab identifier (spec 014). VOs are not
/// shared across contexts per ADR-0044, so this mirrors
/// <c>Identity.Domain.RegisteredClient.FabIdentifier</c>,
/// <c>EventIngestion.Domain.Event.FabIdentifier</c> and
/// <c>Automation.Domain.Rule.FabIdentifier</c> without any project reference.
///
/// <para>
/// The grammar is deliberately identical to those three. The same fab string
/// arrives here on a value-change raised by an Automation rule and from a
/// caller's <c>/fabs/&lt;fabId&gt;</c> group, and a value one context accepts
/// while another rejects would strand variables that can never resolve.
/// </para>
/// </summary>
public sealed record FabIdentifier : StringValueObject
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 32;

    private FabIdentifier(string value) : base(value) { }

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
