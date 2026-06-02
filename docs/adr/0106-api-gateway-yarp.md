# ADR-0106: API Gateway — single YARP reverse proxy at the edge

**Status:** Accepted
**Date:** 2026-06-01
**Supersedes:** —
**Superseded by:** —
**Relates to:** ADR-0070 (Minimal APIs), ADR-0007/0008 (Keycloak auth), ADR-0024/0025 (Aspire → k3s/Helm), ADR-0076 (replaceable realtime transport), ADR-0074 (two React apps), constitution §IV (latency budget)

## Context

The nine bounded-context Minimal APIs (ADR-0070) are currently exposed
**directly** — Aspire service discovery wires `management-web` /
`kiosk-web` to each service in dev, and k3s Ingress fronts them in prod.
There is no single external front door, so cross-cutting edge concerns —
CORS policy, TLS termination, and rate limiting — would have to be
configured (and kept consistent) in nine places.

As the external surface grows (two browser apps plus external event
publishers, ADR-0010), we want **one edge** that owns those concerns,
without eroding the system's load-bearing NFR: the
`event → overlay rendered ≤ 800 ms` budget (constitution §IV). That
budget's hot path is **not HTTP request/response** — it is
`event → RabbitMQ → context handlers → per-kiosk WebSocket push`
(ADR-0076) — and the media path is `Camera → SFU (WebRTC) → kiosk`
(ADR-0011/0012). Any gateway must stay **off** both.

## Decision

Adopt a **single YARP (`Yarp.ReverseProxy`) reverse proxy** as the API
gateway: a new `SmartSentinelEye.ApiGateway` ASP.NET Core service,
registered as an Aspire resource and deployed to k3s.

- **Scope — REST/HTTP only.** It fronts the HTTP APIs of all nine
  contexts. Routes (path-based `/<context>/...`, host-based as a future
  option) map to each service via Aspire/k8s **service discovery**
  (`http://<service>`), so no hand-wired connection strings.
- **It owns, once, at the edge:**
  1. **CORS** — a single cross-origin policy for the browser apps.
  2. **TLS termination** — the externally-exposed HTTPS endpoint.
  3. **Rate limiting** — per-fab / per-client limits and burst control
     via ASP.NET Core's `RateLimiter` middleware partitioned on the fab
     claim/header, fronting YARP.
- **It does NOT do centralized auth offload.** Per-service JWT
  validation stays (ADR-0007/0008, defense in depth); the gateway
  forwards `Authorization` through unmodified. (Revisit later to cut
  duplication.)
- **It does NOT aggregate responses.** Services stay independently
  callable; response composition is a frontend/BFF concern (ADR-0107).
- **Realtime and media stay direct.** The per-kiosk WebSocket push
  (ADR-0076) and the WebRTC/SFU media (ADR-0011/0012) are **not**
  proxied through the gateway, so the `Event → overlay state ≤ 200 ms`
  and `SFU → kiosk decode ≤ 120 ms` legs are untouched.
- **External edge only.** Internal service-to-service traffic is via
  RabbitMQ (ADR-0010) and does not route through the gateway; there is
  no east-west proxying.

## Consequences

**Positive:**

- One external front door; CORS / TLS / rate-limit defined and audited
  once instead of nine times.
- Browser apps target a single base URL (simpler client config + a
  single CORS origin set).
- YARP is high-performance — streaming (no body buffering), pooled
  `SocketsHttpHandler`, HTTP/2 to backends. The added hop is
  sub-millisecond to a few ms and lands only on REST/CRUD paths that
  tolerate it; it is **N/A to the §IV latency budget** by construction
  (the budget paths bypass it).

**Negative:**

- A new deployable and a **single point of failure on the REST path** —
  must run ≥ 2 replicas with health checks (it is stateless, so it
  scales horizontally).
- One more thing to operate: routes, rate-limit policies, CORS config.
- Direct per-service access still exists for internal callers and tests;
  the gateway is additive at the edge, not a hard chokepoint.

## Alternatives Considered

- **Stay gateless (direct access + k3s Ingress) — rejected for v1.**
  Ingress can terminate TLS but not express app-aware per-fab rate
  limiting or a shared CORS policy cleanly; edge concerns stay
  duplicated per service.
- **BFF per frontend — rejected for v1** (see ADR-0107 for the related
  frontend decision). More deployables, and response aggregation isn't
  needed yet. Revisit if the frontends need tailored composition.
- **Centralized auth offload at the gateway — deferred.** Keep
  per-service validation (defense in depth); revisit to remove
  duplication once the gateway is proven.
- **Ocelot / nginx / Envoy — rejected.** YARP is .NET-native: same
  stack, ASP.NET Core middleware for rate limiting and CORS, in-process
  typed config, and team familiarity. nginx/Envoy add a non-.NET
  operational surface for no capability we need here.

## Implementation Notes

- New project `src/ApiGateway/SmartSentinelEye.ApiGateway`
  (ASP.NET Core + `Yarp.ReverseProxy`). Routes/clusters from
  `appsettings` bound to service discovery.
- AppHost: `builder.AddProject<Projects.SmartSentinelEye_ApiGateway>("api-gateway")`
  with a `.WithReference(...)` to each context API, `.WithExternalHttpEndpoints()`.
  Browser apps reference **the gateway** for REST and keep the realtime
  WebSocket endpoint direct.
- k3s/Helm: the gateway is the only externally-exposed HTTP service
  (besides the realtime WS edge); Ingress targets it.
- NetArchTest: the gateway may reference `Shared.Contracts` /
  `Shared.Kernel` only — no cross-context domain references.
  (`ApiGateway_references_no_bounded_context`.)
- **Latency note (verified, #1006).** A NetArchTest
  (`ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`)
  asserts the gateway assembly depends on no SignalR / WebSocket
  transport, so the per-kiosk WebSocket push (ADR-0076) and the
  WebRTC/SFU media (ADR-0011/0012) cannot route through it. The route
  table carries only the nine REST contexts — no WS/media routes — so
  the `event → overlay state` and `SFU → kiosk decode` legs never
  traverse the gateway hop. The added hop is the sub-millisecond YARP
  proxy on REST/CRUD paths only, and is **N/A to the §IV budget** by
  construction.
