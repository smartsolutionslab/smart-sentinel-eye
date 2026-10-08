# Spec 317 — The outage that held the boot

**Issue:** [#2170](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2170)
— *An unreachable Keycloak holds Identity's start for 30 seconds, and WaitFor with it*. Label
`agent:ready`; Project #13, status **In Progress** (verified 2026-10-08).
**Branch:** `fix/2170-bounded-keycloak-sweep-timeout` (cut from `develop` @ `ba37ca5a`)
**Created:** 2026-10-08 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0134 Decision 1 (the startup sweep — kept, and kept *at startup*), 0143 (the standard
resilience handler and its budget — read, not changed), 0103 (the Aspire fixture whose `WaitFor`
chains inherit the delay), 0050 (`[LoggerMessage]` structured logging), 0054 (hand-written fakes),
0049 (`CancellationToken` discipline), 0139 §Testing (new behaviour starts red), 0036 (smallest
change; no config knob).
**New ADR:** no. The decision — bounded, inline — was made by the user on the issue
(2026-10-08 comment). This spec implements it; the bound's *value* is an implementation detail
justified in §4, inside a decision already taken.
**Latency budget (§IV):** N/A — Identity's host start, not the event→overlay path.

**Spec number.** `develop` tops out at **315**; `316` is claimed by the branch
`feat/316-operator-mfe-shell-and-first-remote`. No branch or open PR claims 317 (checked
2026-10-08). Three sibling worktrees (`sse-2077`, `sse-2325`, `sse-2565`) were cut from the same
commit at the same time and may reach for the same number — **re-check before opening the PR**.

## 1. The premise, re-checked on this tree

| Claim in the issue | On this tree |
|---|---|
| The sweep is awaited inline in `StartAsync` | **Confirmed.** `KioskPrivilegeSweepHostedService.StartAsync` (`src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs:36-52`) awaits `SweepOnceAsync` with the host's token and nothing else. |
| `client.Timeout = Timeout.InfiniteTimeSpan` | **Confirmed**, now at `IdentityInfrastructureModule.cs:206` (the issue's `:183` has drifted), with a comment explaining it is deliberate: `HttpClient.Timeout` wraps the whole resilience pipeline. **Not touched by this spec.** |
| The only bound is the pipeline's ~30 s | **Confirmed, and composed as follows.** ServiceDefaults applies `AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)` (`src/ServiceDefaults/Extensions.cs:52`) to every client: `AttemptTimeout` 10 s, `MaxRetryAttempts` 3 (exponential, 2 s base, jittered), `TotalRequestTimeout` 30 s — pinned by `tests/ServiceDefaults.Tests/ResilienceHandlerNestingTests.cs:70-72`. The sweep's first call (`GET admin/realms/{realm}/clients`) is a GET, so it is retried; its authorization handler first mints a token on a second client that keeps retries via `RetryEveryMethod()` (ADR-0143). Against a black hole, each attempt hits the 10 s cap and the 30 s total ends it: the measured **30154 ms** (spec 092 `verification.md` §5). |
| Boot continues after the wait | **Confirmed.** The `catch (Exception) when (exception is not OperationCanceledException)` logs `KioskPrivilegeSweepFailed` (Warning, `Log.cs:64`) and returns. |
| Nothing tells an operator the boot was held | **Confirmed.** `KioskPrivilegeSweepFailed`'s message says the sweep "could not complete"; it says nothing about how long it waited. |

### 1.1 What "the guarantee" is

Spec 092's phase 5 (`specs/092-the-sweep-is-registered-or-retired/verification.md` §2) settled
three things from one boot's log, in order; the third is the one at stake here:

> **The sweep is inline on the startup path.** Wolverine's own start follows it by 15 ms.

Because hosted services start in registration order and `StartAsync` awaits the pass, the pass has
**ended — completed, failed, or (after this spec) given up — before** `ApplicationStarted`, before
Aspire reports Identity `Running`, and so before anything `WaitFor(identity-api)` proceeds. "Has
the sweep run?" is answerable by reading the boot log up to `Application started`: either
`Stripped inherited realm privileges…`, or a Warning, or (empty realm / nothing to strip) silence
with no Warning. **This spec preserves that ordering exactly.** It changes only how long the "gave
up" arm can take, and adds a distinct line for it so the three outcomes stay distinguishable.

### 1.2 What the 30 s is spent on, and what a bound cuts

| Keycloak state | Today | With a 5 s bound |
|---|---|---|
| Reachable, healthy | 519 ms measured (cold boot, one kiosk, includes token mint + TLS) | unchanged |
| Black-holed (no SYN-ACK) | 30154 ms measured, ends in the pipeline's total timeout | ≈ 5 s, ends in the sweep's own bound — no attempt has hit its 10 s cap yet |
| Refusing fast (host up, Keycloak down) | ≈ 14 s+ (four attempts across ~2 + 4 + 8 s jittered back-off) — reasoned, not measured | ≈ 5 s, ends in the sweep's own bound, mid back-off |
| Shutdown requested during start | `OperationCanceledException` propagates (host is stopping) | **unchanged** — the bound must not swallow it |

**Found at phase 6 review, recorded rather than silently accepted**: in the "refusing fast" row,
`KioskPrivilegeSweepTimedOut` carries no exception — unlike `KioskPrivilegeSweepFailed`, which does.
When the bound fires mid back-off, the operator's Warning line no longer names the underlying
`HttpRequestException` the way the pre-existing failed-pass line does. The resilience pipeline's own
per-attempt telemetry (`AddStandardResilienceHandler`) may still log each failed attempt separately
with its exception, which would make this harmless — but that is **not verified against this
pipeline's actual telemetry configuration**, only assumed. Worth a quick check against a stopped
Keycloak before relying on it; not a blocker for this PR, since the only behaviour this spec commits
to is "a slow boot names its cause" at the `KioskPrivilegeSweepTimedOut`/`KioskPrivilegeSweepFailed`
level, which still holds (the bound itself is always named, even without the exception).

## 2. User story

**US1 (P1) — A Keycloak outage costs Identity's boot a bounded few seconds, and says so.** As an
operator (or the Aspire fixture) waiting on `WaitFor(identity-api)` while Keycloak is unreachable,
I want Identity's startup sweep to give up after its own short, fixed bound and log that it did,
so that the stack comes up in seconds rather than half a minute, and a slow boot names its cause —
while the sweep still finishes, one way or the other, before Identity reports started.

No US2: nothing else here is independently shippable.

### Acceptance scenarios

```gherkin
Feature: The startup sweep has its own bound

  Background:
    Given Identity's own composition (AddIdentityInfrastructure)
    And the sweep's bound is 5 seconds

  Scenario: Happy path — a reachable Keycloak is unaffected
    Given a Keycloak that answers the enumeration within the bound
    When the startup sweep runs
    Then it completes before StartAsync returns
    And no "gave up" Warning is logged
    # Existing coverage: KioskPrivilegeSweepStartupTests, KioskPrivilegeSweepSteadyStateTests,
    # KioskPrivilegeSweepStartupIntegrationTests — all must pass unmodified.

  Scenario: Outage — a Keycloak that never answers is abandoned at the bound
    Given a Keycloak whose enumeration never answers (honours cancellation only)
    When the startup sweep runs
    Then StartAsync has not returned when 5 seconds less one tick have elapsed
    And StartAsync returns without throwing once 5 seconds have elapsed
    And exactly one Warning "KioskPrivilegeSweepTimedOut" is logged, carrying the bound as a field
    And no "KioskPrivilegeSweepFailed" Warning is logged for the same pass

  Scenario: Conflict — host shutdown during the sweep is not mistaken for the bound
    Given a Keycloak whose enumeration never answers
    When the host's own start token is cancelled before the bound elapses
    Then StartAsync throws OperationCanceledException
    And no "KioskPrivilegeSweepTimedOut" Warning is logged

  Scenario: Bad request — a provider that fails fast still fails fast
    Given a Keycloak that throws a transport failure immediately
    When the startup sweep runs
    Then StartAsync returns without throwing, well before the bound
    And the existing "KioskPrivilegeSweepFailed" Warning is logged
    And no "KioskPrivilegeSweepTimedOut" Warning is logged
    # Red C (KioskPrivilegeSweepStartupTests) already pins the first two lines; it must pass unmodified.

  # Auth: no new authority is requested or exercised. A refused admin token (401/403 from
  # Keycloak) is a fast failure and falls under "Bad request" above. N/A otherwise.
```

### Independent end-to-end test procedure (phase 5)

1. **Repeat spec 092 §5's measurement, after the change.** Compose `AddIdentityInfrastructure`
   with `ConnectionStrings:keycloak = http://203.0.113.7:8080` (TEST-NET-3, black-holed) and
   ServiceDefaults' resilience handler; drive the hosted services' `StartAsync`; record the wall
   time. Expect **≈ 5000 ms** (not 30154 ms) and the `KioskPrivilegeSweepTimedOut` line verbatim.
   Run it twice (machine-churn rule). Scratch harness only; remove before commit, as spec 092 did.
2. **Healthy control in the real fixture.** Boot the Aspire stack, read Identity's console up to
   `Application started`: the sweep's Keycloak calls precede Wolverine's start (the 092 ordering)
   and **no** `KioskPrivilegeSweepTimedOut` line appears. Record the sweep's own duration (first
   Keycloak request → last) to re-confirm the headroom §4 assumes.

## 3. Requirements

- **FR-001** The startup sweep MUST be abandoned when it has not ended within a fixed bound of
  **5 seconds** from the start of `StartAsync`.
- **FR-002** Abandoning at the bound MUST NOT fail the host start: `StartAsync` returns normally.
- **FR-003** Abandoning at the bound MUST log one Warning, distinct from `KioskPrivilegeSweepFailed`,
  that names the bound as a structured field and says enrolled kiosks may still hold inherited
  realm privileges until the next start.
- **FR-004** Cancellation of the host's own start token MUST still propagate as
  `OperationCanceledException` and MUST NOT be logged as hitting the bound.
- **FR-005** The sweep MUST remain awaited inside `StartAsync` (not moved to `ExecuteAsync` or a
  fire-and-forget task); its outcome is decided before `StartAsync` returns.
- **FR-006** The bound MUST be measured with the `TimeProvider` Identity already registers
  (`IdentityInfrastructureModule.cs:50`), so it is testable without sleeping.
- **FR-007** No change to `HttpClient.Timeout`, to any resilience option, or to the sweep pass
  (`KioskPrivilegeSweep`) itself.

### Out of scope

- Making the bound configurable (no need exists — ADR-0036). A deployment that outgrows it sees
  the FR-003 Warning on every boot, which is the signal to revisit.
- Retrying the sweep after an abandoned start. "The next start tries again" is the existing
  contract, unchanged.
- Reporting partial progress of an abandoned pass (see §5).

## 4. The bound: 5 seconds, and why

The issue says only "well under 30 s". The number is chosen against three things on this tree:

1. **It must sit below the pipeline's per-attempt timeout (10 s).** At or above 10 s, a black-holed
   Keycloak ends in a race between Polly's `TimeoutRejectedException` (logged as the generic
   "could not complete" Warning) and the sweep's own bound (logged as FR-003). Below it, the sweep's
   own bound is deterministically what ends an outage, in both outage modes of §1.2, so the operator
   always reads the line that names the wait. Half the attempt timeout leaves margin for the time
   the pass spends before its first call (scope creation, DI resolution).
2. **It must leave a healthy pass ample room.** Spec 092 measured the whole pass at **519 ms** on a
   cold boot (token mint 374 ms including TLS and JIT; enumeration 91 ms; one kiosk ~54 ms for two
   GETs plus a removal). 5 s is ~10× that. Per additional kiosk the pass costs roughly two GETs
   (~25-54 ms), so a healthy realm stays inside the bound up to roughly **80 enrolled kiosks** at the
   measured worst per-kiosk cost.
3. **It must be short enough to matter for `WaitFor`.** 5 s cuts the measured outage delay by
   ~83 % (30.2 s → 5 s) and the refused-connection delay by roughly two-thirds.

**Assumption, marked:** no fab enrols more than ~80 kiosks. The constitution sizes cameras (250),
not kiosks; nothing on this tree states a kiosk ceiling. The failure mode if the assumption breaks
is **visible, not silent** — a healthy-but-large realm logs `KioskPrivilegeSweepTimedOut` on every
boot — and the tail of the enumeration stays unswept, which is a security cost (§5). Shorter values
(3 s, ~45 kiosks) buy little against `WaitFor` and halve that headroom; 10 s collides with point 1.

## 5. Consequences

- **An abandoned pass sweeps a prefix.** Cancellation propagates out of `SweepAsync`'s per-kiosk
  loop (its catch excludes `OperationCanceledException`), so kiosks already stripped stay stripped
  and the rest wait for the next start; `SweptKioskPrivileges` is not logged for a pass that did not
  finish. This is the same contract as today's catch-and-continue — the next start tries again —
  reached sooner.
- **The guarantee holds** (§1.1): the pass's outcome is fixed before `ApplicationStarted`.
- **Spec 092 Red C, the steady-state tests, and the integration test pass unmodified.** The hosted
  service gains a `TimeProvider` constructor parameter; Red C builds it with
  `ActivatorUtilities.CreateInstance` from Identity's own provider, which registers
  `TimeProvider.System`, so it needs no edit. An edit to any of them means behaviour moved — block.
