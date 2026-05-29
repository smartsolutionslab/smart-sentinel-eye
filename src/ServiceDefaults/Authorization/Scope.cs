namespace SmartSentinelEye.ServiceDefaults.Authorization;

/// <summary>
/// Single source of truth for the v1 Identity scope catalogue
/// (spec 008 FR-002). Shape: <c>sse.&lt;resource&gt;.&lt;verb&gt;</c>.
/// Used at every endpoint via
/// <see cref="RequireScopeExtensions.RequireScope(Microsoft.AspNetCore.Builder.RouteHandlerBuilder, string)"/>
/// and at policy-registration time via
/// <see cref="RequireScopeExtensions.AddScopePolicies"/>.
///
/// <para>
/// Adding a new scope is a one-line edit here plus a
/// corresponding entry in <c>src/AppHost/Realms/smart-sentinel-eye-realm.json</c>.
/// New scopes are forward-compatible: callers without them get a
/// typed 403; callers with them just work.
/// </para>
/// </summary>
public static class Scope
{
    public static class Sse
    {
        public static class Cameras
        {
            public const string Read = "sse.cameras.read";
            public const string Write = "sse.cameras.write";
        }

        public static class Streams
        {
            public const string Read = "sse.streams.read";
            public const string Write = "sse.streams.write";
        }

        public static class Layouts
        {
            public const string Read = "sse.layouts.read";
            public const string Write = "sse.layouts.write";
        }

        public static class Overlays
        {
            public const string Read = "sse.overlays.read";
            public const string Write = "sse.overlays.write";
        }

        public static class Variables
        {
            public const string Read = "sse.variables.read";
            public const string Write = "sse.variables.write";
        }

        public static class Rules
        {
            public const string Read = "sse.rules.read";
            public const string Write = "sse.rules.write";
        }

        public static class Events
        {
            public const string Read = "sse.events.read";
            public const string Write = "sse.events.write";

            /// <summary>Granted to MQTT-publishing devices; not to humans.</summary>
            public const string Publish = "sse.events.publish";
        }

        public static class Webhooks
        {
            public const string Write = "sse.webhooks.write";
        }

        public static class Identity
        {
            public static class DeviceClients
            {
                public const string Write = "sse.identity.devices.write";
            }

            public static class KioskClients
            {
                public const string Write = "sse.identity.kiosks.write";
            }
        }

        public static class Audit
        {
            public const string Read = "sse.audit.read";
        }
    }

    /// <summary>
    /// All v1 scopes, used by
    /// <see cref="RequireScopeExtensions.AddScopePolicies"/> at
    /// startup. Order doesn't matter; uniqueness is enforced by
    /// xUnit test <c>ScopeTests.Every_scope_string_is_unique</c>.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Sse.Cameras.Read, Sse.Cameras.Write,
        Sse.Streams.Read, Sse.Streams.Write,
        Sse.Layouts.Read, Sse.Layouts.Write,
        Sse.Overlays.Read, Sse.Overlays.Write,
        Sse.Variables.Read, Sse.Variables.Write,
        Sse.Rules.Read, Sse.Rules.Write,
        Sse.Events.Read, Sse.Events.Write, Sse.Events.Publish,
        Sse.Webhooks.Write,
        Sse.Identity.DeviceClients.Write,
        Sse.Identity.KioskClients.Write,
        Sse.Audit.Read,
    ];
}
