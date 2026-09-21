# Plan 208 — A ceiling the hook never had

**Spec:** `specs/208-a-ceiling-the-hook-never-had/spec.md`
**Issue:** #2284
**Branch:** `2284-stream-authorize-rate-limit` (worktree `D:/Github/sse-2284`, cut at `origin/develop` `1788d773`)

**Phase-4a colour: RED for all three stories** (ADR-0139, constitution §Testing). Stated here as well as in the spec because it is the gate the engineer is held to, not a note:

> `POST /streams/authorize` answers `429` where today it answers `200`/`401`/`403` forever. **The test for US1 acceptance scenario 2 must be written first, run, and observed failing — and its verbatim output is what the PR body quotes.** A test for the ceiling that arrives green means either the limiter was written first or the test does not exercise the ceiling; both are phase-4 failures, not shortcuts. There is no characterisation half to this spec: nothing here is behaviour-preserving.

**Phase 6: `security-reviewer`, mandatory, regardless of diff size.** Reason recorded in the spec: this is the only control that will exist on a credential-free endpoint gating every wall's video. Specifically ask it to attack (a) the partition key, (b) the ceiling, (c) the unthrottled surface, and (d) whether the limiter can be made to deny MediaMTX.

---

## Bounded context and layers

**One context: `StreamDistribution`. One layer: `Api`.** Plus one AppHost wiring line and one architecture-test register entry.

| Layer | Touched? | Why |
|---|---|---|
| `StreamDistribution/Domain` | **no** | A rate limit is not domain state. No aggregate, no value object, no invariant. Nothing to model — the same reasoning `StreamEndpoints.cs:87-89` gives for a latency figure. |
| `StreamDistribution/Application` | **no** | `AuthorizeWhepCommand`, its handler and `AuthorizeWhepError` are **unchanged**. A `429` is produced by middleware that runs *before* the handler; it never becomes an `ApiError` (ADR-0047/0089), and Application stays ASP.NET-free (ADR-0051). **If a task ends up editing `AuthorizeWhepCommandHandler`, the design has been misread.** |
| `StreamDistribution/Infrastructure` | **comments only** | `WhepAuthValidator.cs:142-143` and `:210-212` name an absent control (FR-008). Prose, no behaviour — and the covering tests (`WhepValidatorRefreshRestraintTests`, `WhepValidatorUnreachableRealmTests`) must stay green untouched. |
| `StreamDistribution/Api` | **yes** | The policy registration (`Program.cs`), the chain attachment and the `429` declaration (`StreamEndpoints.cs`), the defaults (`appsettings.json`). |
| `AppHost` | **yes, one gated block** | The integration-lane ceiling override. §*Test-mode ceiling*. |
| `Architecture.Tests` | **yes** | Census `M15` (US3). |

**No boundary is crossed.** No cross-context project reference is added, no `Shared.Contracts` change, no new message. `NetArchTest`'s `BoundaryTests` should be unaffected; if they are not, something outside this design was touched.

**No new package.** `Microsoft.AspNetCore.RateLimiting` and `System.Threading.RateLimiting` are in the ASP.NET Core shared framework — verified: `src/ApiGateway/SmartSentinelEye.ApiGateway.csproj` uses both with no `PackageReference` for either. `Directory.Packages.props` is not touched.

## Entities / value objects / invariants

**None.** Deliberate, and the one thing to resist: the ceiling and the window are *configuration*, and constitution §II's primitive ban scopes to **domain models**. Wrapping `PermitLimit` in a value object would put a domain type in an options class that binds from JSON, which is precisely the boundary §II exempts (the same reasoning that exempts `Shared.Contracts`). `WhepAuthOptions` (`Infrastructure/Auth/WhepAuthOptions.cs`) is the existing precedent in this very context: `public string Authority { get; set; } = string.Empty;`.

## Messaging

**None.** No domain event, no integration event, no Wolverine handler, no outbox row. A throttled request is not a business fact; it is a refusal at the transport edge. `Shared.Contracts` is untouched, so `V1ResourceMap`'s corpus (and `BoundaryTests`' audit-coverage rule) is unaffected.

---

## The design

### 1. Where the policy is registered — `src/StreamDistribution/Api/Program.cs`

Mirrors `src/ApiGateway/Program.cs:29-44` and `:62` line for line, including the comment discipline.

- The limits are read from configuration and the policy is registered on `builder.Services`.
- `app.UseRateLimiter()` goes into the pipeline **after** `UseAuthentication`/`UseAuthorization` is irrelevant to correctness here (the route is anonymous) but matters for cost: placing it **before** them means a throttled request does no authentication work either. Place it immediately after `app.UseExceptionHandler()` and before `UseAuthentication()`, i.e. as early as possible after the exception handler. `MapDefaultEndpoints` is mapped above and carries no policy, so health and readiness stay unthrottled (FR-005).
- **Not** in `StreamDistributionApiModule.AddStreamDistributionApi` — that method takes only `IServiceCollection` (`:17`) and cannot read configuration. Widening its signature to reach `IConfiguration` would change an ADR-0051 shape shared by nine contexts for one context's need. `Program.cs` is where the gateway does the same thing; mirror it (CLAUDE.md: read before write, mirror existing patterns).

### 2. The policy — fixed window, partitioned on the remote address

```
policy name:   "whep-authorize"   (named, attached per route — never a global limiter)
partition key: $"ip:{context.Connection.RemoteIpAddress}"   (null → "unknown", as the gateway does)
limiter:       RateLimitPartition.GetFixedWindowLimiter
options:       PermitLimit = config, Window = config, QueueLimit = 0
rejection:     options.RejectionStatusCode = StatusCodes.Status429TooManyRequests
```

The spelling of the key deliberately matches `ApiGateway/Program.cs:77-78` (`ip:{RemoteIpAddress}`) so the two limiters partition the same way and a reader recognises it.

**Rejected alternatives, and why — this is the part a reviewer should push on:**

| Alternative | Rejected because |
|---|---|
| **One global bucket** for the route | An anonymous caller could exhaust the whole window and **deny MediaMTX**, converting a CPU-burn finding into a trivially triggered fab-wide video outage. A control whose failure mode is the outage it prevents is not a control. This is the single most important decision in the spec. |
| **Allowlist MediaMTX's address**, throttle everything else hard | Needs deployment knowledge this repo does not have: there is no Helm chart (#1015, #2238 — `deploy/helm/` holds one hand-written Mosquitto chart), and in run mode MediaMTX's calls arrive via the Aspire DCP proxy (see §*What the two lanes actually prove*), so the address is not even stable in dev. |
| **Partition on `X-Fab`**, like the gateway | The caller invents the header. On an anonymous route there is no authenticated principal behind the trust assumption, so it is unbounded in aggregate — this is exactly the bypass spec §4 documents on the gateway's own policy. |
| **Concurrency limiter**, like `IngestWriteLimiter` | Bounds simultaneity, not rate. A serial attacker at one request at a time would never be limited, and serial is all it takes to burn a core on RSA verifies. The issue asks for a fixed window; a fixed window is the right instrument for a *rate* finding. |
| **Widen the gateway's policy to cover it** | Only covers the proxied path; MediaMTX's own path (`mediamtx.yml:45`) bypasses the gateway by design, and so would an attacker with service reach — which the issue names as the only prerequisite. |
| **Sliding window / token bucket** | More faithful to a burst, and the issue asks for fixed window. No reason to deviate; `QueueLimit = 0` plus a one-minute window already forgives the wall-boot burst. |

### 3. Configuration

`src/StreamDistribution/Api/appsettings.json` gains, mirroring the gateway's section shape (`ApiGateway/appsettings.json:20-24`):

```
"WhepAuthorizeRateLimiting": { "PermitLimit": 2000, "Window": "00:01:00" }
```

Bound with `GetValue<int?>(...) ?? 2000` / `GetValue<TimeSpan?>(...) ?? TimeSpan.FromMinutes(1)` — the gateway's exact idiom (`Program.cs:30-31`), so a missing section is a working default rather than a startup failure. The derivation of `2000` is in spec §*Sizing the ceiling*; it is 2.5× the worst computed minute of a fab-wide retry storm (≈800), and survives a 2× surprise in the per-open POST count.

### 4. The chain — `src/StreamDistribution/Api/StreamEndpoints.cs:63-80`

Two additions to the authorize mapping and nothing else:

- `.RequireRateLimiting("whep-authorize")`
- `.ProducesProblem(StatusCodes.Status429TooManyRequests)` (FR-007) — matching `EventsEndpoints.cs:53`, which declares the same status for `IngestWriteLimiter` with a comment saying what is being bounded. Do the same here.

The other three mappings in the group are untouched. **The policy is attached to the mapping, never to the `MapGroup`** — `ApiGateway/Program.cs:27-28` states the reason this repo already holds: a named policy attached per route is how health and probe endpoints stay unthrottled.

### 5. US2 — rejection visibility

`options.OnRejected` on the limiter, emitting through a `[LoggerMessage]` source-generated method (ADR-0050) in the Api project's existing logger partial-class pattern.

**Transition-only, not per-request** (FR-009, US2 scenario 2). Copy the discipline from `WhepAuthValidator.cs:142-151` verbatim in spirit: an `Interlocked`-guarded flag per partition, so a sustained flood produces one record, not one per refused request. ADR-0118 gives one sink; a control that floods it at the moment an operator needs it readable repeats the exact defect spec 119 fixed on this same path.

The record carries the partition key and the configured limit as structured fields. It must **not** carry the bearer token or any part of the request body.

> **Scope guard.** The `OnRejected` callback is the only place US2 touches. It must not reach into the handler, and it must not add a metric — ASP.NET Core's rate limiter already emits `aspnetcore.rate_limiting.*` through the meter ADR-0118's sink collects. Adding a second instrument would be speculative generality.

### 6. US3 — the census

`tests/Architecture.Tests/StatusProducerDeclarationTests.cs`, the `Census` array at `:158-183`, gains:

```
new("M15", "StreamDistribution /streams/authorize fixed-window rate limiter", "429", Visibility.Chain, …)
```

`Visibility.Chain` and not `Visibility.None` (M14's value), because unlike the gateway's this one **is** declared on the mapping chain (FR-007) — and the census's job is to record which side of that line each mechanism sits on. The row's comment records the `ForwardedHeaders` precondition from spec §*Partition key*: the key's integrity depends on nothing in `src` configuring forwarded headers, and a future spec that enables `UseForwardedHeaders` without `KnownProxies` silently unbounds this limiter.

The census's typed-in counts (`RouteHandlerMappingCount = 60`, `EndpointFileCount = 13`) are **unchanged** — no route is added and no endpoint file is created. If either count has to move, something outside this design was touched.

---

## Test-mode ceiling — the one piece of test design that must be decided up front

**The problem, stated plainly:** the partition key is the source address, and a test cannot vary its own address. The existing `WhepAuthIntegrationTests` (7 cases) and `WhepHandshakeLatencyTests` all POST to `/streams/authorize` from the same test host. A test that exhausts a 2000/min window poisons every neighbour for the rest of that minute — and xUnit gives no ordering guarantee inside `AspireCollection`. The gateway's own rate-limit test dodges this only because it partitions on a header it can vary (`GatewayRateLimitIntegrationTests.cs:42`). This one cannot.

**Decision: the AppHost lowers the ceiling for the integration lane only.**

```
if (isE2ETests)  // AppHost.cs:17 — set by AspireFixture (AspireFixture.cs:281), and by
                 // AppHostE2ESwitchTests; NOT by ci.yml's plain `dotnet run` e2e boot
{
    streamDistribution
        .WithEnvironment("WhepAuthorizeRateLimiting__PermitLimit", "20")
        .WithEnvironment("WhepAuthorizeRateLimiting__Window", "00:00:10");
}
```

- `isE2ETests` is the correct gate and **not** `isRunMode`: the flag is set by the integration fixture and by `AppHostE2ESwitchTests`, and *not* by the end-to-end stack boot, which is a plain `dotnet run`. That asymmetry is documented at `AppHost.cs:211-216` and is exactly what is wanted here — see §*What the two lanes actually prove*.
- A 10-second window means the poisoning self-heals inside the fixture's other waits, and the test can **wait for the condition** (a request admitted again) rather than for a count or a fixed sleep — constitution §Testing.
- `AppHostE2ESwitchTests`' six cases assert resource presence and volumes, not environment variables, so this block should not disturb them. Verify, do not assume.

**The residual, stated rather than hidden:** the shipped `2000/min` default is never exercised at rate by any test. It is asserted as a configured value (a cheap unit test reading `appsettings.json`, so a fat-fingered edit to `200` fails the build), and its *derivation* is the spec. This is the honest trade for a limiter keyed on something a test cannot vary; the alternative — 2001 real requests inside a shared fixture — buys a worse flake for no additional proof.

## What the two lanes actually prove — and a dev-only caveat worth writing down

In **publish mode** (production) `stream-distribution` is containerized and MediaMTX uses the service DNS baked into `mediamtx.yml:45`, so the address StreamDistribution sees for MediaMTX's calls is MediaMTX's own pod address: the per-IP partition is exact.

In **run mode** `AppHost.cs:404-420` overrides `MTX_AUTHHTTPADDRESS` to the Aspire-resolved endpoint, because the host-process service is not reachable by container DNS. MediaMTX's calls therefore arrive **through the DCP proxy**, so in dev a developer's `curl` and MediaMTX's hook can land in the same partition. Harmless for the control's purpose, but it means:

- the **Playwright e2e lane** (plain `dotnet run`, production ceiling) proves the ceiling does not clip a real wall opening real tiles;
- the **integration lane** (small ceiling) proves the `429`, the handler bypass, and the per-partition isolation;
- **neither** proves partition exactness in production, which needs a cluster (#1015).

Write this down rather than discovering it during phase 5.

## Red-first, concretely

The engineer receives `test-writer`'s verbatim output as its brief and **may not edit the tests to pass** (ADR-0144, phase 4a/4b split). What red looks like here:

| Story | The test | Observed failure before the change |
|---|---|---|
| US1 | `Authorize_refuses_a_source_over_its_window_with_429` (integration, `AspireCollection`) | The 21st request returns `401`/`200`, not `429`. Shouldly's diff is the quoted evidence. |
| US1 | `Authorize_from_a_second_source_is_unaffected_by_an_exhausted_window` | Cannot be red before the limiter (nothing is ever throttled) — it is a **guard against the rejected global-bucket design**, and it passes trivially today. Say so in the PR rather than presenting it as red evidence. |
| US1 | `A_throttled_authorize_never_reaches_the_handler` | Red: today every request reaches the handler. Asserted by the absence of the handler's own log record for the throttled attempts, not by a mock. |
| US2 | `A_partition_entering_the_throttled_state_is_logged_once` | Red: no such record exists. |
| US3 | the census assertion | Red: the census has no entry for a second limiter. |

And what must stay green, unmodified, throughout: the seven `WhepAuthIntegrationTests` cases, `WhepHandshakeLatencyTests`, `WhepValidatorRefreshRestraintTests`, `WhepValidatorUnreachableRealmTests`, `BoundaryTests`, `PrimitiveBoundaryTests`, `HandlerDeconstructionTests`. An assertion in any of those that has to be edited is evidence behaviour moved beyond the added status — **block, do not adjust** (constitution §Testing).

## Phase 5 — what must be observed, not merely tested

1. **The ceiling fires on the running stack.** Exhaust a window against `/streams/authorize` and read the `429` and the single log record.
2. **A real wall is not clipped.** Boot the e2e stack at the production ceiling, open a layout, and confirm every tile goes live with nothing throttled.
3. **SC-005 — the measurement this spec's number rests on.** Count the authorize POSTs MediaMTX makes for **one** WHEP open (the assumed 1). Read it off the request count in the logs/traces while opening a single tile. If it exceeds 2, revisit FR-004's default before merge.
4. **What MediaMTX does with a `429`** (spec assumption 3, #2160's territory for `5xx`). Observe whether it denies once or retries.
5. **Latency unchanged** — `WhepHandshakeLatencyTests`' figure, cited. NFR-001.

Per this repo's standing lesson, write each observed figure **down** in `verification.md`. A measurement reported only to the orchestrator is invisible to every later grep and reviewer.

## Risks

| Risk | Mitigation |
|---|---|
| **The ceiling clips a real wall** — the worst outcome, since it turns a stream outage into a permanent authorization outage with #2355's unbounded retry behind it. | 2.5× margin over the computed worst minute; `QueueLimit = 0`; US2's log so it is diagnosable in one line; phase-5 observation 2. |
| A global-bucket regression in a later edit | US1 scenario 3 is the standing test that fails on it. |
| `UseForwardedHeaders` enabled later without `KnownProxies` → the key becomes attacker-chosen | Recorded in the census row (US3) next to the mechanism, which is where someone sweeping for throttles will read it. |
| `UseRateLimiter` placed after the endpoint mapping, or the policy attached to the group | US1 scenario 7 (other routes unthrottled) and the `MapDefaultEndpoints` assertion. |
| A reviewer reads this as contradicting ADR-0106 | Argued in spec §*Does this need an ADR?* with four citations, one of them a merged precedent (`IngestWriteLimiter`). If the reviewer still disagrees, **park the issue** — this lane may not write the ADR. |

## Not in this plan

Everything in spec §*Out of scope*, and in particular: no change to `AuthorizeWhepCommandHandler`, no change to `AuthorizeWhepError`, no change to `WhepAuthValidator`'s behaviour (comments only), no change to the gateway, no `deploy/` change, no `Directory.Packages.props` change, and no census row for `IngestWriteLimiter` (reported for separate filing).
