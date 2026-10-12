using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Keycloak client_id string (spec 008). Grammar matches
/// Keycloak's own client-id validation: starts with a letter or
/// digit, contains only letters/digits/<c>.</c>/<c>_</c>/<c>-</c>,
/// 1–255 chars. The shape is also constrained by FR-008 (the
/// MQTT ACL expects <c>&lt;source&gt;.&lt;fab&gt;.&lt;deviceId&gt;</c>
/// for device clients, ADR-0172) but that's checked at command-time,
/// not by this VO. The fab segment is <c>.</c>-separated rather than
/// <c>-</c>-separated: both <c>FabIdentifier</c> and the caller-supplied
/// name/identifier segment may themselves contain <c>-</c>, so a
/// <c>-</c>-joined composition is not injective — <c>(fab: "dresden",
/// name: "2-press")</c> and <c>(fab: "dresden-2", name: "press")</c>
/// would both compose to <c>"plc-dresden-2-press"</c>. Neither part may
/// contain <c>.</c>, so joining on it is unambiguous.
/// </summary>
public sealed record ClientId : StringValueObject
{
    public const int MaximumLength = 255;

    private ClientId(string value) : base(value) { }

    public static ClientId From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must start with a letter or digit and contain only letters, digits, or '.', '_', '-'");
        return new ClientId(value);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsLetterOrDigit(s[0]))
        {
            return false;
        }

        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }
        return true;
    }
}
