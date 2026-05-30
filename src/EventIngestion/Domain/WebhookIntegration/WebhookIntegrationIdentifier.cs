using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// Stable identifier for a webhook integration (ADR-0090). The
/// public-facing handle is <see cref="WebhookIntegrationName"/> (the
/// URL path segment); this Guid v7 is the durable aggregate id.
/// </summary>
public readonly record struct WebhookIntegrationIdentifier(Guid Value) : IStronglyTypedId<Guid>, IComparable<WebhookIntegrationIdentifier>
{
    public static WebhookIntegrationIdentifier New() => new(Guid.CreateVersion7());

    public static WebhookIntegrationIdentifier From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("WebhookIntegrationIdentifier cannot be empty.", nameof(value))
            : new WebhookIntegrationIdentifier(value);

    public static implicit operator Guid(WebhookIntegrationIdentifier id) => id.Value;

    /// <summary>Orders by the underlying Guid v7 so EF ordering and in-memory sorts agree.</summary>
    public int CompareTo(WebhookIntegrationIdentifier other) => Value.CompareTo(other.Value);

    public static bool operator <(WebhookIntegrationIdentifier left, WebhookIntegrationIdentifier right) => left.CompareTo(right) < 0;
    public static bool operator <=(WebhookIntegrationIdentifier left, WebhookIntegrationIdentifier right) => left.CompareTo(right) <= 0;
    public static bool operator >(WebhookIntegrationIdentifier left, WebhookIntegrationIdentifier right) => left.CompareTo(right) > 0;
    public static bool operator >=(WebhookIntegrationIdentifier left, WebhookIntegrationIdentifier right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString();
}
