namespace SmartSentinelEye.AppHost;

internal static class RunLaneOverrides
{
    internal static void WithRunLaneIcePublish(
        this IResourceBuilder<ContainerResource> mediamtx,
        bool isRunMode,
        bool isE2ETests)
    {
        if (isRunMode && !isE2ETests)
        {
            // WebRTC media (ICE) must reach the host browser, which can't route to the
            // container IP MediaMTX advertises by default. Aspire's DCP proxies container
            // endpoints on random TCP host ports, which ICE can't use (the advertised
            // candidate port wouldn't match and UDP wouldn't traverse), so publish the
            // ICE mux directly with a raw docker port map (host 8189 -> container 8189,
            // UDP + TCP) and advertise the host loopback. Both run-mode lanes need this
            // — the developer's own stack and CI's end-to-end job, whose Playwright
            // suite drives a real browser against it — so it stays on `isRunMode &&
            // !isE2ETests` alone, not `PersistentStack`: it is a networking need, not
            // persistence (#2297).
            mediamtx
                .WithContainerRuntimeArgs("--publish", "8189:8189/udp", "--publish", "8189:8189/tcp")
                .WithEnvironment("MTX_WEBRTCADDITIONALHOSTS", "127.0.0.1");
        }
    }

    // In run mode the stream-distribution service runs as a host process, so the
    // mediamtx container can't reach it via the `stream-distribution` service name
    // baked into mediamtx.yml's authHTTPAddress (that DNS name resolves only among
    // containers on the AppHost network). Override the WHEP auth hook with the
    // Aspire-resolved, container-reachable endpoint so every WHEP open can be
    // authorized; without it MediaMTX's auth callback fails with "no such host" and
    // returns 401 to the browser. Publish mode containerizes stream-distribution, so
    // the baked-in service name is correct there and this override is run-only.
    internal static void WithRunLaneAuthorizeHookAddress(
        this IResourceBuilder<ContainerResource> mediamtx,
        IResourceBuilder<ProjectResource> streamDistribution,
        bool isRunMode)
    {
        if (isRunMode)
        {
            mediamtx.WithEnvironment(context =>
            {
                context.EnvironmentVariables["MTX_AUTHHTTPADDRESS"] =
                    ReferenceExpression.Create($"{streamDistribution.GetEndpoint("http")}/streams/authorize");
            });
        }
    }

    // Spec 232 (#2221): a single four-tile live kiosk wall's own routine
    // telemetry — polling, latency and skew POSTs, none of it an operator write —
    // already spends ~246 gateway requests/min at rest, over the gateway's
    // 100/min production default (src/ApiGateway/appsettings.json) by itself,
    // because no client sends X-Fab so every browser in a local or CI run shares
    // one source-IP partition. A local Playwright run at the default worker
    // count simulates several such walls concurrently (8 workers, some specs
    // holding two pages, ~12 four-tile walls), so the dev/e2e stack widens its
    // own budget to 6000/min (plan.md §2's derivation) to cover that concurrency
    // without touching the production default or the partition key. **Gated
    // `isRunMode && !isE2ETests`**: the integration fixture (E2ETests=true) must
    // keep the 100/min production default — GatewayRateLimitIntegrationTests
    // exhausts exactly that window — while the dev stack and CI's Playwright job
    // boot the same run-mode shape (AppHostE2ESwitchTests pins that CI passes no
    // E2ETests).
    //
    // This is a dev/e2e-only mitigation, not a fix to the production concern:
    // whether 100/min is itself too tight for a real fab's wall/NAT topology
    // (many screens sharing one address behind an Ingress) is tracked
    // separately as #2563.
    internal static void WithRunLaneRateBudget(
        this IResourceBuilder<ProjectResource> apiGateway,
        bool isRunMode,
        bool isE2ETests)
    {
        if (isRunMode && !isE2ETests)
        {
            apiGateway.WithEnvironment("RateLimiting__PermitLimit", "6000");
        }
    }
}
