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
/// Application stays ASP.NET-free per ADR-0051.
/// </para>
///
/// <para>
/// <c>KioskScopeParityTests</c> (spec 041) asserts <see cref="Kiosk"/>
/// against the realm's <c>kiosk-web</c> client as a set, in both
/// directions — there is one notion of what a kiosk may do, and the
/// browser kiosk is not a second one. Until then nothing checked, and
/// this comment claimed a <c>ScopeBundleTests</c> that had never been
/// written.
/// </para>
/// </summary>
public static class KeycloakScopeBundles
{
    /// <summary>
    /// The client scope that puts this product's API in a token's <c>aud</c>
    /// claim (spec 069). Appended at the call sites rather than added to a
    /// bundle: the bundles are permission sets, and
    /// <c>KioskScopeParityTests</c> compares <see cref="Kiosk"/> against the
    /// realm's <c>kiosk-web</c> client without filtering the bundle side.
    /// </summary>
    /// <remarks>
    /// A second spelling of a string <c>ServiceDefaults</c> also holds, for the
    /// same reason every <c>sse.*</c> string in this file is re-spelt rather
    /// than imported: ADR-0051 keeps this layer ASP.NET-free. Do not add a
    /// project reference to remove the duplication — that reference is the
    /// thing ADR-0051 forbids. <c>Architecture.Tests</c> reads both assemblies
    /// and holds the pairing.
    /// </remarks>
    public const string AudienceScope = "sse-audience";

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
