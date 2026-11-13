using Aspire.Hosting.JavaScript;

namespace SmartSentinelEye.AppHost;

internal static class WebApplications
{
    // REST goes through the gateway (#1005): each app gets VITE_API_GATEWAY_URL
    // and calls `${gateway}/<context>/...` cross-origin (the gateway's CORS
    // policy allows the app origins, #1003); the shell is the exception since
    // spec 491 and calls `/api` on its own origin (see AddWebApplications).
    // Auth is direct to Keycloak (OIDC):
    // VITE_KEYCLOAK_URL is the same Aspire endpoint the services validate tokens
    // against, so the issuer matches (ServiceDefaults.AddBearerAuthentication).
    // The realtime SignalR hub (ADR-0152, LayoutComposition) and WebRTC media
    // stay direct — the two kiosk instances and the console get the hub's
    // address from layout-composition's own endpoint, off the gateway.
    //
    // VITE_LAYOUT_HUB_ORIGIN is the shell-safe alias the kiosk app uses to
    // build its `/hubs` dev proxy, kept as the fallback for a Vite server
    // started outside Aspire: hubUrl.ts prefers VITE_LAYOUT_HUB_URL.
    // management-web has no `/hubs` proxy and gets only VITE_LAYOUT_HUB_URL.
    // Aspire's own service-discovery key is
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

        // ADR-0192 / spec 491: the console is reached at ONE origin, the gateway's. The shell therefore calls the API at `/api`
        // and loads its remotes from `/remotes/<name>`, relative to whatever origin it was opened at; opened directly at :5173
        // its own Vite dev server proxies both (apps/management-web/vite.config.ts), so the fixed port keeps working. The
        // gateway origin reaches that proxy under a shell-safe alias (see the POSIX-shell note above).
        var managementWeb = builder.AddJavaScriptApp("management-web", "../../apps/management-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", "/api")
            .WithEnvironment("VITE_GATEWAY_PROXY_TARGET", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            // The hub stays off the gateway (ADR-0192); no WaitFor, the wall page copes with it being down.
            .WithEnvironment("VITE_LAYOUT_HUB_URL", LayoutHubUrl(layoutComposition))
            // Spec 316: the shell reads this to register the cameras remote
            // (navigation/remotes.ts, T009). No WaitFor, matching the reasoning
            // already recorded at api-gateway's declaration in AppHost.cs for its
            // own nine references: an unentitled session never registers this
            // remote at all (FR-013), and an entitled one that beats it up
            // surfaces one contained load failure (FR-007/FR-008), not a blocked
            // shell boot.
            .WithEnvironment("VITE_CAMERAS_REMOTE_URL", "/remotes/cameras")
            // Issue #2780: the second remote's URL, same reasoning as the line above.
            .WithEnvironment("VITE_EVENT_INGESTION_REMOTE_URL", "/remotes/event-ingestion")
            .WithExternalHttpEndpoints()
            // Dashboard grouping: nest the SPAs under the gateway they call.
            .WithParentRelationship(apiGateway);

        var kioskWeb = builder.AddKioskWeb(apiGateway, keycloak, layoutComposition);
        var kioskWall = builder.AddKioskWall(apiGateway, keycloak, layoutComposition);

        apiGateway.WithOneOriginUi(managementWeb, camerasRemote, eventIngestionRemote, kioskWeb, kioskWall);

        // The hub is called cross-origin from the kiosk, the wall, the one origin and the console's own port, so
        // layout-composition admits exactly those. Endpoint references only, no WaitFor: the kiosks already wait for it.
        layoutComposition
            .WithEnvironment("LayoutHub__Cors__AllowedOrigins__0", kioskWeb.GetEndpoint("http"))
            .WithEnvironment("LayoutHub__Cors__AllowedOrigins__1", kioskWall.GetEndpoint("http"))
            .WithEnvironment("LayoutHub__Cors__AllowedOrigins__2", apiGateway.GetEndpoint("http"))
            .WithEnvironment("LayoutHub__Cors__AllowedOrigins__3", managementWeb.GetEndpoint("http"));
    }

    // The gateway's own address in run mode, so the one URL can be bookmarked and allowed by the identity provider (the
    // management-web realm client already accepts any http://localhost:* redirect). Without a port Aspire assigns a random one
    // on every start. It sits beside the Vite ports 5173-5177 and is not used by anything else in the tree.
    internal const int OneOriginDevPort = 5180;

    // ADR-0192 decision 4: in dev the gateway proxies the Vite dev servers. The five destinations are the apps' own fixed,
    // unproxied endpoints; `Ui__Mode=Proxy` turns UI hosting on (UiHosting in src/ApiGateway), and without it the gateway
    // serves no page. Only the run-mode stack calls this, so the integration fixture's gateway keeps its dynamic port and its
    // "no accidental catch-all" 404.
    internal static void WithOneOriginUi(
        this IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<JavaScriptAppResource> shell,
        IResourceBuilder<JavaScriptAppResource> camerasRemote,
        IResourceBuilder<JavaScriptAppResource> eventIngestionRemote,
        IResourceBuilder<JavaScriptAppResource> kioskWeb,
        IResourceBuilder<JavaScriptAppResource> kioskWall)
    {
        apiGateway
            .WithEndpoint("http", endpoint => endpoint.Port = OneOriginDevPort)
            .WithEnvironment("Ui__Mode", "Proxy")
            .WithEnvironment("Ui__Proxy__Shell", shell.GetEndpoint("http"))
            .WithEnvironment("Ui__Proxy__Cameras", camerasRemote.GetEndpoint("http"))
            .WithEnvironment("Ui__Proxy__EventIngestion", eventIngestionRemote.GetEndpoint("http"))
            .WithEnvironment("Ui__Proxy__Kiosk", kioskWeb.GetEndpoint("http"))
            .WithEnvironment("Ui__Proxy__Wall", kioskWall.GetEndpoint("http"));
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

    // The path is LayoutLifecycleHub.Path; the AppHost references no bounded context, so it is repeated here.
    private static ReferenceExpression LayoutHubUrl(IResourceBuilder<ProjectResource> layoutComposition) =>
        ReferenceExpression.Create($"{layoutComposition.GetEndpoint("http")}/hubs/layouts");

    private static IResourceBuilder<JavaScriptAppResource> AddKioskWeb(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<ProjectResource> layoutComposition)
    {
        return builder.AddJavaScriptApp("kiosk-web", "../../apps/kiosk-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5174, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            .WithReference(layoutComposition)
            .WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http"))
            .WithEnvironment("VITE_LAYOUT_HUB_URL", LayoutHubUrl(layoutComposition))
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
    private static IResourceBuilder<JavaScriptAppResource> AddKioskWall(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> apiGateway,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<ProjectResource> layoutComposition)
    {
        return builder.AddJavaScriptApp("kiosk-wall", "../../apps/kiosk-web", "dev")
            .WithNpm(install: false)
            .WithHttpEndpoint(env: "PORT", port: 5175, isProxied: false)
            .WithReference(apiGateway)
            .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
            .WithReference(keycloak)
            .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
            .WithReference(layoutComposition)
            .WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http"))
            .WithEnvironment("VITE_LAYOUT_HUB_URL", LayoutHubUrl(layoutComposition))
            .WithEnvironment("VITE_KIOSK_MODE", "wall")
            .WaitFor(layoutComposition)
            .WithExternalHttpEndpoints()
            .WithParentRelationship(apiGateway);
    }
}
