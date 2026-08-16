using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// LayoutComposition's own copy of the fab identifier (spec 017). VOs are not
/// shared across contexts per ADR-0044, so this mirrors
/// <c>Identity.Domain.RegisteredClient.FabIdentifier</c>,
/// <c>EventIngestion.Domain.Event.FabIdentifier</c>,
/// <c>Automation.Domain.Rule.FabIdentifier</c>,
/// <c>SystemVariables.Domain.Variable.FabIdentifier</c>,
/// <c>CameraCatalog.Domain.Camera.FabIdentifier</c> and
/// <c>StreamDistribution.Domain.Stream.FabIdentifier</c> without any project
/// reference.
///
/// <para>
/// The grammar is deliberately identical to those six, and this context is
/// where a divergence would hurt most: the same fab string arrives from a
/// caller's <c>/fabs/&lt;fabId&gt;</c> group, is compared against a camera's
/// fab held by CameraCatalog (spec 017 FR-014), and becomes the SignalR group
/// name a kiosk joined on connect. A value one of those three accepted and
/// another rejected would silently deliver a frame to nobody.
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
