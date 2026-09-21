# Spec 208 — A ceiling the hook never had

**Issue:** #2284 — *"`/streams/authorize` is anonymous by design and nothing rate-limits it"*
(the issue's *title* reads `C:/Program Files/Git/streams/authorize` — a Git-Bash path expansion of `/streams/authorize` at filing time. The route is `/streams/authorize`; no such file path exists or is referenced anywhere in this repo.)

**Branch:** `2284-stream-authorize-rate-limit`
**Verified against:** working tree at `1788d773` (`origin/develop` tip). Every line number below was re-read here, not copied from the issue — which is a year old and whose surrounding code has moved (specs 089, 115, 119, 120 all landed on this path since).

**Phase-4a colour: RED (behaviour-changing), all three stories.** The route gains a status it cannot produce today. A test that arrives green is a phase-4 failure, not a shortcut (ADR-0139, constitution §Testing, CLAUDE.md §House rules).

| Story | Colour | Why |
|---|---|---|
| **US1** | **RED** | `POST /streams/authorize` answers `429` where today it answers `401`/`403`/`200` forever. New behaviour. |
| **US2** | **RED** | A rejection emits a log record that no code path can emit today. |
| **US3** | **RED** | The census gains M15. The assertion fails on the census as it stands, which is the point of a register. |

**Phase 6: `security-reviewer` is mandatory, regardless of diff size.** This is the only control that will exist on an unauthenticated, credential-free endpoint that gates every wall's video. Same shape as spec 207 / #2285 (this repo's only account-lockout control — small diff, security-critical): a control whose *absence* is invisible and whose *misconfiguration* is a fab-wide video outage.

**ADRs:** **ADR-0106** (the gateway owns rate limiting *at the edge* — see §*Does this need an ADR?*, the one place this spec sits near a decision boundary), **ADR-0070** (Minimal APIs — the chain is where the policy attaches), **ADR-0011/0012** (the WebRTC SFU whose external-auth hook this is), ADR-0007/0008 (Keycloak per fab — what the hook validates against), ADR-0047 + ADR-0089 (`Result<T, Error>` / `ApiError` — the refusal surface, unchanged: a `429` comes from middleware and never becomes an `AuthorizeWhepError`), ADR-0050 (`ILogger<T>` + `[LoggerMessage]` source-gen, structured fields — US2), ADR-0051 (per-context DI composition — why the registration lands in `Program.cs` and not in `AddStreamDistributionApi`), ADR-0052/0053 (xUnit + Shouldly, sentence-style names), ADR-0103 (integration tests via the Aspire fixture, no Testcontainers), ADR-0105 (`Ensure.That` guards), ADR-0109 (`[P]` markers), ADR-0118 (one telemetry sink — US2 must not flood it), **ADR-0139** (new behaviour starts red), **ADR-0143** (retry safety — see §*ADR-0143: does anything retry into this ceiling?*), ADR-0144 (autonomous lane), ADR-0037 (phases).

**Constitution:** **§VIII** (untrusted input is validated at the trust boundary — this *is* the boundary, and an unbounded one), §Security (token-bound short-lived credentials; this hook is what binds a stream to one), §VII (observability — US2), §IV (latency budget — see §*Latency-budget impact*), §NFR (250 concurrent cameras per fab; a wall of 20 kiosks — the two numbers that set the ceiling), §Testing (red for new behaviour).

---

## The premise, re-verified

All four claims in the issue hold, two of them with corrections that matter to the design.

### 1. The route is still `AllowAnonymous` — confirmed

`src/StreamDistribution/Api/StreamEndpoints.cs:63-64`:

```csharp
group.MapPost("/authorize", AuthorizeWhep)
    .AllowAnonymous()
```

Deliberate, and it must stay: MediaMTX's external-auth hook posts the stream path and the viewer's bearer **in the JSON body**, not as an `Authorization` header, so no authentication middleware can judge it. The handler validates the forwarded token itself (`AuthorizeWhepCommandHandler.cs:33-92`).

### 2. The code still concedes that nothing rate-limits it — confirmed, at moved line numbers

The issue cites `WhepAuthValidator.cs:128,196`. At `1788d773` the two concessions are at **`:142-143`** and **`:210-212`**:

> `// Logged on the transition, not per request. /streams/authorize is AllowAnonymous and nothing rate-limits it, so one Warning and one full exception chain per WHEP open floods the single OTLP sink…`

> `/// <c>/streams/authorize</c> is <c>AllowAnonymous</c> and nothing rate-limits it, so refreshing on every rejection turns a kiosk reconnect loop replaying an expired token into a JWKS storm…`

Both are load-bearing comments explaining *other* mitigations (spec 119's transition-only logging; spec 120's refresh discrimination) that exist **because** this ceiling does not. Correcting them is in scope (US1): a comment that names an absent control is how the next reader re-derives the same workaround.

### 3. MediaMTX does not reach it through the gateway — confirmed

`src/AppHost/Resources/mediamtx.yml:44-45`:

```yaml
authMethod: http
authHTTPAddress: http://stream-distribution:8080/streams/authorize
```

Service DNS, straight at the service. ADR-0106 classifies exactly this as internal traffic that does not route through the gateway.

### 4. …but "the gateway's limiter does not even nominally apply" is **too kind to the gateway**

The gateway's `stream-distribution` route is a catch-all (`src/ApiGateway/appsettings.json:34-40`, `"Path": "/stream-distribution/{**catch-all}"`), so `POST /stream-distribution/streams/authorize` **is** proxied, and #2238's body names that exact path as a live attack surface. The `per-fab` policy nominally applies to it — and is defeated in one header:

`src/ApiGateway/Program.cs:69-79` partitions on `context.Request.Headers["X-Fab"]`, **a string the caller invents**. An attacker rotating `X-Fab: 1, 2, 3, …` gets a fresh 100-request window per value, unbounded in aggregate. The gateway's own comment concedes the mechanism (*"the partition key is a trusted-edge header rather than a verified claim"*) without noticing that this route is reachable anonymously, so there is no authenticated caller behind the trust assumption.

**This is worse than no control, because it looks like one.** It is the decisive argument for putting the ceiling on the service rather than widening the gateway's policy: a limiter on `StreamEndpoints` closes both routes at once — the direct one and the proxied one — and keys on something the caller cannot choose.

### 5. What an anonymous attacker actually gets is **less than the issue says**, and it changes the oracle decision

The issue's "Gets" paragraph claims an unbounded RSA-verify **plus an indexed DB read** per request, and a three-way oracle. Read against `AuthorizeWhepCommandHandler.HandleAsync` (`:39-92`), the check order is **action → token presence → token validation → scope → stream state**:

| Cost / signal | Reachable with no credential? | Where |
|---|---|---|
| JSON body parse, `MediaMtxPath.From` syntax check | yes | `StreamEndpoints.cs:358-369` |
| action check (`403` unknown / not-permitted) | yes | handler `:46-62` |
| JWT parse + **signature verification attempt** | yes — a structurally valid JWT with a plausible `kid` forces real RSA work | `WhepAuthValidator.cs:167` |
| **on-demand JWKS re-fetch** on an unknown `kid` | yes — floored at 5 min by `RefreshInterval`, #2238's mechanism | `:228-237` |
| scope check (`403 WHEP_FORBIDDEN`) | **no** — behind a fully validated token | handler `:79-83` |
| **indexed DB read** `GetByPathAsync` | **no** — behind token *and* scope | handler `:85` |
| stream-offline (`403 WHEP_STREAM_UNAVAILABLE`) | **no** | handler `:87-90` |

So the DB read is **not** anonymously reachable, and the RSA verify is. The CPU finding stands; the database half of it does not.

And the oracle the issue describes is not an anonymous oracle at all. See §*Scope decision: the oracle*.

---

## Scope decision: the oracle

**Out of scope for this spec. No follow-up issue is filed for the anonymous case, because there is no anonymous oracle to collapse.** Recorded here rather than left silent, because "what an attacker gets" naming something that "done looks like" does not address is exactly the kind of gap that gets rediscovered.

Three reasons, in order of weight:

1. **The disclosing half needs a credential, and it is already filed.** Per the table above, `wrong scope` (403) and `stream offline` (403) both sit behind a fully validated bearer. The population the issue defines — *"reach to the StreamDistribution port and no credential at all"* — can distinguish only `401`, `403 WHEP_INVALID_PATH` (a regex on `cam-{guid}`), and `403 WHEP_ACTION_UNKNOWN` / `WHEP_ACTION_NOT_PERMITTED`. **Every one of those is a fact about the request the attacker themselves sent.** None reveals whether a camera, a stream or a fab exists.

2. **The oracle that *is* real belongs to #2092.** A caller holding any valid `sse.streams.read` token — which per #2092's own comment thread is now *every operator* (#2279) — can already enumerate: handler `:85-92` returns **`200` for a path that is not registered at all** and `403 WHEP_STREAM_UNAVAILABLE` for one that is registered and offline. That is a live existence oracle over the whole camera id-space, and it sits in the same six lines as #2092's missing fab check. Fixing it here would mean designing the fab gate #2092 is `agent:blocked` on — ADR-0144 bars this lane from writing that ADR. **Reported to the orchestrator for a comment on #2092; not acted on here.**

3. **Collapsing what remains would contradict a deliberate decision.** Spec 115 already applies this repo's *collapse the answer, log the difference* pattern to this endpoint: `AuthorizeWhepErrors.cs:44-53` and `RefuseUnknownAction` (`handler :119-129`) return an **identical** `WHEP_ACTION_UNKNOWN` 403 whether the action was absent or unrecognised, and put the diagnosis in the log. It is the same pattern `EventsEndpoints.cs:69` uses for the anonymous webhook route (*"every refusal collapses to one 401 so the answer never reveals which integrations exist"*). The pattern is present; there is nothing here it has not already been applied to.

**And the honest limit of this spec, stated plainly:** a rate limiter does not close an information-disclosure oracle. It bounds how fast one can be probed. If #2092's fix later needs the anonymous answers narrowed further, that is #2092's spec, not this one.

---

## ADR-0143: does anything retry into this ceiling?

Asked because a limiter that a *legitimate* client's own resilience logic trips is a self-inflicted outage. Three callers, all checked:

1. **Our HTTP clients → MediaMTX.** `StreamDistributionInfrastructureModule.cs:104` opts the MediaMTX gateway back into `RetryEveryMethod()` (ADR-0143's five justified opt-ins, named in CLAUDE.md). Those calls go to MediaMTX's **API** on `:9997` — and `mediamtx.yml:46-49` puts `action: api` in `authHTTPExclude`, so a retried `add/` or `patch/` produces **no authorize call at all**. **No self-amplification from ADR-0143's opt-in.** This is the clean answer: the one client in this context that retries non-idempotent methods cannot reach the limited route.

2. **MediaMTX → us, on a refusal.** Not configured anywhere in this repo and **not documented here**. `AuthorizeWhepErrors.cs:60-66` records that MediaMTX's handling of a `5xx` from this hook is *unobserved* — that is the whole of #2160. A `429` is in the same unobserved class. Two things bound the risk: the limiter answers a **non-2xx**, which MediaMTX treats as a deny like any other, and `QueueLimit = 0` means the refusal is immediate rather than a hung request. **Phase 5 must observe the actual behaviour** (see US1's verification), and it is a stated assumption, not a claim.

3. **The kiosk → MediaMTX, after a refusal.** This is the real amplifier, and it is measurable: `apps/shared/src/ui/composites/useWhepSession.ts:53-67` retries on `min(1000·2^attempt, 15_000) ms` with ±20% jitter, plus `DISCONNECT_GRACE_MS = 5_000`, and **never gives up** (#2355: no terminal state, no `error` status reached from an authorization refusal). Every retry is one more WHEP open and therefore one more authorize POST. The ladder is what makes the worst case finite and computable — see §*Sizing the ceiling*. It is also why the ceiling is deliberately generous: clipping this traffic converts a stream outage into a permanent **authorization** outage, with every tile reading "Reconnecting…" and no way out.

---

## Sizing the ceiling — the number, and where each factor comes from

The issue asserts *"MediaMTX's call rate is bounded and known, so the ceiling can be tight."* It is bounded. Here is the derivation, every factor cited.

**One authorize POST per admitted MediaMTX request.** `mediamtx.yml:44-49`: `authMethod: http` with only `api`, `metrics` and `pprof` excluded. So `read` and `playback` each cost one POST.

**Ingest costs nothing.** `MediaMtxRtspGateway.cs:32` registers paths as `{"source": "rtsp://…"}` — MediaMTX **pulls**, so it is the RTSP client and there is no incoming publish to authorize. (The handler refuses `publish` outright anyway, `:58-62`.) So all legitimate authorize traffic is viewer-session opens.

**Concurrent viewer sessions per fab ≤ ~100.** `GridDimensions.cs:18` — `public const int MaxTiles = 4`, and its own doc comment says *"the kiosk never decodes more than MaxTiles simultaneous"*. Constitution §NFR gives a wall of **20 kiosks** per fab. 20 × 4 = **80** kiosk tiles, plus the management console, which shows **one** camera at a time (`useWhepSession.ts:46-48`, spec 043) — say 20 operator desktops → **≈100 concurrent sessions**.

> **This corrects a figure it would have been easy to take from #2092's comment thread**, which says "a 250-tile wall". The 250 in constitution §NFR is *concurrent cameras per fab* — cameras ingested and health-tracked. It is **not** the number simultaneously decoded, which the layout aggregate caps at 4 per kiosk. Sizing off 250 tiles would have inflated the ceiling 2.5×.

**Worst sustained minute — a fab-wide outage, every session in the retry ladder.** Per tile from a cold ladder the attempt times are ≈ 0.8, 2.4, 5.6, 12, 24, 36, 48, 60 s → **≈8 attempts in the worst 60 seconds**; at the cap the floor is 15 000 × 0.8 = **12 s**, so the steady rate is one attempt per 12 s per tile.

| | figure |
|---|---|
| sustained | 100 ÷ 12 s ≈ **8.3 POST/s** |
| worst 60 s (cold ladder) | 100 × 8 ≈ **800 POST/min** |
| peak, first ~25 s of an outage | ≈ **20 POST/s** |
| wall-boot burst | ≈ **100** opens in a few seconds |

**Decision: `PermitLimit = 2000`, `Window = 00:01:00`, `QueueLimit = 0`, rejection `429`.**

- **2.5× the worst computed minute (800).** The margin is deliberate and covers the one factor not verified against a running MediaMTX: whether a WHEP open costs *one* authorize POST or two. At 2× it is still 1600 < 2000. Phase 5 measures the real count; if it exceeds two, the number moves in config, not in code.
- **Window of one minute, not ten seconds.** A fixed window forgives bursts in proportion to its length, and the burst here (a whole wall booting, a whole wall recovering) is the case that must not be clipped. A 10 s window sized to the same rate would clip a cold-ladder storm.
- **`QueueLimit = 0`** — mirrors `ApiGateway/Program.cs:42`. Nothing queues on a path a wall is waiting on; a refusal must be immediate.
- **Not "tight" in absolute terms; tight in the only terms that matter.** 2000/min is ~33 req/s per source IP against an endpoint that today accepts as many as a socket can deliver — roughly three orders of magnitude, and it bounds anonymous RSA work to a rate a single core does not notice.
- **Configurable**, mirroring the gateway's `RateLimiting` section shape, so the number is a deploy-time decision and not a recompile.

## Partition key: source IP, and explicitly **not** a global bucket

- **A global bucket would be strictly worse than today.** One anonymous caller could consume the whole window and **deny MediaMTX**, turning a CPU-burn finding into a trivially triggered fab-wide video outage. A control whose failure mode is the thing it protects is not a control.
- **Source IP is not spoofable here.** Verified: `ForwardedHeaders` / `UseForwardedHeaders` / `X-Forwarded` appear **nowhere** in `src` (zero hits). `HttpContext.Connection.RemoteIpAddress` is the real socket peer, so an `X-Forwarded-For` an attacker invents changes no partition. **This is a load-bearing precondition: if a later spec enables `UseForwardedHeaders` without `KnownProxies`, this limiter silently becomes unbounded.** US3 records it next to the mechanism.
- **MediaMTX's address need not be known or stable.** It gets its own partition automatically, whatever the deployment gives it. A configured allowlist of MediaMTX addresses would need deployment knowledge this repo does not have — there is no Helm chart at all (#1015, #2238, `deploy/helm/` holds one hand-written Mosquitto chart).
- **The gateway-proxied path collapses into one attacker-only partition.** MediaMTX calls the service directly, so the gateway's pod IP carries *no* legitimate authorize traffic. Everything arriving that way shares one bucket — which is the correct outcome, and it is what closes the rotating-`X-Fab` bypass in §4 above.
- **Mirrors an existing shape rather than inventing one:** `ApiGateway/Program.cs:77-78` already falls back to `ip:{RemoteIpAddress}` for unattributed traffic. Same key, same spelling, same reason.

## Does this need an ADR?

**Flagged deliberately, because it is the one boundary this spec touches, and ADR-0144 bars this lane from writing an ADR.** Assessment: **no new ADR required.** A reviewer who disagrees should block, and the lane must park rather than write one.

ADR-0106 says the gateway *"owns, once, at the edge: … Rate limiting"*. Four reasons this spec does not contradict it:

1. ADR-0106's own Consequences: *"Direct per-service access still exists for internal callers and tests; **the gateway is additive at the edge, not a hard chokepoint**."* A control that exists only at a non-chokepoint is not a control for a route reached off-edge **by design**.
2. ADR-0106 explicitly keeps per-service JWT validation *"(ADR-0007/0008, **defense in depth**)"*. The same reasoning: the edge policy is not removed or relocated, a per-service one is added beside it.
3. `/streams/authorize` is not an edge route. `mediamtx.yml:45` addresses the service by service DNS, which ADR-0106 itself classifies as internal traffic outside the gateway.
4. **Precedent, already merged:** `IngestWriteLimiter` throttles *inside* EventIngestion and answers its own `429` (`EventsEndpoints.Writes.cs:344-350`, declared at `EventsEndpoints.cs:53`), with no ADR of its own. A second in-service limiter is not a new class of thing.

## Latency-budget impact

**N/A to all six legs of constitution §IV.** `/streams/authorize` is on WHEP **session establishment** — not the media path (`Camera → SFU → kiosk`, which never touches HTTP here) and not the event path (`event → RabbitMQ → handlers → SignalR push`).

It *is* on spec 002 FR-013's separate budget, *click → first decoded frame ≤ 3 s p95*, and that has an existing instrument: `tests/Integration.Tests/StreamDistribution/WhepHandshakeLatencyTests.cs`. The admitted path gains one partition lookup and one counter increment — in-memory, no I/O, no allocation on the hot path — and the refused path fails immediately (`QueueLimit = 0`) rather than waiting. **US1 requires that suite to stay green**, which is the check, not the claim.

---

## User Scenarios & Testing

### User Story 1 — the hook stops answering forever (Priority: P1) — **RED**

An operator of a fab can no longer be made to pay unbounded CPU by an unauthenticated caller with nothing but network reach to the StreamDistribution port. Beyond a configured number of authorize attempts per minute from one source address, the hook answers `429` and does no cryptographic or database work at all. MediaMTX's own traffic, and every legitimate viewer behind it, is unaffected.

**Why P1:** it is the entire issue, and it is independently shippable — one policy registration, one chain call, one config section. Nothing in US2 or US3 is needed for it to work or to be observed.

**Independent Test:** boot the Aspire stack; post to `/streams/authorize` past the configured ceiling and observe `429`; confirm a request under the ceiling is still answered on its merits.

**Acceptance Scenarios:**

1. **Happy path (the control is absent from normal operation).** **Given** the stack is running and no caller has approached the ceiling, **When** MediaMTX's hook posts a valid bearer with `action: read` for a live path, **Then** the answer is `200` and no request is throttled.
2. **The ceiling (the red test).** **Given** a source address that has already made `PermitLimit` requests to `/streams/authorize` inside the window, **When** it makes one more, **Then** the answer is `429`, **and** the handler is never entered — no token is parsed, no signature verified, no stream read.
3. **Isolation — a throttled attacker must not darken the wall.** **Given** one source address has exhausted its window, **When** a *different* source address posts a valid authorize request, **Then** it is answered on its merits and **not** `429`. (This is the scenario that proves the key is per-IP and not a global bucket; it is the one that would fail on the design this spec rejects.)
4. **Conflict / self-inflicted-outage guard.** **Given** a fab-wide stream outage with every session in the retry ladder — the worst computed rate of ≈800 requests/minute from the MediaMTX partition — **When** that traffic runs for a full window, **Then** nothing is throttled, because the ceiling is 2000.
5. **Bad request, unchanged.** **Given** a caller under the ceiling, **When** it posts a malformed path or an unknown action, **Then** it still receives exactly the `403` and the code it receives today — `WHEP_INVALID_PATH`, `WHEP_ACTION_UNKNOWN`, `WHEP_ACTION_NOT_PERMITTED` — with no change of status, code, or detail.
6. **Auth, unchanged.** **Given** a caller under the ceiling, **When** it posts no token, a malformed token, a valid token without `sse.streams.read`, or a valid token while the realm is unreachable, **Then** it receives exactly today's `401 WHEP_UNAUTHORIZED` / `403 WHEP_FORBIDDEN` / `401 WHEP_IDENTITY_PROVIDER_UNAVAILABLE`. **The limiter adds a status; it removes and re-codes none.**
7. **The other three routes are untouched.** **Given** the ceiling is exhausted for a source address, **When** that address calls `GET /streams/{id}`, `GET /streams/` or `POST /streams/kiosk-latency`, **Then** those are answered normally: the policy is attached to the one mapping, not to the group. (Mirrors ADR-0106's reason for naming the policy per route rather than globally — `ApiGateway/Program.cs:27-28`: *"so the gateway's own health endpoints and the k8s liveness and readiness probes are never throttled."* `MapDefaultEndpoints` health routes must stay unthrottled here for the same reason.)
8. **The route's contract says so.** **Given** the generated OpenAPI document, **When** the authorize operation is read, **Then** it declares `429` alongside its `200`/`401`/`403`.
9. **The record stops naming an absent control.** **Given** `WhepAuthValidator.cs`, **When** the two comments at `:142-143` and `:210-212` are read, **Then** they describe the ceiling that now exists, and the mitigations they justify are re-stated against it rather than against its absence.

---

### User Story 2 — a control that fires is a control you can see (Priority: P2) — **RED**

When the limiter refuses a request, one structured record says which partition was refused and how many times, so an operator whose wall has gone dark can tell "the ceiling is too low" from "Keycloak is down" — today's two indistinguishable symptoms.

**Why P2:** the largest *availability* risk this spec introduces is a false positive, and a silent false positive is undiagnosable. Not gold-plating: constitution §VII, and the exact failure mode spec 119 already wrote a transition-only logger for on this same path.

**Independent Test:** exhaust the window; read the log for one record naming the partition; keep exhausting and confirm the sink is not flooded.

**Acceptance Scenarios:**

1. **Given** a source address crossing the ceiling, **When** it is refused, **Then** one record is emitted at `Warning` naming the partition key and the configured limit, through a `[LoggerMessage]` source-generated method (ADR-0050).
2. **Given** a sustained flood from one address — thousands of refusals in a window — **When** the log is read, **Then** the single OTLP sink is not flooded: the record is emitted on the **transition into** throttling for a partition, not per refused request. **This is the same discipline, for the same reason, as `WhepAuthValidator.cs:142-151`'s `Interlocked.Exchange` transition log** (ADR-0118: one sink, and an operator needs it readable at exactly the moment it would otherwise drown).
3. **Given** no request is ever refused, **Then** nothing is logged. A quiet control is silent.

---

### User Story 3 — the register stays true (Priority: P3) — **RED**

`StatusProducerDeclarationTests`' census is the repo's written answer to *"what in this tree can put a status on a response that the mapping line does not name?"*. It gains an entry for this limiter, so the next person sweeping for throttles finds two, not one.

**Why P3:** a register nobody updates is how §IV's leg table and CLAUDE.md's Phase-3 gate both drifted. Its own doc comment (`:150-155`) says the quiet part: *"A green run proves the five known handlers still exist… it cannot notice a mechanism nobody wrote down. **The rate limiter below was found by sweeping for `RateLimiter`, not by any test.**"* Adding a second limiter without adding a row makes that sentence retroactively false.

**Independent Test:** run the census test before the row is added and observe it fail; add the row; observe green.

**Acceptance Scenarios:**

1. **Given** the census at `StatusProducerDeclarationTests.cs:158-183`, **When** the StreamDistribution limiter exists, **Then** the census carries `M15` for it with its status (`429`) and its visibility, and the test that asserts the census covers this tree's throttles fails without it.
2. **Given** the per-IP partition, **When** the census row is read, **Then** it records the `ForwardedHeaders` precondition from §*Partition key* — that the key's integrity depends on nothing in `src` configuring forwarded headers.

### Edge Cases

- **A 429 that MediaMTX handles in an unobserved way.** Stated assumption, not a claim; phase 5 observes it. #2160 records that the same gap exists for `5xx` today.
- **The limiter's own burst pathology.** A fixed window admits up to 2×`PermitLimit` across a boundary (4000 in the worst two adjacent seconds). Accepted: that is still bounded, and it is the reason the ceiling is not sized to the millisecond.
- **A shared window inside the integration suite.** `WhepAuthIntegrationTests` already sends 5+ requests to this route from the test host's single address. A test that exhausts the window poisons its neighbours for the remainder of it — which the gateway's own rate-limit test avoids only by partitioning on a header it can vary, and this one cannot vary its IP. Resolved in plan.md §*Test-mode ceiling*; it is a design constraint, not an afterthought.
- **A production default that no test drives at rate.** Consequence of the above, stated rather than hidden: the shipped `2000/min` is asserted as a configured value, not exercised by sending 2001 requests.
- **`MapDefaultEndpoints` health and readiness routes.** Must remain unthrottled. `UseRateLimiter` in the pipeline throttles nothing without a policy attached, and the policy is attached to one mapping — but this is asserted, not assumed.

## Requirements

### Functional Requirements

- **FR-001**: `POST /streams/authorize` MUST refuse with `429` once a single source address exceeds a configured number of requests within a configured fixed window.
- **FR-002**: The refusal MUST occur before the endpoint handler runs — no token parse, no signature verification, no repository read.
- **FR-003**: Requests MUST be partitioned by the connection's remote address. There MUST NOT be a single global bucket, because an anonymous caller must never be able to exhaust MediaMTX's budget.
- **FR-004**: The limit and the window MUST come from configuration, with the production default `PermitLimit = 2000` over `Window = 00:01:00` and `QueueLimit = 0`.
- **FR-005**: No other route in StreamDistribution — including the health and readiness endpoints from `MapDefaultEndpoints` — may be throttled by this policy.
- **FR-006**: Every existing status, error code and detail this endpoint produces MUST be unchanged for a request under the ceiling.
- **FR-007**: The authorize mapping MUST declare `429` on its chain so the generated OpenAPI names it.
- **FR-008**: The two comments in `WhepAuthValidator.cs` (`:142-143`, `:210-212`) MUST be corrected to describe the ceiling that now exists.
- **FR-009**: A partition entering the throttled state MUST emit one structured `Warning` naming the partition and the configured limit; repeats within that state MUST NOT be logged (US2).
- **FR-010**: `StatusProducerDeclarationTests`' census MUST carry an entry for this limiter, and a test MUST fail if it does not (US3).

### Non-Functional Requirements

- **NFR-001**: No measurable change to the admitted path's latency; `WhepHandshakeLatencyTests` stays green.
- **NFR-002**: No new package reference. ASP.NET Core rate limiting is in the shared framework — `src/ApiGateway/SmartSentinelEye.ApiGateway.csproj` references `Yarp.ReverseProxy` and service discovery only, and uses `Microsoft.AspNetCore.RateLimiting` with no `PackageReference`.

### Key Entities

None. A rate limit is not domain state: nothing enters a domain model, no value object is introduced, no aggregate changes. (Same reasoning as `RecordKioskLatency`'s comment at `StreamEndpoints.cs:87-89` — *"a latency figure is telemetry, not domain state"*.)

## Success Criteria

- **SC-001**: An unauthenticated caller from one address cannot cause more than `PermitLimit` token-validation attempts per window — verified by an integration test that observes `429` and that the throttled requests produce no authorize log record.
- **SC-002**: A second address is unaffected by the first's exhaustion — verified in the same test, and the scenario that distinguishes this design from a global bucket.
- **SC-003**: The seven existing `WhepAuthIntegrationTests` cases pass unmodified. An assertion that has to be edited is evidence behaviour moved beyond the added status.
- **SC-004**: `WhepHandshakeLatencyTests` stays green (NFR-001).
- **SC-005**: Phase 5 records the **observed** number of authorize POSTs MediaMTX makes per WHEP open, against the assumed 1. If it exceeds 2, FR-004's default is revisited before merge.
- **SC-006**: `security-reviewer` returns no unresolved finding on the partition key, the ceiling, or the unthrottled surface.

## Assumptions

1. **One authorize POST per WHEP open.** Derived from `mediamtx.yml:44-49`, not observed against a running MediaMTX. The 2.5× margin absorbs 2×; SC-005 measures it.
2. **≈100 concurrent viewer sessions per fab.** `GridDimensions.MaxTiles = 4` × 20 kiosks (constitution §NFR) + ~20 single-camera consoles. The kiosk count is the softer half; the 2.5× margin covers 250 sessions at the sustained rate.
3. **MediaMTX treats `429` as a deny and does not retry the hook.** Not documented in this repo; #2160 records the same gap for `5xx`. Phase 5 observes it.
4. **`RemoteIpAddress` is the socket peer.** Verified today (no forwarded-headers configuration in `src`); recorded in the census (US3) because it is a precondition a future spec could silently remove.
5. **Whether an equivalent proxy sits in front of `stream-distribution` in production is unverified, and unverifiable until a production deployment exists.** Confirmed during T004's rework: in **run mode**, `stream-distribution`'s HTTP endpoint is reached through Aspire's DCP proxy (`WithHttpEndpoint()`, default-proxied), which terminates every inbound connection and re-originates it from its own loopback socket — so `RemoteIpAddress` as Kestrel sees it is genuinely the direct socket peer (assumption 4 still holds), but in this dev/test topology that peer is the *proxy*, not MediaMTX or a test client. This is what makes the two "sources" `WhepAuthorizeRateLimitTests` originally tried to construct indistinguishable at the layer the limiter partitions on (`WhepAuthorizeRateLimitTests.cs`, the `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket` remarks). This repository has no production deployment — the Aspire k8s publisher has never been run and no k8s package is referenced (CLAUDE.md §What lives where; `specs/047-the-decisions-we-made/audit.md:373`, issue #1015) — so there is currently no way to observe whether a k3s deployment would put an equivalent proxy (an ingress, a service-mesh sidecar) in front of `stream-distribution`, or whether MediaMTX and StreamDistribution would communicate pod-to-pod without one. **Neither is assumed here.** Should a production deployment materialise, verifying partition exactness there — that `RemoteIpAddress` is genuinely MediaMTX's own address rather than a shared proxy's — is a separate, later concern, not this spec's to resolve (mirrors spec 206 / #2429's handling of the same "no production deployment exists yet" gap for a different question, `specs/206-a-row-no-timeline-can-reach/spec.md:269`).

## Out of scope

- **Collapsing the 401/403 oracle** — §*Scope decision: the oracle*. Not anonymously exploitable; the exploitable half is #2092's.
- **#2238's plaintext discovery/JWKS transport.** No file overlaps: #2238 is `HttpDocumentRetriever { RequireHttps = false }` at `WhepAuthValidator.cs:83` plus `AuthenticationDefaults.cs:70` plus the realm's `sslRequired`, and it is blocked on a deployment story that does not exist (#1015). This spec touches none of it. It does *marginally* reduce #2238's exploitability — an on-path attacker can force the on-demand JWKS re-fetch fewer times per minute — but the 5-minute `RefreshInterval` floor already dominates that, so the reduction is real and small. **Not a mitigation, and must not be reported as one.**
- **#2092's missing fab check**, and #2355's unbounded retry of an authorization refusal. Both are named here because they set this spec's worst-case rate; neither is changed by it.
- **Widening the gateway's `per-fab` policy** or fixing its attacker-chosen partition key. A separate surface with its own blast radius; this spec's service-level ceiling covers the proxied path for *this* route regardless.
- **A census entry for `IngestWriteLimiter`.** Found while reading the census for US3: `EventsEndpoints.Writes.cs:344-350` answers `429` from a handler body and **no census row covers it** — M14 names only the gateway limiter. A genuine pre-existing gap in the register, and deliberately not fixed here: it is EventIngestion's file, unrelated to this issue, and folding it in would make this diff a two-context change. **Reported for filing.**
