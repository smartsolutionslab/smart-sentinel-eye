using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// CameraCatalog's own copy of the fab identifier (spec 015). VOs are not
/// shared across contexts per ADR-0044, so this mirrors
/// <c>Identity.Domain.RegisteredClient.FabIdentifier</c>,
/// <c>EventIngestion.Domain.Event.FabIdentifier</c>,
/// <c>Automation.Domain.Rule.FabIdentifier</c> and
/// <c>SystemVariables.Domain.Variable.FabIdentifier</c> without any project
/// reference.
///
/// <para>
/// The grammar is deliberately identical to those four. The same fab string
/// reaches this context from a caller's <c>/fabs/&lt;fabId&gt;</c> group and
/// travels back out on every camera lifecycle event, so a value one context
/// accepts while another rejects would strand cameras that no downstream
/// context can attribute.
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
