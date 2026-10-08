# Spec 315 — The create that went first

**Issue:** [#2598](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2598)
— *RegisteredClientConcurrencyIntegrationTests' first CreateAsync can exceed the fixture's 10 s
timeout when run in isolation*. Label `agent:ready`; Project #13, status **In Progress**
(verified 2026-10-08).
**Branch:** `test/2598-registeredclient-create-timeout` (cut from `develop` @ `db92e3be`)
**Created:** 2026-10-08 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0143 (retry only idempotent methods — preserved, not touched), 0142 (a retried create
must not apply twice — the reason the POST stays single-attempt), 0103 (integration via the Aspire
fixture), 0139 §Testing (behaviour-preserving changes are characterised), 0036 (smallest change;
no speculative mechanism), 0144 (lane).
**New ADR:** no. Neither retry policy nor any timeout value changes.
**Latency budget (§IV):** N/A — test harness only; Identity is not on the event→overlay path.

**Spec number.** `develop` tops out at **314**; no remote branch and no open PR claims 315
(checked 2026-10-08). Re-check before opening the PR.

## 1. The premise, re-checked on this tree

| Claim in the issue | On this tree |
|---|---|
| 10 s per-attempt timeout | **Confirmed, and it is the library default, not a fixture setting.** `FixtureHttpClients.Configure` (`tests/Integration.Tests/Fixtures/FixtureHttpClients.cs:28`) calls `AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)` and configures nothing else, so the standard handler's `AttemptTimeout` 10 s / `TotalRequestTimeout` 30 s apply. The same values are what production clients get (`tests/ServiceDefaults.Tests/ResilienceHandlerNestingTests.cs:71-72`). |
| The POST is not retried | **Confirmed.** `RetryIdempotentMethodsOnly` (ADR-0143). One slow attempt is fatal. |
| Every fact shares `CreateAsync` | Five of eight facts call `CreateAsync` (lines 87, 103, 116, 161, 280). |
| In a full-class run an earlier fact warms the path | Plausible. The class's `InitializeAsync` (lines 49-54) waits only for `identity` to reach `Running`; nothing in the class or the fixture sends Identity a request first. |

### 1.1 What "cold" can mean for this create — and what the fixture does not wait for

The fixture gates Identity on `KnownResourceStates.Running` only (`AspireFixture.cs:456-458`).
Its own comment at lines 463-467 says why that is not enough: *Running only means the process
launched — it does not mean Kestrel has bound its listener* — which is why `overlay-designer`
alone gets `WaitForServiceHealthAsync`. Identity does not. Run in isolation, a fact's
`CreateAsync` is **the first request Identity ever serves**, so it pays every first-time cost on
its path:

| Leg of the first create | Warmed by an authenticated `GET /webhook-integrations`? |
|---|---|
| Identity's listener / Aspire proxy readiness | yes |
| JWT bearer: first OIDC metadata + JWKS fetch from Keycloak | yes (same scheme) |
| Authorization policy `sse.webhooks.write` + fab guard | yes (same group policy, `WebhookRotationEndpoints.cs:37`) |
| EF model build, first Npgsql connection, first `RegisteredClient` query | yes |
| Keycloak admin token + `CreateClientAsync` (Keycloak's admin REST, its own DB write) | **no** |
| EF insert + Wolverine outbox commit | **no** |

The warm-up below covers the first four rows. The last two remain cold by design — see §4.

### 1.2 The issue's own caveat stands

One observation, on a machine running a second Aspire stack. Reproduction on a quiet machine is
task **T001** and its outcome is recorded in §6 before the change lands.

## 2. User story

**US1 (P1) — A single fact of the class can be run on its own.** As an engineer running one fact
of `RegisteredClientConcurrencyIntegrationTests` with `--filter` (a counterfactual, a targeted CI
rerun), I want the fact to reach its own assertions, so that its result says something about the
behaviour it tests rather than about whether Identity had served a request yet.

### Acceptance scenarios

```gherkin
Scenario: an isolated fact reaches its assertions on a freshly booted stack
  Given a freshly booted Aspire fixture on which Identity has served no request
  When one fact of RegisteredClientConcurrencyIntegrationTests is run alone
  Then the class's setup sends Identity an authenticated GET /webhook-integrations?fabId=munich first
  And that GET may be retried by the fixture's resilience handler (GET is idempotent, ADR-0143)
  And the fact's first CreateAsync is sent to an Identity that has already authenticated,
      authorised and queried for a request

Scenario: the create stays single-attempt (the conflict ADR-0142/0143 exist for)
  Given the change is applied
  When CreateAsync's POST exceeds the 10 s attempt timeout
  Then it is not retried and the fact fails in setup with TimeoutRejectedException, as today
  And no timeout value, retry predicate or resilience option has changed anywhere

Scenario: setup fails loudly when Identity cannot serve (bad request / unavailable)
  Given Identity does not answer the warm-up GET successfully within the handler's 30 s total budget
  When the class's InitializeAsync runs
  Then InitializeAsync throws (EnsureSuccessStatusCode or TimeoutRejectedException)
  And the failure names the warm-up, not a fact's assertion

Scenario: the warm-up uses the admin principal the facts use (auth)
  Given the warm-up GET requires sse.webhooks.write, the same scope as the rotation
  When it is sent with the client from aspire.CreateAdminClientAsync("identity")
  Then it is answered 200; a 401/403 there means the facts could not have passed either
```

### Independent end-to-end test procedure

1. One Aspire stack on the machine (memory: *one machine, one Aspire stack*); stop any other.
2. `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests.A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down"` — three times, each a fresh fixture boot.
3. Repeat step 2 for `A_created_client_is_listed_with_the_version_its_next_rotation_needs` (the
   cheapest `CreateAsync`-first fact) once.
4. `dotnet test ... --filter "FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests"` — the
   whole class, 8/8.

## 3. Decision — option 1, warming with the class's existing GET

**Change:** in the class's `InitializeAsync`, after the existing `Running` wait, create the admin
client and call the existing `ListWebhooksAsync` helper once. One file, ~3 lines plus a *why*
comment. No new helper, no fixture change, no resilience change.

### Why not option 2 (a longer per-attempt timeout for this class's creates)

1. **It cannot be scoped to this class without new machinery.** Every client the fixture hands
   out shares one unnamed `IHttpClientFactory` client and so one resilience pipeline
   (`AspireFixture.cs:273-292`, which records that even a separately named client was not enough
   isolation). The ways to give only these creates more time are: raise `AttemptTimeout`
   fixture-wide (every integration test, and it is coupled to `TotalRequestTimeout` 30 s and the
   circuit breaker's sampling duration — that coupling is recalled from the library's options
   validation, not verified against the pinned version, and not needed since this option is
   rejected on the other grounds); add a per-request timeout knob to `FixtureHttpClients` (a new
   mechanism for one class, ADR-0036); or build a factory-bypassing client like
   `StreamDistributionThrottleProbe` (drops the GET retries this class's read helpers rely on).
2. **It would hide a real defect.** 10 s is what production clients give an attempt. If Identity's
   create genuinely takes longer than that on a warmed stack, that is a product finding, and a test
   client more patient than production would make it invisible.

### Why the GET is the right warm-up and not a throwaway create

A throwaway create (the `OverlayLifecycleIntegrationTests` pattern) would warm the two rows a GET
cannot, but it is itself a single-attempt POST with the same 10 s cap: it fails exactly as
`CreateAsync` does, and tolerating that failure means swallowing `TimeoutRejectedException` around
a write whose outcome is unknown. Rejected.

### Considered and deferred — a fixture-wide `WaitForServiceHealthAsync("identity")`

It would close *Running ≠ listening* for every Identity test class, mirroring `overlay-designer`.
But it covers only the first row of §1.1, touches the most-shared file in the suite, and no other
Identity class has been reported. Out of scope; file separately if another class shows this.

## 4. Residual risk, stated

If T001/T004 show the create itself taking more than 10 s **after** the warm-up — i.e. the cost is
in Keycloak's admin path or the commit — this change does not fix it, and the correct outcome is
a product finding with the measured timings, **not** a raised timeout and **not** a retryable POST.

## 5. Out of scope

Any change to `FixtureHttpClients`, `AspireFixture`, `IdempotentRetry`, the standard handler's
options, Identity source, or any fact's body or assertions.

## 6. Observed (filled in during phases 4-5)

_T001 pre-change isolated runs, T003 post-change runs, verbatim summaries._

### T001 — pre-change, unchanged branch (2026-10-08)

Machine note: the first attempt at this task was killed mid-`dotnet build` by the harness's own
low-memory watchdog (`MSB4166: Child node "6" exited prematurely`), before the Aspire fixture or
Identity were ever reached — not a reproduction finding. Recovered by building once with
`-m:2` (caps MSBuild worker nodes), then running tests with `--no-build` so `dotnet test` does not
re-trigger its own full parallel build. No machine contention otherwise: `tasklist` showed no
AppHost/testhost process before each cold boot; the only pre-existing state was seven Docker
containers left over from a stack torn down ~25h earlier (stopped, not removed, by the orchestrator
immediately before this run — consistent with "run-mode containers outlive the AppHost").

**Whole-class run (`--filter FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests`),
cold boot 1 — 8/8, as expected:**

```
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.Disabling_a_device_needs_no_precondition [5 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_created_client_is_listed_with_the_version_its_next_rotation_needs [1 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down [2 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_superseded_by_another_admin_leaves_the_live_secret_working [2 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.Each_rotation_returns_the_version_the_next_one_must_send [2 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.Rotating_a_client_that_does_not_exist_creates_nothing [392 ms]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.Re_creating_an_existing_client_does_not_roll_its_secret [1 s]
Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_without_a_precondition_is_refused_with_428 [27 ms]

Test Run Successful.
Total tests: 8
     Passed: 8
 Total time: 2,8887 Minutes
```

**`A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down`
alone, three fresh cold boots (one `dotnet test` process per attempt, fixture tears down between
runs, `tasklist` confirmed no leftover AppHost/testhost process before each) — did not reproduce
on any attempt:**

```
Attempt 1: Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down [4 s]
           Total tests: 1  Passed: 1  Total time: 2,7633 Minutes

Attempt 2: Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down [8 s]
           Total tests: 1  Passed: 1  Total time: 2,7034 Minutes

Attempt 3: Passed SmartSentinelEye.Integration.Tests.Identity.RegisteredClientConcurrencyIntegrationTests.A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down [5 s]
           Total tests: 1  Passed: 1  Total time: 2,6396 Minutes
```

No `TimeoutRejectedException` observed in any run. **The issue's own caveat (§1.2) holds**: on
this quiet, single-stack machine, the cold-start race did not reproduce in 3/3 isolated attempts —
consistent with the issue's own observation having been made on a machine running a second
concurrent Aspire stack. Per plan.md §4, this makes the change's demonstrated effect
**undemonstrated/preventive** unless T003's post-change runs show something different (they
cannot show *more* passing than this already-green baseline, so the comparison that matters is
whether any run, pre- or post-change, ever produces the `TimeoutRejectedException` the issue
describes).
