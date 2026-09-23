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

    /// <summary>
    /// Currently unproducible: its only writer would have been
    /// <c>V1ResourceMap.Conventions.NamespaceToResource["Automation"]</c>,
    /// but <c>Shared.Contracts</c> has no <c>Automation</c> folder —
    /// Automation publishes into other contexts' namespaces instead. Kept in
    /// <see cref="All"/> anyway: the vocabulary is a closed public list
    /// and removing it would turn <c>GET /audit/rule/x</c> from
    /// 200-empty into 400 for a reason unrelated to the orphaned mapping
    /// (same reasoning as <see cref="Webhook"/>, below).
    /// </summary>
    public static ResourceKind Rule { get; } = new("rule");

    public static ResourceKind Event { get; } = new("event");

    /// <summary>
    /// Currently unproducible: the only writers were two hand-tweaks in
    /// <c>V1ResourceMap.Conventions</c> that named
    /// <c>EventIngestion.WebhookIntegrationRegisteredV1</c> /
    /// <c>...RevokedV1</c>, types that do not exist in
    /// <c>Shared.Contracts</c>. The live webhook path is
    /// <see cref="WebhookIntegration"/>, via
    /// <c>Identity.WebhookIntegrationRotatedV1</c>. Kept in
    /// <see cref="All"/> anyway: the vocabulary is a closed public list
    /// and removing it would turn <c>GET /audit/webhook/x</c> from
    /// 200-empty into 400 for a reason unrelated to this defect.
    /// </summary>
    public static ResourceKind Webhook { get; } = new("webhook");

    public static ResourceKind Device { get; } = new("device");

    public static ResourceKind Kiosk { get; } = new("kiosk");

    public static ResourceKind WebhookIntegration { get; } = new("webhook-integration");

    public static IReadOnlyList<ResourceKind> All { get; } =
    [
        Camera,
        Stream,
        Layout,
        Overlay,
        Variable,
        Rule,
        Event,
        Webhook,
        Device,
        Kiosk,
        WebhookIntegration
    ];

    public static ResourceKind From(string value)
    {
        ResourceKind? found = All.FirstOrDefault(kind => kind.Value == value);
        return found ?? throw new ArgumentException($"Unknown ResourceKind '{value}'. Expected one of: {string.Join(" | ", All.Select(kind => kind.Value))}.", nameof(value));
    }

    public sealed override string ToString() => Value;
}
