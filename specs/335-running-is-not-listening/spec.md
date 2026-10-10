# Spec 335 — Running is not listening

**Issue:** [#2777](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2777) —
*Identity integration classes other than `RegisteredClientConcurrencyIntegrationTests` share the
same cold-start CreateAsync/POST timeout exposure*. Label `agent:ready`; Project #13 (In
Progress, this lane run).
**Branch:** `fix/2777-identity-fixture-wide-warmup` (cut from `develop` @ `0b94c8cf`)
**Created:** 2026-10-11 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0103 (integration via the Aspire fixture), 0142/0143 (retry safety — preserved, not
touched: no POST becomes retryable), 0036 (smallest change), 0139 §Testing (behaviour-preserving
changes are characterised), 0144 (lane).
**New ADR:** no. No decision is being made here — the user already decided (issue comment,
2026-10-08): fixture-wide `WaitForServiceHealthAsync("identity")` over a per-class warm-up.
**Latency budget (§IV):** N/A — test harness only; not on the event→overlay path.

**Spec number.** `develop` tops out at 334. 330 and 332 are not on `develop`: 330 (#2286) is an
unmerged worktree, 332 is unused on any branch (checked across every local/remote branch
2026-10-11). This spec claims 335 to avoid either collision. Re-check before opening the PR.

## 1. The premise, re-checked on this tree

| Claim in the issue | On this tree |
|---|---|
| `AbsentDeviceIdentifierIsRefusedIntegrationTests.InitializeAsync` waits only for `identity` → `Running` | **Confirmed** (`tests/Integration.Tests/Identity/AbsentDeviceIdentifierIsRefusedIntegrationTests.cs:37-42`) — no warm-up GET, no `WaitForServiceHealthAsync` call. |
| Every row sends a non-retried `POST /devices/register` | **Confirmed** — `RegisterDeviceRequest` POSTs at lines 70-71, 86-87, 101, 126, 130, 153; ADR-0143 means a `POST` is single-attempt. |
| The same shape as #2598 | **Confirmed** — identical `InitializeAsync` pattern to what #2598/spec 315 found in `RegisteredClientConcurrencyIntegrationTests` before its own per-class warm-up fix. |
| `AspireFixture.InitializeAsync` already has the pattern for one resource | **Confirmed** — `overlay-designer` gets `WaitForServiceHealthAsync("overlay-designer", …)` at `AspireFixture.cs:468`, with its own comment explaining *why*: "Running only means the process launched — it does not mean Kestrel has bound its listener." `identity` gets only the `Running` wait (lines 456-458), with a comment that says it is tailed and that this was "the only such resource with no gate here" — true for log-tailing, not true for listener readiness. |

## 2. Decision (already made by the user, 2026-10-08)

**Fixture-wide `WaitForServiceHealthAsync("identity", cts.Token)`**, added to
`AspireFixture.InitializeAsync` immediately after the existing `identity` → `Running` wait,
mirroring the `overlay-designer` call three lines below it. This closes "Running ≠ listening" once
for every Identity integration test class — present (`AbsentDeviceIdentifierIsRefusedIntegrationTests`
and any other sibling not yet filed) and future — rather than adding a per-class warm-up to each
one as it is separately discovered flaky.

**`RegisteredClientConcurrencyIntegrationTests`'s own per-class warm-up (spec 315) is left as
is.** It predates this fix, is harmless now that the fixture also warms `identity`, and removing
it is a second, unrelated change this issue did not ask for (ADR-0036: smallest change). It also
warms two legs `WaitForServiceHealthAsync`'s `/health` probe does not reach (the Keycloak-admin
`CreateClientAsync` call and the EF insert) — spec 315 §1.1 rows 5-6 — so it is not strictly
redundant.

**Does not touch:** any resilience/retry setting (ADR-0142/0143 untouched — no `POST` becomes
retryable), the `RegisteredClientConcurrencyIntegrationTests` warm-up, `overlay-designer`'s
existing call, or any other resource's wait.

## 3. User story

**US1 (P1) — Any Identity integration fact can be run alone.** As an engineer running one fact of
any Identity test class with `--filter` (a counterfactual, a targeted CI rerun), I want the
fixture's own boot to have already proven Identity's listener bound before any fact's
`InitializeAsync` runs, so a fact's result says something about the behaviour it tests rather than
about whether Identity happened to have served a request yet.

### Acceptance scenarios

```gherkin
Scenario: the fixture itself warms Identity's listener, once, for the whole collection
  Given a freshly booted Aspire fixture
  When InitializeAsync runs
  Then after the identity → Running wait, the fixture calls WaitForServiceHealthAsync("identity")
  And this happens once, in the fixture, before any test class's own InitializeAsync runs

Scenario: an Identity class with no per-class warm-up still reaches its own assertions
  Given AbsentDeviceIdentifierIsRefusedIntegrationTests runs alone via --filter
  When its InitializeAsync (unchanged) waits for identity → Running
  Then the fixture-level health wait has already completed by this point
  And the first POST /devices/register is sent to an Identity whose listener is proven bound

Scenario: no retry policy changes
  Given the change is applied
  When a POST exceeds its single attempt
  Then it is not retried — no resilience option, timeout value or retry predicate changed anywhere

Scenario: fixture boot fails loudly, and distinctly, if Identity's listener never binds
  Given WaitForServiceHealthAsync("identity") exhausts its 60 attempts
  When InitializeAsync's wait chain reaches it
  Then the TimeoutException names "identity /health was not reachable after 60 attempts"
  And ThrowIfAnyGatedResourceDiedAsync still runs afterwards for every gated resource
```

### Independent end-to-end test procedure

1. One Aspire stack on the machine (memory: *one machine, one Aspire stack*); stop any other.
2. `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~AbsentDeviceIdentifierIsRefusedIntegrationTests"` on a fresh boot.
3. `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests"` — 8/8, confirming the pre-existing per-class warm-up still behaves identically (characterisation).
4. Full `Identity/` folder, one boot.

## 4. What this spec does not attempt

Not a repro-then-fix: like spec 315, the underlying race is a cold-stack timing gap that does not
reproduce on demand. No claim is made here that the issue's exact `TimeoutRejectedException` was
observed on this tree before the change — the fix is preventive, verified by (a) the identical,
already-accepted shape of spec 315's own fix and the `overlay-designer` precedent already in the
fixture, and (b) CI's own `integration tests (Docker)` job, which boots the fixture for real on
every PR and is the authoritative verification for fixture-level changes (§5).

## 5. Verification record

See plan.md §3 and the PR body for what was actually run.
