using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.StreamDistribution.Domain.Stream;

/// <summary>
/// StreamDistribution's own copy of the fab identifier (spec 016). VOs are not
/// shared across contexts per ADR-0044, so this mirrors
/// <c>Identity.Domain.RegisteredClient.FabIdentifier</c>,
/// <c>EventIngestion.Domain.Event.FabIdentifier</c>,
/// <c>Automation.Domain.Rule.FabIdentifier</c>,
/// <c>SystemVariables.Domain.Variable.FabIdentifier</c> and
/// <c>CameraCatalog.Domain.Camera.FabIdentifier</c> without any project
/// reference.
///
/// <para>
/// The grammar is deliberately identical to those five. A stream's fab is not
/// authored here — it arrives on <c>CameraRegisteredV1</c> from CameraCatalog,
/// which accepted it under that grammar. A value CameraCatalog accepts while
/// this context rejects would drop the provisioning of a camera that registered
/// perfectly well.
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
