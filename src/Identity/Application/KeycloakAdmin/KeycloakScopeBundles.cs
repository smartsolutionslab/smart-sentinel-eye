namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Pre-computed scope bundles per persona (spec 008 FR-002).
/// Used by the command handlers when handing the
/// <see cref="KeycloakClientRepresentation"/> off to
/// <see cref="IKeycloakAdminClient.CreateClientAsync"/>.
///
/// <para>
/// The scope strings are hard-coded here rather than pulled
/// from <c>ServiceDefaults.Authorization.Scope</c> so that
/// Application stays ASP.NET-free per ADR-0051. The
/// <c>ScopeBundleTests</c> assertion (spec 008 PR F) verifies
/// these strings match the catalogue.
/// </para>
/// </summary>
public static class KeycloakScopeBundles
{
    public static IReadOnlyList<string> Kiosk { get; } =
    [
        "sse.cameras.read",
        "sse.streams.read",
        "sse.layouts.read",
        "sse.overlays.read",
        "sse.variables.read",
        "sse.events.write",
    ];

    /// <summary>
    /// PLC / inference devices.
    /// </summary>
    public static IReadOnlyList<string> Device { get; } =
    [
        "sse.cameras.read",
        "sse.events.publish",
    ];

    public static IReadOnlyList<string> WebhookIntegration { get; } =
    [
        "sse.events.write",
    ];
}
