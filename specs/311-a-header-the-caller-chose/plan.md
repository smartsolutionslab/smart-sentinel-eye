# Plan 311 — A header the caller chose

Spec: [`spec.md`](spec.md). Issue #2283. ADRs 0106, 0153 (clause 2), 0103, 0139, 0144.

## 1. Shape

No bounded context is touched. Three files of production code, all edge/composition:

| File | Change |
|---|---|
| `src/ApiGateway/Program.cs` | Delete `fabHeader`; `ResolveFabPartition` → `ResolveSourcePartition(HttpContext)` returning `ip:{RemoteIpAddress}` (or `ip:unknown`). Correct the rate-limit block comment, the spec-208 note's X-Fab sentence, and the function comment (FR-007). |
| `src/ApiGateway/appsettings.json` | Remove `RateLimiting:FabHeader`. Add `{ "RequestHeaderRemove": "X-Fab" }` to the `Transforms` of **every** route (nine). |
| `src/AppHost/AppHost.cs` | Remove the `if (!isE2ETests) { apiGateway.WithReplicas(2); }` block; replace the HA comment with a short record: one instance per ADR-0153 clause 1, chosen under clause 2 by #2283 because no shared store exists. Touch the spec-232 comment only where it says "per replica" ambiguously — the 6000/min dev budget stays. |

### 1.1 Why config transforms, not a code `AddTransforms`

A code-level `AddTransforms(ctx => ctx.AddRequestHeaderRemove("X-Fab"))` cannot miss a route, but
it is only observable by hosting the gateway's own `Program` — no test project hosts it
(no `WebApplicationFactory` anywhere in `tests/`). The config form is executable from a test with
YARP's public pipeline (`AddReverseProxy().LoadFromConfig(...)` + `ITransformBuilder`) against the
real `appsettings.json`, and the test iterates **every** configured route, so a tenth route added
without the transform fails it. That is the stronger guarantee of the two in this repo.

### 1.2 Why not keep `X-Fab` as a secondary key (e.g. `ip+fab`)

Composing it in still lets one source split its budget into N windows. Any header in the key is the
defect.

## 2. Entities / invariants

None (no domain). Invariant of the edge: *a caller's partition is a function of its connection,
never of its request.* Budget per source = `PermitLimit` per `Window`, exactly, because there is
one limiter instance.

## 3. Messaging

None.

## 4. Boundary rules

Unchanged. `ApiGateway_references_no_bounded_context` and
`ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` must stay green. No
`Shared.Contracts` change. Integration.Tests gains a `Yarp.ReverseProxy` package reference (version
from `Directory.Packages.props`) — not a project reference to the gateway.

## 5. Tests (behaviour-changing → red first, ADR-0139)

All in `tests/Integration.Tests`.

1. **`GatewayRateLimitIntegrationTests` (Aspire fixture, existing class — already in
   `ci-shards/shard-1.filter`).**
   - **New** `A_caller_rotating_X_Fab_is_still_refused_once_over_its_limit`: 101 proxied
     `GET /camera-catalog/health`, `X-Fab: rot-{n}`; assert the count of non-429 responses is
     `<= 100` (the configured production `PermitLimit`, which `AppHostGatewayRateBudgetTests` pins
     for the fixture). Asserted as a ceiling on successes, not "≥ 1 refusal after N", so it is not
     vacuous whatever state the shared partition starts in, and it fails deterministically today
     (every distinct header is a fresh partition → 101 successes).
   - **Invert** the existing test's second assertion: after exhausting with `rl-fab-a`, a request
     with `rl-fab-b` is **429**; and the gateway's own `/health` is still **200**. Rename to say so
     (e.g. `Gateway_refuses_a_source_over_its_limit_whatever_fab_header_it_sends`). Doc comment
     rewritten. This is a strictening, not a weakened gate.
   - **Shared-partition hygiene.** In the fixture every caller collapses to one source address, and
     `GatewayRoutingIntegrationTests` uses the same gateway. Each test that exhausts the window must
     end (in `finally`) by polling until a request is answered non-429, bounded by `Window` + 15 s,
     so later gateway tests in the same process start unthrottled. xUnit runs a collection
     serially, so this is sufficient. Fixed window = recovers ≤ 60 s.
2. **`GatewayEdgeHeaderTransformTests` (new class, `[Trait("Category","FixtureLogic")]`, no stack
   boot).** Load `src/ApiGateway/appsettings.json` (path resolved from the repo root the way other
   FixtureLogic tests resolve source files — normalise separators, memory: *source-scanning tests
   need slash normalising*), build a `ServiceCollection` with `AddReverseProxy().LoadFromConfig`,
   resolve `IProxyConfigProvider` + `ITransformBuilder`, and for **each** route build its
   transformer and run `TransformRequestAsync` on a `DefaultHttpContext` carrying `X-Fab: forged`
   and `Authorization: Bearer x`. Assert the outbound `HttpRequestMessage` has no `X-Fab` and still
   carries `Authorization`. Assert the route count is ≥ 9 (non-vacuity). **New class → add to a
   shard filter** (memory: *new test classes need a shard-filter entry*; `shard-4.filter`, beside
   the other `AppHost*` FixtureLogic classes).
3. **`AppHostReplicaCountTests` (existing).** Run-mode assertion → `ShouldBeEmpty`, mirroring the
   integration-lane test; delete `GatewayException` and the #2283 paragraph of the doc comment.
   **Add a witness** (spec 169 §5.4): a throwaway `DistributedApplication.CreateBuilder` model with
   one project resource at `WithReplicas(2)` (path-string `AddProject` overload; no build needed),
   asserting `ResourcesAboveOneInstance` reports it at 2 — proves the accessor still sees a count
   above one now the real model has none.

Red evidence expected on this tree: (1) new test fails with 101 successes; inverted assertion
fails with `200` for `rl-fab-b`; (2) fails — `X-Fab` present on every route's outbound request;
(3) run-mode assertion fails with `api-gateway = 2`. The witness test is a characterisation of the
accessor and may arrive green — it is new *infrastructure for the guard*, not new behaviour; say
so in the PR.

**Counterfactual for (1)** (memory: *prove a guard by counterfactual*): after the fix, temporarily
re-add the header to the key and observe the test fail again; quote it in the PR.

## 6. Risks

- **Dev/e2e budget halves in effect.** Run mode had 2 × 6000/min; it becomes 6000/min. Spec 232's
  derivation needed ~2 950/min for ~12 concurrent walls, so it still fits; phase 5 should watch the
  Playwright job for 429s anyway.
- **HA.** One gateway instance is a REST-path SPOF. Accepted system-wide by ADR-0153 clause 1; no
  deployment artefact exists that could run two pods today (ADR-0153 §*Two arguments*).

## 7. Contention

`AppHost.cs` and `ci-shards/shard-4.filter` are high-contention files — rebase immediately before
push. No other spec in flight touches `src/ApiGateway`.

## 8. Constitution / ADR check

- §II primitives: no domain model touched. §IV: N/A (REST only, ADR-0106 latency note).
- ADR-0153 clause 2: choice exercised as delegated. Clause 3 (earning a second instance) is moot.
- ADR-0106: Decision honoured (no auth offload; "per-client" limits). Its *"must run ≥ 2
  replicas"* consequence and *"fab claim/header"* wording become stale → **recommend** a human
  clerical note (spec §0). Not written here (ADR-0144).
- ADR-0103: integration via Aspire fixture; the transform test boots nothing.

## 9. Implementer

**infra-engineer** — gateway plumbing and AppHost wiring are both in its brief; no bounded-context
code. Tests by test-writer first.
