# Feature Specification: A slow probe attempt is a failed attempt

**Feature Branch**: `test/3087-probe-attempt-timeout`

**Created**: 2026-10-22

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #3087 (*AspireFixture probe loops let a per-request HttpClient timeout
masquerade as the 8-minute startup timeout*)

**ADRs**: ADR-0103 (integration tests boot the real Aspire stack), ADR-0068 (the
fixture), ADR-0143 (the standard resilience handler the fixture's factory clients
carry), ADR-0037 (phases and gates), ADR-0139 / constitution §Testing (new
behaviour starts red), ADR-0144 (the lane; it may not write an ADR), ADR-0052 /
ADR-0053 (Shouldly, sentence-style test names).

**Predecessor**: spec 430 (#3076, PR #3088) — `ProbeExhaustedAsync` gives every
probe's exhaustion message the resource snapshot. This spec makes sure a slow
attempt reaches that message instead of escaping the loop.

> Every construct is named, not line-numbered, for the reason spec 098 gives.

---

## 1. The binding decision

Owner, 2026-10-22 (body of #3087):

> treat a `TaskCanceledException` raised while the fixture's own cancellation
> token is NOT cancelled as a failed attempt in the Keycloak and MediaMTX probe
> loops, so the probe's own exhaustion message (with the resource snapshot from
> #3076/PR #3088) fires instead of the 8-minute startup timeout. Test-fixture
> only. […] No ADR.

## 2. Premise, re-verified against `origin/develop` (`cbdc137c`, after #3088)

| Construct | What a single slow request raises today | Where it ends up |
|---|---|---|
| `WaitForKeycloakRealmAsync` — client from `CreateKeycloakClient()`: a plain `new HttpClient(handler)`, **no resilience handler**, default `HttpClient.Timeout` (100 s) | `TaskCanceledException` (inner `TimeoutException`), fixture token **not** cancelled | Not caught (`catch (HttpRequestException)` only) → escapes the loop → `InitializeAsync`'s `when (ex is OperationCanceledException or TaskCanceledException)` → reported as **"did not start within 8 minutes"**. **Premise holds, exactly as filed.** |
| `WaitForMediaMtxAsync` — client from `CreateMediaMtxApiClient()` → `App.CreateHttpClient("mediamtx", "api")`, which carries `FixtureHttpClients.Configure`'s **standard resilience handler** (ADR-0143: 10 s attempt / 30 s total) | `Polly.Timeout.TimeoutRejectedException` after 30 s — the total timeout fires long before `HttpClient.Timeout`'s 100 s | Not caught → escapes the loop → **not** matched by `InitializeAsync`'s filter either (`TimeoutRejectedException` derives from `ExecutionRejectedException : Exception`, not from `OperationCanceledException`) → `InitializeAsync` faults with a raw Polly exception, **no snapshot at all**. **Premise holds in substance, with a different exception type than filed — and the outcome is worse than filed.** |
| `WaitForServiceHealthAsync` — `App.CreateHttpClient(resourceName, "http")`, same resilience handler | `TimeoutRejectedException` | **Already caught** (`catch (TimeoutRejectedException)`, with a comment explaining exactly this defect). `HttpClient.Timeout` (100 s) cannot fire before the 30 s total. **Not the same defect — out of scope.** |

`ProbeExhaustedAsync` and the three `throw await ProbeExhaustedAsync("… after 60
attempts …")` sites from spec 430 are present. The `Task.Delay` between attempts
sits outside each `try` and honours the fixture token — unaffected.

**Reading of the owner's decision (A1):** its intent is "a per-request timeout is
a failed attempt, so the probe's own exhaustion message fires". For MediaMTX the
per-request timeout surfaces as `TimeoutRejectedException`; classifying only
`TaskCanceledException` there would change nothing observable. This spec
therefore classifies **both** shapes as an attempt timeout in **both** loops,
using `WaitForServiceHealthAsync`'s existing `TimeoutRejectedException` handling
as the precedent. Flagged for the reviewer; not a new decision.

## 3. User story

### US1 (P1) — An intermittently slow probe request does not end the probe

As the engineer reading a red integration job, when Keycloak's realm endpoint or
MediaMTX's control API answers some requests slowly (a few attempts time out while
the service is still coming up), I want the probe to count each timed-out request
as a failed attempt and keep polling, so that one slow request no longer escapes
as a bare Polly or `TaskCanceledException` and ends the boot at the first one.
A probe that accepts connections but *never* answers is not fixed by this: it
still ends in the 8-minute startup catch (see A4).

**Why P1 / only story**: it is the whole issue; one slice.

**Acceptance scenarios** (driven through the extracted probe loop, §plan)

```gherkin
Scenario: a request that hits HttpClient.Timeout is a failed attempt
  Given a probe client whose HttpClient.Timeout is 50 ms
    And a handler that never answers until its request is cancelled
    And a fixture token that is not cancelled
  When the probe loop runs with 3 attempts
  Then it makes 3 requests
  And it reports "not reachable" rather than throwing

Scenario: a resilience-handler timeout is a failed attempt
  Given a handler that throws TimeoutRejectedException on every request
  When the probe loop runs with 3 attempts
  Then it makes 3 requests and reports "not reachable"

Scenario: an attempt that times out and a later one that succeeds
  Given a handler that times out once, then answers 200
  When the probe loop runs with 3 attempts
  Then it reports "reachable" after 2 requests

Scenario: the fixture's own cancellation still propagates (counterfactual)
  Given a handler that never answers until its request is cancelled
    And no HttpClient.Timeout shorter than the test
  When the fixture token is cancelled during the first request
  Then the loop throws OperationCanceledException
  And it made exactly 1 request

Scenario: a cancelled fixture token that coincides with a timeout-shaped exception
  Given a fixture token that is already cancelled
    And a handler that throws TaskCanceledException
  Then the loop throws OperationCanceledException, not "not reachable"

Scenario: a refused connection is still a failed attempt (characterisation)
  Given a handler that throws HttpRequestException on every request
  Then the loop makes 3 requests and reports "not reachable"
```

Bad-request / auth / conflict scenarios: **N/A** — test infrastructure, no
endpoint, no caller identity, no concurrent writer.

## 4. Independent test procedure (Docker-free)

```sh
dotnet build tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --no-build \
  --filter "Category=FixtureLogic" -- RunConfiguration.TreatNoTestsAsError=true
```

CI's "Docker-free fixture logic tests" step verbatim. A real `HttpClient` over a
scripted `HttpMessageHandler`, in `OverlaySnapshotReadinessTests`' /
`FixtureRetryPolicyTests`' shape. Passing runs are discharged by CI's Docker
integration job: a probe whose first request answers returns before any new code
runs.

## 5. Assumptions (explicit)

- **A1** — `TimeoutRejectedException` is in scope for the MediaMTX loop (§2).
- **A2** — the classification keys on the **fixture token** being uncancelled
  (`cancellationToken.IsCancellationRequested`), never on
  `TaskCanceledException.CancellationToken` or its inner exception: the fixture
  token is the only fact that distinguishes "this request was slow" from "the
  boot's 8 minutes are up".
- **A3** — attempt count (60), delay (1 s), probe sentences and the spec-430
  `throw await ProbeExhaustedAsync(...)` sites stay unchanged; spec 430's
  source-scan test must pass unmodified.
- **A4** — worst case gets longer, not shorter: 60 Keycloak attempts at 100 s
  each (6000 s) and 60 MediaMTX attempts at ~31 s each (~1860 s) both exceed the
  8-minute budget, so a probe that hangs *every* request still ends as the
  startup timeout, not as the probe's own exhaustion sentence. The fix helps the
  intermittent case (a few slow requests during realm import), which today ends
  the boot at the first one. Not lowering `HttpClient.Timeout` — not decided by
  the owner.
- **Residual** — `BrokenCircuitException` from the shared resilience handler is
  not caught by the helper, is uncaught at the MediaMTX probe and in
  `WaitForServiceHealthAsync`, and the startup filter
  (`OperationCanceledException or TaskCanceledException`) does not match it
  either. Out of scope here; recorded so it is not mistaken for covered.

## 6. Locked tech / latency

xUnit + Shouldly + hand-written handler, `Category=FixtureLogic`. **Latency budget
(constitution §IV): N/A** — test fixture only, no leg of the event→overlay path.
