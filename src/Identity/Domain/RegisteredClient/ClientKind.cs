using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient;

/// <summary>
/// Discriminator for what a registered Keycloak client represents
/// (spec 008). Three v1 cases. <see cref="Rotate"/> on
/// <c>RegisteredClient</c> is only valid for
/// <see cref="WebhookIntegration"/> per FR-014; the aggregate
/// enforces that.
/// </summary>
public sealed record ClientKind(string Value) : IValueObject<string>
{
    public static ClientKind Device { get; } = new("Device");

    public static ClientKind Kiosk { get; } = new("Kiosk");

    public static ClientKind WebhookIntegration { get; } = new("WebhookIntegration");

    public static ClientKind From(string value) =>
        value switch
        {
            "Device" => Device,
            "Kiosk" => Kiosk,
            "WebhookIntegration" => WebhookIntegration,
            _ => throw new ArgumentException(
                $"Unknown ClientKind '{value}'. Expected: Device | Kiosk | WebhookIntegration.",
                nameof(value)),
        };

    public sealed override string ToString() => Value;
}
