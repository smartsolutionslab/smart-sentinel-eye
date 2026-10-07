# Spec 311 — A header the caller chose

**Issue:** [#2283](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2283)
— *Gateway rate limit: partition key is caller-chosen (`X-Fab`), and the limiter is per-process
behind two replicas*. Found by the 2026-09-13 security and infra reviews.
**Branch:** `fix/2283-gateway-ratelimit-xfab-bypass` (cut from `origin/develop` @ `d238b979`)
**Created:** 2026-10-07 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0106 (API gateway), 0153 (one instance per service — clause 2 delegates this choice to
#2283), 0159 (multi-fab deployment), 0103 (Aspire fixture), 0139 (red first), 0144 (lane).
**Related:** #2268, #2563 (IP partition behind a shared NAT/Ingress — out of scope here), #1005.

**Spec number.** On 2026-10-07 `origin/develop` tops out at **310**; no remote branch and neither
other worktree (`sse-2662`) carries `specs/311-*`. Re-check immediately before opening the PR.

## 0. The two decisions this spec makes, and why neither needs an ADR

**Partition key → source IP, inbound `X-Fab` stripped.** A verified fab claim does not exist at
the edge, by decision and by construction:

- ADR-0106 *Decision*: the gateway *"does NOT do centralized auth offload. Per-service JWT
  validation stays … the gateway forwards `Authorization` through unmodified."* `Program.cs`
  registers no authentication handler; the code comment (`Program.cs:25-27`) says the same.
- The route the issue names — `/event-ingestion/events/webhook/*` — is not authenticated with a
  JWT at all. Its caller presents an opaque bearer token that only EventIngestion can check,
  against a stored hash (`EventsEndpoints.Writes.cs:198 AuthenticateWebhookAsync`). There is no
  claim, verified or otherwise, for the gateway to read on that path.
- Decoding a JWT without validating it would be the same caller-chosen key under a different name.

So "verified claim" would mean adding JWT validation to the gateway — reopening ADR-0106's
deferred auth-offload decision. Source IP is the only key the caller cannot choose freely (it is
bound by the TCP handshake). ADR-0106 already names *"per-fab / per-client limits"*; per-client
by source address is inside that wording.

No backend reads `X-Fab` (`grep -rn "X-Fab" src --include=*.cs` outside `ApiGateway/` finds only
two comments), and no client sends it (`AppHost.cs:670`). Stripping it changes nothing for any
legitimate caller and closes the header as a future trust vector.

**Replicas → one.** ADR-0153 clause 2 (accepted 2026-09-16): *"the gateway's rate limiter must
either move to a shared store or the replica count must come back to one. **Choosing between
those is #2283's job, not this ADR's.**"* This spec chooses **one replica**:

- **No shared store exists.** No Redis, Garnet or `IDistributedCache` package or resource anywhere
  in `src/`, `Directory.Packages.props` or `AppHost.cs` (only a name in a health-check allow-list).
  Adding one is a new runtime resource and a new technology on the stack table — that *would* be
  an ADR, which the lane may not write (ADR-0144).
- **One replica is the system's stated default** (ADR-0153 clause 1) and every other service
  already runs at one. The SPOF consequence ADR-0106 lists was accepted system-wide by ADR-0153;
  ADR-0153 also records that no deployment artefact exists that could run two pods today.
- With one replica, the integration lane's topology *is* the shipped topology. The issue's
  *"the test must exercise the replicated shape"* is satisfied by removing the divergence, not by
  testing a shape that is no longer shipped.

**Recommended follow-up (a human's, not this lane's):** a one-line note in ADR-0106 that its
*"must run ≥ 2 replicas"* consequence and *"partitioned on the fab claim/header"* wording are
superseded by ADR-0153 clause 2 as exercised by #2283, and the descriptive *"the gateway's rate
limiter partitions on `X-Fab`"* in ADR-0159 §Context. These are clerical — the governing decision
(ADR-0153) already authorises the change — so they do not block implementation.

## 1. The premise, re-checked on this tree (`d238b979`)

- `src/ApiGateway/Program.cs:29` reads `RateLimiting:FabHeader` (default `X-Fab`);
  `:95-105 ResolveFabPartition` returns `fab:{header}` when the header is non-blank, else
  `ip:{RemoteIpAddress}`. **Confirmed**: any caller picks its own partition.
- `Program.cs:32-44` — in-process `RateLimitPartition.GetFixedWindowLimiter`, no shared store.
  **Confirmed.**
- `src/AppHost/AppHost.cs:652-664` — `apiGateway.WithReplicas(2)` under `if (!isE2ETests)`;
  comment says so the rate-limit tests *"resolve a single endpoint"*. **Confirmed** (the issue's
  537-540 is stale).
- `tests/Integration.Tests/ApiGateway/GatewayRateLimitIntegrationTests.cs` asserts that
  `X-Fab: rl-fab-b` gets `200` after `rl-fab-a` is exhausted — i.e. it **pins the bypass as
  intended behaviour**. Its second assertion must invert (FR-002). This is the behaviour change the
  issue asks for, not a weakened gate (ADR-0144): the assertion becomes stricter.
- `tests/Integration.Tests/AppHostReplicaCountTests.cs:89` pins `api-gateway = 2` in run mode and
  uses it as the guard's only liveness witness (`:36-45`, which says what to do if #2283 returns
  the gateway to one).
- Memory-side growth: idle partitions are reaped by the partitioned limiter, so "unbounded" is an
  overstatement; but the population is caller-driven. With an IP key it is bounded by addresses
  that can complete a TCP handshake.

## 2. User stories

### US1 (P1) — A caller cannot mint itself a fresh rate-limit window

As the plant operator, I need the edge rate limit to bound a caller regardless of the headers it
sends, so that the webhook ingest route cannot be flooded by rotating `X-Fab`.

### US2 (P1) — The rate limit that is tested is the rate limit that ships

As the maintainer, I need the gateway to run in the same instance count in every lane, so that a
caller's budget is exactly `PermitLimit` per window and the integration test proves it.

Both are P1 and ship in one slice: US1 alone leaves the budget at 100–200/min depending on replica,
US2 alone leaves it bypassable. Neither is observable as "fixed" without the other.

## 3. Functional requirements

- **FR-001** The rate-limit policy partitions on the connection's source address only. No request
  header contributes to the partition key.
- **FR-002** Requests from one source that exceed `PermitLimit` within `Window` are refused `429`,
  whatever `X-Fab` value (or none) each carries.
- **FR-003** The gateway removes any inbound `X-Fab` header before proxying, on every route.
- **FR-004** `RateLimiting:FabHeader` configuration is removed (no dead knob).
- **FR-005** `api-gateway` runs one instance in every AppHost mode; the `isE2ETests` replica
  exception is removed.
- **FR-006** `AppHostReplicaCountTests` asserts no resource above one instance in run mode, and
  keeps a liveness witness proving its accessor can see a count above one (spec 169 §5.4).
- **FR-007** Comments that describe the old behaviour are corrected in the same change:
  `Program.cs` (rate-limit block, `ResolveFabPartition`, the spec-208 note's *"partitions on the
  caller-supplied X-Fab header"*), `AppHost.cs` HA block and spec-232 note, the two test classes'
  doc comments. Policy name `per-fab` was originally kept at this phase (route config references
  it; renaming looked like pure churn) — phase 6 review on the implementing branch judged the name
  itself actively misleading once the key was source-IP, not X-Fab, and it was renamed to
  `per-source` across `Program.cs` and all nine `appsettings.json` route entries in a follow-up
  commit. No test references the string literal.

## 4. Acceptance scenarios

```gherkin
Scenario: Rotating X-Fab does not reset the window (happy path of the fix / the red test)
  Given the gateway's PermitLimit is 100 per minute
  When one source sends 101 proxied requests within the window, each with a distinct X-Fab
  Then at least one response is 429

Scenario: No header, same behaviour
  Given one source has exhausted its window without sending X-Fab
  When it sends a request with X-Fab "some-other-fab"
  Then the response is 429

Scenario: The gateway's own health endpoints stay unthrottled
  Given one source has exhausted its window
  When it calls /health on the gateway
  Then the response is 200

Scenario: X-Fab never reaches a backend (bad-request / spoof)
  Given a request to any proxied route carrying "X-Fab: forged"
  When the gateway builds the outbound request
  Then the outbound request carries no X-Fab header

Scenario: Authorization is unaffected (auth)
  Given a request carrying a bearer token
  When the gateway proxies it
  Then the Authorization header is forwarded unmodified and the backend decides 401/403 as before

Scenario: One instance everywhere (topology)
  Given the AppHost composed in run mode, and again with E2ETests=true
  Then api-gateway has one replica in both
```

No conflict (409) scenario applies — the gateway holds no versioned state.

## 5. Independent end-to-end test procedure

1. Boot the stack via the Aspire fixture (`E2ETests=true`, production `PermitLimit` 100).
2. From one `HttpClient`, send 101 `GET /camera-catalog/health` through `api-gateway`, header
   `X-Fab: rot-{n}`. Observe ≥ 1 `429`. On `develop` today: zero `429`.
3. Wait out the window (the shared integration-stack partition must be left usable for other
   gateway tests — see plan §5) and confirm a request succeeds again.
4. Compose the AppHost model in run mode; observe `api-gateway` replica count 1.

## 6. Locked tech and latency

- ASP.NET Core `RateLimiter` + YARP transforms (ADR-0106); no new package, no new resource.
- **Latency budget: N/A.** The gateway carries REST only; realtime SignalR and WebRTC media bypass
  it (ADR-0106 *Latency note*, architecture test
  `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`). No §IV leg is touched.

## 7. Out of scope, recorded

- **#2563** — behind a k3s Ingress or a plant NAT, every caller shares one source address unless
  forwarded headers are trusted from a known proxy. No `UseForwardedHeaders` is configured today,
  so the IP fallback that already governs every browser has this property now; this spec does not
  change it. Trusting `X-Forwarded-For` without a `KnownProxies` list would recreate this very
  defect, so it belongs with #2563's deploy-topology work.
- **Per-fab fairness.** The old key promised one fab's burst could not starve another. It never
  held (caller-chosen, and no client sent it). Restoring it needs a verified fab at the edge —
  gateway JWT validation — which is ADR-0106's deferred decision.
- **IPv6 address rotation within a prefix** — a residual of any IP key; not addressed.

## 8. Phase-4a colour

**Behaviour-changing → red first.** The 101-rotating-header test and the inverted second assertion
of `GatewayRateLimitIntegrationTests` must be observed failing on this tree before the fix.
