using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Closed vocabulary of resources the audit trail can pivot on
/// (spec 009 FR-009). Adding a new resource lands in this file
/// + the <see cref="V1ResourceMap"/> mapping registry; nothing
/// else needs to know.
/// </summary>
public sealed record ResourceKind(string Value) : IValueObject<string>
{
    public static ResourceKind Camera { get; } = new("camera");

    public static ResourceKind Stream { get; } = new("stream");

    public static ResourceKind Layout { get; } = new("layout");

    public static ResourceKind Overlay { get; } = new("overlay");

    public static ResourceKind Variable { get; } = new("variable");

    public static ResourceKind Rule { get; } = new("rule");

    public static ResourceKind Event { get; } = new("event");

    public static ResourceKind Webhook { get; } = new("webhook");

    public static ResourceKind Device { get; } = new("device");

    public static ResourceKind Kiosk { get; } = new("kiosk");

    public static ResourceKind WebhookIntegration { get; } = new("webhook-integration");

    public static IReadOnlyList<ResourceKind> All { get; } =
    [
        Camera, Stream, Layout, Overlay, Variable, Rule,
        Event, Webhook, Device, Kiosk, WebhookIntegration,
    ];

    public static ResourceKind From(string value) =>
        All.FirstOrDefault(k => k.Value == value)
            ?? throw new ArgumentException(
                $"Unknown ResourceKind '{value}'. Expected one of: {string.Join(" | ", All.Select(k => k.Value))}.",
                nameof(value));

    public sealed override string ToString() => Value;
}
