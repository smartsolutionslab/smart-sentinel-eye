# ADR-0192: The gateway also fronts the UIs, at one origin

**Status:** Accepted (owner, 2026-11-11; tracked in #3244, epic #3248)
**Date:** 2026-11-11
**Supersedes:** —
**Superseded by:** —
**Amends:** ADR-0106 (scope: REST only → REST and the UIs, at one origin)
**Relates to:** ADR-0074 (two React apps), ADR-0107 / ADR-0168 (operator micro-frontends, "served behind the gateway/static edge"), ADR-0152 (SignalR), ADR-0011 / ADR-0012 (WebRTC media), ADR-0153 (one instance per service), ADR-0080 (browser auth), ADR-0108 (Playwright), constitution §IV (latency budget)

## Context

ADR-0106 made the YARP gateway the single front door for **REST**, and its first positive consequence is "browser apps target a single base
URL". It says nothing about the pages themselves. Today a person who wants the operator console, a kiosk and a wall display uses **five
origins**: five Vite dev servers on fixed ports (5173 the shell, 5174 the kiosk, 5175 the wall, 5176 and 5177 the two federated remotes,
declared in `src/AppHost/WebApplications.cs`). The shell and the kiosks call the gateway cross-origin through `VITE_API_GATEWAY_URL`, which is
why the gateway carries a CORS policy whose allowed origins are the three UI ports 5173-5175 (`Cors:AllowedOrigins`). ADR-0107 says the
operator UI is "served behind ADR-0106's edge" and ADR-0168 "behind ADR-0106's gateway/static edge"; nothing yet builds that.

The owner wants the UI and the API reached through **one URL**.

Four facts bound the design, each checked against the tree on 2026-11-11:

1. **The latency budget does not go through HTTP.** The event-to-overlay path is RabbitMQ and a per-kiosk SignalR push (ADR-0152); media is
   WebRTC to the SFU. ADR-0106 kept both off the gateway, and `BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`
   enforces it: the gateway assembly may not depend on `Microsoft.AspNetCore.SignalR`, `.WebSockets` or `.Http.Connections`.
2. **A shared root collides.** The gateway's REST routes are `/<context>/...` (`/system-variables/**`, `/event-ingestion/**`, ...). The shell
   owns the pages `/system-variables` and `/cameras`, and mounts a remote at `event-ingestion/*`. At one root, the gateway's
   `/system-variables/**` and `/event-ingestion/**` would shadow the shell's own routes.
3. **The gateway is one instance.** ADR-0153 clause 1 pins `api-gateway` to a single instance (#2283: its rate limiter is in memory, per
   process); `AppHostReplicaCountTests` pins it. ADR-0106's "at least 2 replicas" is therefore not exercised today.
4. **Production has no edge yet.** The only chart in `deploy/helm` is Mosquitto; no production deployment has run, so there is no Ingress
   whose design this must fit, and none whose behaviour can be assumed.

## Decision

1. **The gateway is the one external origin for pages and REST.** A person opens one URL; the console, the kiosk and the wall display are
   paths under it.
2. **Paths.** `/` is the management shell; `/remotes/cameras/` and `/remotes/event-ingestion/` are the federated remotes;
   `/kiosk/` and `/wall/` are the two kiosk instances; **`/api/<context>/...` is REST**, with the `/api` prefix stripped (YARP
   `PathRemovePrefix`) before the request reaches the service. The `/api` prefix is what removes the collision in fact 2. The bare
   `/<context>/...` routes stay while the fixed dev ports exist (point 7).
3. **Scope is UI plus REST only. Three things stay direct, unchanged:** Keycloak (browser OIDC, ADR-0080), the SignalR layout hub
   (ADR-0152) and WebRTC/WHEP media (ADR-0011/0012). The event-to-overlay and SFU-to-decode legs gain no hop, so §IV is untouched, and
   `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` stays in force **unmodified**. The one WebSocket the gateway will carry is
   Vite's hot-module-reload socket in dev; it is a development tool, not a §IV leg, and YARP proxies it through its own upgrade handling
   without the gateway's own code referencing the forbidden namespaces. **If phase 1 cannot meet the test that way, this ADR is amended;
   the test is not weakened.**
4. **Who serves the bytes.** In **dev** the gateway proxies to the Vite dev servers (HMR included). In **production** it serves the built
   `dist` bundles, from the gateway itself or from a small static container behind it; the phase-1 spec chooses, and records why. This ADR
   fixes only that the origin is the gateway's.
5. **The environment contract keeps its intent.** `docs/deployment-frontend-env.md` makes a missing `VITE_*` origin a hard production
   failure, "a guard against bundles that silently fall back to `localhost` or same-origin". That guard stays: `VITE_API_GATEWAY_URL` is
   still required, and its value becomes the explicit `/api` base. A bundle that sets nothing still fails loudly. The two remote URLs become
   explicit relative paths under `/remotes/`.
6. **Auth and CORS.** Keycloak stays direct, so the issuer the services validate is unchanged. The new origin is added to the realm
   clients' `redirectUris` / `webOrigins`; the kiosk's OIDC callback becomes base-aware. The gateway's CORS policy keeps serving external
   publishers and stops being the UIs' port list once nothing cross-origin remains (after point 7).
7. **Dev compatibility.** Ports 5173-5177 and the cross-origin mode keep working beside the one URL. Nothing cuts over until the end-to-end
   suite does (epic #3248, phase 3), which then decides whether the ports stay as a dev convenience.
8. **Rate limiting.** Static assets are served under their own policy, or none. They do not pass through the `per-source` limiter (100 per
   minute per source address in production; raised to 6000 only for the dev stack and CI's Playwright job, `RunLaneOverrides.cs`). A single page load fetches many
   files, and whether even 100/min suits a real fab behind one address is already open (#2563). `/api/...` stays limited as today.
9. **Carved-out routes survive.** `POST /stream-distribution/streams/authorize` stays a hard 404 at the gateway, under the `/api` prefix as
   well as without it.
10. **The production edge (TLS, Ingress, replicas) is deferred** until a production deployment exists. TLS still terminates at the deploy
    edge as ADR-0106 says; this ADR chooses no Ingress.

## Consequences

**Positive**

- One URL for the console, the kiosk, the wall and the API; no per-app CORS dance for our own UIs, and one origin to allow in the realm.
- The `/api` prefix removes a real routing collision instead of relying on route precedence.
- §IV is untouched by construction: nothing on the event-to-overlay or media path moves.
- It builds the "gateway/static edge" that ADR-0107 and ADR-0168 already assumed.

**Negative**

- **The gateway is now the single point of failure for the pages, not only for REST,** and it is one instance today (fact 3). A gateway
  restart takes the console and the kiosks down together. Fixing that means revisiting ADR-0153 clause 1 (a shared rate-limit store), which
  this ADR does not do.
- **The gateway becomes an HTML origin**: it needs security headers and a Content-Security-Policy it never needed as a JSON proxy.
- **Subpath deployment is a known fragile area.** Vite `base`, the Module Federation manifest and remote-entry paths, and the router
  `basename` all have to agree per app.
- **The hub stays a different origin**, visible in the network tab though not to the person using the UI, and `VITE_LAYOUT_HUB_URL` stays an
  absolute URL in production.
- **Two modes to keep green** (one URL and fixed ports) until the end-to-end cut-over.

## Alternatives Considered

- **Keep five origins (status quo).** Rejected: it is the thing the owner asked to change, and every new app adds an origin to CORS and the
  realm.
- **Path routing in a Kubernetes Ingress instead of in the gateway.** Rejected for now: there is no production deployment to put it in
  (fact 4), it would leave dev with a different shape from production, and it moves routing out of code that is tested here. Not
  incompatible later: the Ingress would front the gateway.
- **A separate static-file container as the origin, with the gateway behind it for `/api`.** Not rejected as the production *byte server*
  (point 4 allows it), but rejected as the *origin*: two services would then share one front door.
- **Host-based routing (`console.`, `kiosk.`, `wall.` subdomains).** Rejected: it is still several URLs.
- **Also proxy the SignalR hub (one origin for everything).** Deferred. It puts a hop on the ≤200 ms event-to-overlay leg, contradicts
  ADR-0106's latency note, and would require changing the architecture test. It needs a measurement and its own ADR, not a rider on this one.
- **Also put Keycloak behind the gateway.** Deferred. The token issuer must match the URL the services validate against; moving it changes
  the issuer and the realm, which is a larger decision than a front door.

## Implementation Notes

- Phase issues: #3245 (shell and remotes), #3246 (kiosk and wall), #3247 (end-to-end and a guard against hard-coded `localhost:517x`
  origins). Each is a supervised-lane spec; none starts before this ADR is accepted.
- The first slice should prove the riskiest assumption, that Vite's HMR socket and the Module Federation remote entry both work under the
  prefix and that `BoundaryTests` stays green, before any other work depends on it.
