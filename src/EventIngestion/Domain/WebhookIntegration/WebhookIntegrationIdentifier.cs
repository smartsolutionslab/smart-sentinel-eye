using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// Stable identifier for a webhook integration (ADR-0090). The
/// public-facing handle is <see cref="WebhookIntegrationName"/> (the
/// URL path segment); this Guid v7 is the durable aggregate id.
/// </summary>
public readonly record struct WebhookIntegrationIdentifier(Guid Value) : IStronglyTypedId<Guid>
{
    public static WebhookIntegrationIdentifier New() => new(Guid.CreateVersion7());

    public static WebhookIntegrationIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("WebhookIntegrationIdentifier cannot be empty.", nameof(value))
            : new WebhookIntegrationIdentifier(value);

    public override string ToString() => Value.ToString();
}
