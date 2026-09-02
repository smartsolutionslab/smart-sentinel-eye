using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// The Keycloak client backing a webhook integration, where one has been
/// provisioned. Absent until it has.
/// </summary>
public sealed record KeycloakClientIdentifier : StringValueObject
{
    /// <summary>
    /// Matches the column width in the EF configuration. The two must agree: a
    /// bound narrower than its column refuses values the database would accept,
    /// and a wider one hands Postgres a write it will reject.
    /// </summary>
    public const int MaximumLength = 255;

    private KeycloakClientIdentifier(string value)
        : base(value)
    {
    }

    public static KeycloakClientIdentifier From(string value)
    {
        Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength);

        return new KeycloakClientIdentifier(value);
    }
}
