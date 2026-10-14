using Aspire.Hosting.JavaScript;

namespace SmartSentinelEye.AppHost;

internal static class WebApplications
{
    // REST goes through the gateway (#1005): each app gets VITE_API_GATEWAY_URL
    // and calls `${gateway}/<context>/...` cross-origin (the gateway's CORS
    // policy allows the app origins, #1003). Auth is direct to Keycloak (OIDC):
    // VITE_KEYCLOAK_URL is the same Aspire endpoint the services validate tokens
    // against, so the issuer matches (ServiceDefaults.AddBearerAuthentication).
    // The realtime SignalR hub (ADR-0152, LayoutComposition) and WebRTC media
    // stay direct — a direct reference to layout-composition from the two
    // kiosk instances keeps that URL resolvable, off the gateway.
    //
    // VITE_LAYOUT_HUB_ORIGIN is the shell-safe alias the kiosk app uses to
    // build its `/hubs` dev proxy (management-web's vite.config.ts reads no
    // environment and has no such proxy). Aspire's own service-discovery key is
    // `services__layout-composition__http__0`, and that name contains hyphens —
    // POSIX shells (bash, dash) refuse to import environment variables whose
    // names aren't valid identifiers, so `npm run dev` (which spawns Vite via
    // `sh -c`) drops it on Linux. cmd.exe keeps it, so this reproduces only off
    // Windows: the proxy is silently omitted, every `/hubs/layouts/negotiate`
    // 404s, and the kiosk sits on a permanently-stuck "live updates degraded"
    // badge (spec 011 FR-006/FR-010).
    //
    // WaitFor(layoutComposition) on the two kiosk instances (AddKioskWeb and
    // AddKioskWall) is ordering hygiene — each vite.config.ts reads the
    // environment once, at config-evaluation time, so the endpoint should be
    // resolvable before the app starts.
    //
    // WithNpm(install: false): Aspire.Hosting.JavaScript installs packages by
    // default, adding a "<name>-installer" child that npm-installs the app
    // directory and blocks the app on it. This repo installs once, at the pnpm
    // workspace root; npm doesn't understand the workspace:* protocol the apps
    // depend on, so left at its default the installer fails and every app
    // waits on it forever. install: false only removes the wait, not the
    // resource: the installer still shows in the dashboard as a startable,
    // explicit-start child (StackStatusReport skips it, so it can't stall
    // e2e's readiness wait) — don't start it by hand, it runs npm against a
    // pnpm workspace and can leave a stray package-lock.json/node_modules
    // behind that shadows the real install.
    internal static void AddWebApplications(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<ProjectResource> layoutComposition)
    {
        var camerasRemote = builder.AddCamerasRemote(apiGateway);
        var eventIngestionRemote = builder.AddEventIngestionRemote(apiGateway);

        builder.AddJavaScriptApp("management-web", "../../apps/management-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            // Spec 316: the shell reads this to register the cameras remote
            // (navigation/remotes.ts, T009). Same-origin dev port as every other
            // Aspire-resolved endpoint here — no WaitFor, matching the reasoning
            // already recorded at api-gateway's declaration in AppHost.cs for its
            // own nine references: an unentitled session never registers this
            // remote at all (FR-013), and an entitled one that beats it up
            // surfaces one contained load failure (FR-007/FR-008), not a blocked
            // shell boot.
            .WithEnvironment("VITE_CAMERAS_REMOTE_URL", camerasRemote.GetEndpoint("http"))
            // Issue #2780: the second remote's URL, same reasoning as the line above.
            .WithEnvironment("VITE_EVENT_INGESTION_REMOTE_URL", eventIngestionRemote.GetEndpoint("http"))
            .WithExternalHttpEndpoints()
            // Dashboard grouping: nest the SPAs under the gateway they call.
            .WithParentRelationship(apiGateway);

        builder.AddKioskWeb(apiGateway, keycloak, layoutComposition);
        builder.AddKioskWall(apiGateway, keycloak, layoutComposition);
    }

    // management-cameras (spec 316, US3): the federated cameras remote. It
    // carries no VITE_KEYCLOAK_URL/VITE_API_GATEWAY_URL pair of its own — its
    // only job is to serve remoteEntry.js to the shell on a fixed, unproxied
    // port (dev mode confirmed directly, T001 finding 1), so it needs no
    // WaitFor and no reference beyond the parent-relationship grouping below.
    // Declared before management-web so the shell can read its endpoint.
    private static IResourceBuilder<JavaScriptAppResource> AddCamerasRemote(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway)
    {
        return builder.AddJavaScriptApp("management-cameras", "../../apps/management-cameras", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5176, isProxied: false)
            .WithExternalHttpEndpoints()
            // Dashboard grouping: nest it under the gateway, like the three shells.
            .WithParentRelationship(apiGateway);
    }

    // management-event-ingestion (issue #2780): the second federated remote,
    // composed identically to management-cameras above — same no-WaitFor
    // reasoning, same fixed unproxied port (the next free one after 5176).
    private static IResourceBuilder<JavaScriptAppResource> AddEventIngestionRemote(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway)
    {
        return builder
            .AddJavaScriptApp("management-event-ingestion", "../../apps/management-event-ingestion", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5177, isProxied: false)
            .WithExternalHttpEndpoints()
            .WithParentRelationship(apiGateway);
    }

    private static void AddKioskWeb(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<ProjectResource> layoutComposition)
    {
        builder.AddJavaScriptApp("kiosk-web", "../../apps/kiosk-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5174, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            .WithReference(layoutComposition)
            .WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http"))
            .WaitFor(layoutComposition)
            .WithExternalHttpEndpoints()
            .WithParentRelationship(apiGateway);
    }

    // **The same application, deployed as a wall display** (spec 052).
    //
    // A second instance rather than a flag on the first, because a fab runs
    // both: screens somebody stands at, and screens bolted to a wall. Only this
    // one signs in as `kiosk-wall` and asks for a grant that outlives the
    // session ceiling — and only this one is narrowed to read-only, since
    // scopes belong to clients.
    //
    // It exists in the dev stack so the difference is exercised rather than
    // described. Without it, wall mode would be a code path nothing runs.
    private static void AddKioskWall(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<ProjectResource> layoutComposition)
    {
        builder.AddJavaScriptApp("kiosk-wall", "../../apps/kiosk-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5175, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            .WithReference(layoutComposition)
            .WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http"))
            .WithEnvironment("VITE_KIOSK_MODE", "wall")
            .WaitFor(layoutComposition)
            .WithExternalHttpEndpoints()
            .WithParentRelationship(apiGateway);
    }
}
