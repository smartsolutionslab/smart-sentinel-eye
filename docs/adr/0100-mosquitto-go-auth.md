# ADR-0100: Mosquitto JWT/JWKS auth plugin

**Status:** **Accepted** — implemented via a custom Go plugin (see Addendum 2026-05-31)
**Date:** 2026-05-29
**Supersedes:** —
**Superseded by:** —

## Addendum (2026-05-31) — Un-parked: custom Go plugin

The 2026-05-29 parking was re-investigated and its core finding
**confirmed against the upstream source**: `iegomez/mosquitto-go-auth`
verifies a JWT's signature with the configured secret as raw HMAC
bytes (`backends/jwt.go` returns `[]byte(secret)` from its keyfunc) —
there is no asymmetric/PEM path and no JWKS option. Keycloak realm
tokens are **RS256**, so that plugin can never validate them locally.
The parked addendum's three claims were accurate.

**Resolution (chosen over swapping the broker or per-CONNECT
introspection):** a small custom Mosquitto v5 plugin written in Go,
living in `src/AppHost/mosquitto/plugin/`:

- On `MOSQ_EVT_BASIC_AUTH` it treats the MQTT password as a JWT and
  verifies the RS256 signature against the realm JWKS, which is
  fetched once and kept fresh in-process (`MicahParks/keyfunc`), so a
  CONNECT never round-trips to Keycloak — keeping the NFR-002 ≤ 5 ms
  p99 budget. Rotation is handled by keyfunc's refresh-on-unknown-kid
  (FR-006).
- It enforces `azp == username` (the device's Keycloak client_id) and
  that the issuer is our realm, plus `exp`.
- A non-JWT password returns `MOSQ_ERR_PLUGIN_DEFER`, so the spec-006
  password_file users (station-4, camera-12, event-ingestion) keep
  authenticating unchanged.

The image is built from `src/AppHost/mosquitto/Dockerfile`: Mosquitto
2.0.18 and the plugin are both compiled on Debian/glibc, which avoids
the musl "initial-exec TLS resolves to dynamic" load error that
blocked the Alpine attempt (parked-claim 3). AppHost builds it via
`AddDockerfile` and injects the container-reachable JWKS URL.

**Scope note:** this PR ships connect-time authentication (NFR-002).
The JSON-from-JWT publish **ACL** described under "Decision" below
(scope/groups/topic binding) is not yet enforced by the plugin —
device usernames are simply absent from `acl.txt`, so they are
deny-by-default for publish/subscribe until that follow-up lands.

## Addendum (2026-05-29) — Decision parked

Hands-on bring-up against the real broker surfaced three
fundamental gaps between the documented v1 plan and what
`mosquitto-go-auth` actually supports:

1. **No pure-JWKS validation mode.** `auth_opt_jwt_mode local`
   requires a SQL DB + `jwt_userquery` for the user lookup —
   *not* the lightweight signature-only verification the spec
   asked for. `auth_opt_jwt_jwks_uri` (referenced throughout
   the implementation plan and in the dev `go-auth.conf` draft)
   is **not a supported option in v3.0.0**.
2. **Asymmetric keys are passed via `auth_opt_jwt_secret` as
   PEM**, not via a JWKS URI. JWKS rotation handling is the
   caller's responsibility — defeats the "24 h cache + auto-
   refresh on signature failure" property we relied on for
   FR-006.
3. **The Go shared library can't load into the Alpine-based
   `eclipse-mosquitto:2.0` image** (`runtime.tlsg: initial-exec
   TLS resolves to dynamic`). Upstream's own Dockerfile
   sidesteps this by rebuilding mosquitto from source on a
   Debian image — a heavier image with its own ops
   implications.

The remaining `jwt_mode remote` option *would* work end-to-end
but requires a per-CONNECT HTTP round-trip to Keycloak's
userinfo endpoint, which violates the NFR-002 ≤ 5 ms p99
budget that justified picking this plugin in the first place.

**What this means.** ADR-0100's decision is parked until the
Mosquitto-side auth model is re-designed. The likely paths,
in rough order of cost:

- Write a thin custom Go plugin that does JWKS-cached
  validation directly (≈ 200 LOC).
- Switch the broker to one with first-class OIDC/JWT support
  (EMQX, NanoMQ) — bigger change.
- Accept the per-CONNECT introspection cost and revise NFR-002.

Until that resumption, the dev stack runs the upstream
`eclipse-mosquitto:2.0` image with `passwords.txt` + `acl.txt`
(spec 006 ADR-0095). The drafted `mosquitto-go-auth` config +
Dockerfile stay in `src/AppHost/mosquitto/` and `conf.d/`
as the starting point for the resumption. Spec 008 ships the
HTTP-API side (Identity + scope catalogue + fab guard) with
NFR-002 explicitly parked alongside this ADR.

**Issues opened off this addendum:** TBD.

## Context

Spec 008 (Identity) lifts every fab-floor device — PLCs, cameras,
webhook integrations — into Keycloak as service-account clients.
Devices then authenticate to MQTT by presenting their
Keycloak-minted JWT as the MQTT password. The bare Mosquitto
`password_file + acl_file` model (spec 006 ADR-0095) can't
validate JWTs; we need a plugin.

Spec 008 NFR-002 budgets **≤ 5 ms p99 per device-connect**. We
already operate at sustained 1 000 ev/s of MQTT publishes
(spec 006 NFR-002); per-message auth overhead must remain
**zero** so the 50 ms ingest budget stays intact. That means
auth runs **per-connect, not per-message**, with cached JWKS
verification.

## Decision

**Mosquitto loads the `mosquitto-go-auth` plugin** (the
`iegomez/mosquitto-go-auth` open-source plugin) configured in
**JWT mode** with a **24 h JWKS cache** against the Keycloak
realm.

- Devices connect with `Username=<keycloakClientId>`,
  `Password=<JWT>`.
- The plugin verifies the JWT's signature against the cached
  realm JWKS, checks the `exp` claim, then evaluates the ACL.
- The ACL is **JSON-from-JWT**: a publish to topic
  `fab/<fabId>/<source>/<deviceId>` is allowed iff
  - the `scope` claim contains `sse.events.publish`,
  - the `groups` claim contains `/fabs/<fabId>`,
  - the `azp` claim (the Keycloak client_id) equals
    `<source>-<deviceId>`.
- JWKS refresh: on first connect, then every 24 h, plus a
  forced refresh on any signature-validation failure (handles
  signing-key rotation transparently — FR-006).

## Consequences

**Positive:**

- Single source of truth for device credentials (Keycloak), no
  static password file to keep in sync.
- Revocation = remaining JWT TTL (24 h for devices); emergency
  revocation by rotating the Keycloak signing key.
- Per-message overhead is **zero** — auth runs once per TCP
  connect, then Mosquitto trusts the session for its lifetime.
- Sub-millisecond signature verification once JWKS is cached.
- Same Mosquitto image works for dev (Aspire) and prod (Helm).

**Negative:**

- A new Mosquitto-side dependency: the plugin is a Go shared
  library loaded by Mosquitto. The eclipse-mosquitto Docker
  image doesn't ship it; we either consume the maintainer's
  image (`iegomez/mosquitto-go-auth`) or build our own
  Dockerfile that adds `go-auth.so` to the upstream image.
- One extra moving part to monitor: the plugin's own log
  output sits alongside Mosquitto's. Operationally OK; just
  documentation.
- Stale credentials within the 24 h TTL: a compromised device
  JWT remains valid until expiry unless the signing key is
  rotated.

## Alternatives Considered

**Per-connect HTTP introspection against Keycloak's
`/token/introspect` — REJECTED.** Pros: real-time revocation
(introspect always returns latest state). Cons: every device
reconnect adds a ≥ 5 ms RTT to Keycloak; if Keycloak hiccups,
no device can reconnect; tighter coupling between the MQTT
broker and Keycloak's availability. We get the same revocation
behaviour by rotating the signing key when needed.

**Mosquitto password file populated by an Identity-owned
cron — REJECTED.** Pros: zero changes to Mosquitto's config.
Cons: stale-credential window between cron runs; two sources
of truth for credentials (Keycloak + the file); brittle — a
cron failure leaves the device fleet in a half-broken state.

**`mosquitto-auth-plug` (C plugin, predecessor of
`mosquitto-go-auth`) — REJECTED.** No active maintenance since
2021; missing JWKS-cache support; predates Keycloak's modern
JWT shape.

**Run an EMQX broker instead — REJECTED.** EMQX has built-in
JWT auth via Keycloak; would replace Mosquitto entirely. The
spec 006 decision (ADR-0095) explicitly chose Mosquitto for
its operational simplicity at 1 k/s; swapping the broker now
would be a much larger change than adding a plugin.

## Implementation Notes

- AppHost composes Mosquitto from a custom Dockerfile that
  starts FROM the upstream `eclipse-mosquitto:2.0` and adds
  the prebuilt `go-auth.so` plugin (or, for v1, we consume
  `iegomez/mosquitto-go-auth:latest` directly).
- Config files live at:
  - `src/AppHost/mosquitto/mosquitto.conf` — loads the plugin
    via `auth_plugin /mosquitto/go-auth.so`.
  - `src/AppHost/mosquitto/go-auth.conf` — JWT mode + Keycloak
    JWKS URL + 24 h cache + ACL expression.
- Production Helm chart adds the same plugin layer to its
  Mosquitto StatefulSet image (spec 006 T107 fragment gets a
  follow-up commit).
- The grandfathered Mosquitto password file (spec 006 ADR-0095)
  keeps working for legacy devices for one release cycle, then
  spec 008b removes it.

## Performance Validation

- Plugin's bundled benchmark: ~ 50 µs per JWT validation once
  JWKS is cached. Easily clears the 5 ms p99 connect-time
  budget.
- Per-message overhead: zero — Mosquitto trusts the TCP
  session for its lifetime.
- Integration test `NFR002_MqttConnectAuthTests` (spec 008
  T088) asserts p99 ≤ 5 ms over a warm 100-cycle connect test
  against a Testcontainers Keycloak + Mosquitto.
