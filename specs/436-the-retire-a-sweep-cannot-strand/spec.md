# Feature Specification: The retire a sweep cannot strand

**Feature Branch**: `fix/2783-terminal-write-retry`

**Created**: 2026-10-22

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #2783 (*a retire can already lose a race to a health-watcher write, both bumping the
stream's version*)

**ADRs**: **ADR-0182** (Accepted — a terminal, idempotent message write may retry a lost race, once
verified to reload; the decision this spec implements), ADR-0113 (two-layer optimistic concurrency;
amended by ADR-0182), ADR-0088 (Wolverine defaults: per-module queues, Postgres outbox, eager
transactions), ADR-0042 / ADR-0057 (Wolverine behind the handler interfaces), ADR-0139 / constitution
§Testing (new behaviour starts red), ADR-0144 (the lane; it may not write or edit an ADR).

**Predecessors**: spec 028 (FR-008, stream retirement), spec 296 (`WallSceneSwitchFailurePolicy`, the
first chain-scoped failure rule — the *opposite* outcome, correctly, for a decision against state),
spec 318 (#2743, the provision/retire race), spec 328 (`WebhookIntegrationRevokedFailurePolicy`),
spec 389 (`CameraEventFailurePolicyCharacterisationTests`).

> Constructs are named; line numbers pin premises on `cfd0ba4f` only.

---

## 1. The binding decision

ADR-0182 (Accepted 2026-10-22, PR #3085) Decision 1 makes verification a **precondition**: the race
must be real and the retry attempts must reload, or the lane blocks. Decision 3 permits automatic
retry on a concurrency conflict for exactly one write, `CameraRetiredV1` → `RetireStreamCommand`,
because it is terminal, idempotent, not computed from what it read, and reloading. Decision 4: if the
default does not already retry with a fresh scope, pin a retry with an `IHandlerPolicy` scoped to that
chain; **never** `MoveToErrorQueue` on the first conflict.

## 2. Premise, verified at `cfd0ba4f` (phase 1 — the precondition)

### 2a. The race is real (by reading; forced against Postgres in US1's IT)

| Fact | Where |
|---|---|
| Retire loads the stream by camera, calls `Retire`, saves, then removes the path | `RetireStreamCommandHandler.cs:23,33,42,46` |
| `ReportHealthy` assigns `LastSuccessAt` on **every** sweep, so a healthy stream's row is modified every ~2 s | `Stream.cs:208-218`; `StreamHealthWatcher.cs:40` |
| The sweep writes through its **own** per-camera scope, so its DbContext genuinely competes with the retire's | `StreamHealthWatcher.cs:210` |
| Every modified root's `Version` is bumped; EF puts the loaded value in the `WHERE` | `AggregateVersionInterceptor.cs:89,101-102`; `StreamConfiguration.cs:137-141` (`IsConcurrencyToken`) |
| Neither the retire handler nor `CameraRetiredIntegrationEventHandler` catches `DbUpdateConcurrencyException` | `RetireStreamCommandHandler.cs:42`; `CameraRetiredIntegrationEventHandler.cs:41-54` |

So a sweep save landing between the retire's load and save makes the retire's `SaveAsync` throw
`DbUpdateConcurrencyException`. Nothing durable happened: the stream stays live, unretired, swept and
**watchable** for a camera the catalogue has retired.

Nuance (from `ProvisionRetireRaceIntegrationTests`' own doc): a `Degraded` stream re-reported with the
same error issues no `UPDATE`, so the window is chiefly a **Healthy** stream's (every sweep writes it).

### 2b. What the default does — **not** what ADR-0182's Context assumed

ADR-0182 records the default as "three immediate attempts, then the dead letter queue". Decompiling the
pinned WolverineFx **6.51.0** says otherwise, and the repository already pins it for the two sibling
chains:

- `FailureRuleCollection.combine` yields a requeue ladder **only when `MaximumAttempts` has a value**
  on the chain or the graph; nothing in this repository sets one. `DetermineExecutionContinuation`
  falls through to `new MoveToErrorQueue(e)` when no rule matches.
- No StreamDistribution rule exists (`CameraEventFailurePolicyCharacterisationTests.StreamDistribution_registers_no_wolverine_failure_rule`),
  and Wolverine's own defaults cover only `DuplicateIncomingEnvelopeException`, `ApplyItemException`
  and `RateLimitExceededException` (`WolverineOptions.cs:1052-1053,2234` decompiled). Wolverine.EF's
  only `DbUpdateConcurrencyException` catch is for sagas (`EFCorePersistenceFrameProvider.WrapSagaConcurrencyException`).
- `CameraEventFailurePolicyCharacterisationTests.A_refused_address_is_moved_to_the_error_queue_on_every_attempt_without_a_retry`
  observes exactly this — `MoveToErrorQueue` on attempt 1 — for `CameraRegisteredV1` and
  `CameraAddressChangedV1` (its doc: *"the plan's 'three attempts' is not what the default does"*).

**So today the first lost race dead-letters the retire. There are no retry attempts to reload.**
ADR-0182 Decision 2 ("nothing is pinned") cannot apply; Decision 4 (pin) does. Recorded here, not
edited into the ADR (ADR-0144: the lane does not edit ADRs) — flagged for the owner.

### 2c. A pinned retry does reload (by reading; observed in US1's unit facts)

- On failure `Executor.ExecuteAsync` calls `context.ClearAllAsync()` (discarding the failed attempt's
  captured outbox messages) before choosing the continuation (`Executor.cs:207`).
- `RetryInlineContinuation.ExecuteAsync` → `MessageContext.RetryExecutionNowAsync` →
  `Pipeline.InvokeAsync(envelope)` → `Executor.ExecuteAsync` → **`Handler.HandleAsync` again**
  (`MessageContext.cs:638-645`, `Executor.cs:193`). The inline `InvokeAsync` path does the same via
  `InvokeResult.TryAgain` (`Executor.cs:116,225`).
- The generated handler creates its service scope **inside** `HandleAsync`
  (`JasperFx.CodeGeneration.Services.ScopedContainerCreation`, `await using var … CreateAsyncScope()`),
  and the chain is service-located (`WolverineDefaults.cs:79-83`: `IRtspGateway`,
  `DomainEventDispatcher`). A scoped `StreamDistributionDbContext` cannot be a field of the cached
  generated handler, so each attempt resolves a new repository on a new DbContext and reloads.

Reload is therefore achievable **without a design change**, with `RetryNow`/`RetryWithCooldown`
(inline, no durable inbox needed). `ScheduleRetry` would also reload but needs `durableInboxMessageTypes`
(`WolverineDefaults.cs:162`) and is not chosen (plan §2). **The premise holds; the lane proceeds.**
The by-reading claims in 2c are observed, not trusted, by T002 (fresh scope per attempt) — if that
fact cannot tell a reused scope from a fresh one (T007 counterfactual), the lane blocks per Decision 1.

---

## 3. User stories

### US1 (P1) — A retire that loses a race to a health sweep still retires the stream

**Why**: a dead-lettered `CameraRetiredV1` leaves a retired camera's stream live and watchable
(ADR-0182 Context). This is the whole slice.

**Acceptance scenarios**

```gherkin
Scenario: Happy — a lost race is retried on fresh state and the stream ends Retired
  Given a Healthy stream for camera C
  And the first retire attempt's save throws DbUpdateConcurrencyException
  When CameraRetiredV1 for C is handled
  Then a second attempt loads the stream through a new scope
  And the stream ends Retired
  And the MediaMTX path is removed
  And nothing is moved to the error queue

Scenario: Conflict — the conflict outlasts the ladder
  Given every retire attempt's save throws DbUpdateConcurrencyException
  When CameraRetiredV1 is handled
  Then the ladder's attempts are made, each on a new scope
  And after the last the default behaviour (error queue) applies — never on the first conflict

Scenario: Bad request — a non-concurrency failure on the retire chain is not retried by this policy
  Given the retire chain fails with InvalidOperationException (e.g. the SFU unreachable after the save)
  When the continuation is chosen
  Then it is MoveToErrorQueue on attempt 1, as today

Scenario: Scope — a non-terminal or non-idempotent write still does not retry (ADR-0113 unchanged)
  Given a DbUpdateConcurrencyException on CameraRegisteredV1 (provision: creates) or
        CameraAddressChangedV1 (repoint: decides against the recorded camera version)
  When the continuation is chosen
  Then it is MoveToErrorQueue on attempt 1
  And WallSceneSwitchRequestedV1 still dead-letters on its first conflict (spec 296, unmodified)

Scenario: Already retired — a retry onto a stream someone else retired is a no-op
  Given attempt 2 reloads a stream already Retired
  Then Retire raises nothing, the save is a no-op, and the path is removed
```

**Auth**: N/A — a broker-internal subscriber; no endpoint, scope, fab or principal changes.

## 4. Independent end-to-end test

1. `tests/StreamDistribution.Infrastructure.Tests` (no Docker): the policy's continuations per
   attempt for the four chains above, and a Wolverine host (`StubAllExternalTransports`, no Postgres)
   that handles `CameraRetiredV1` through the real handler with a repository whose first save throws,
   recording one repository instance per attempt.
2. `tests/Integration.Tests/StreamDistribution/RetireHealthRaceIntegrationTests.cs` (Aspire fixture,
   ADR-0182 Implementation Note 1): against real Postgres, interpose a health save between the
   retire's load and save → the retire's save throws `DbUpdateConcurrencyException` (the race is
   real); a second attempt on a fresh scope retires the stream and removes the path.

## 5. Locked choices

.NET 10, Wolverine 6.51.0 `IHandlerPolicy` (shape of `WallSceneSwitchFailurePolicy`), registered via
`AddWolverineForContext(..., configureMore:)`; EF Core optimistic concurrency unchanged
(ADR-0113 Layer 2); xUnit + Shouldly + hand-written fakes; Aspire fixture (ADR-0103).

## 6. Out of scope

- Stopping the sweep from bumping the version (ADR-0182 Alternatives — rejected here).
- The **path-removal** failure on the same chain (`CameraRetiredIntegrationEventHandler.cs:48-53`
  says Wolverine retries it; per 2b it is dead-lettered on attempt 1). Benign — the row is already
  Retired and `MediaMtxReconciler` removes orphaned paths of retired streams
  (`MediaMtxReconciler.cs:18-22,148,206`) — but the comment is inaccurate. Not this issue; reported
  to the orchestrator for a separate decision.
- Correcting ADR-0182's Context sentence (2b): owner's edit.

## 7. Latency

**N/A.** Retirement is control-plane work off every §IV leg (ADR-0182). The ladder adds at most
~2.6 s of inline delay to one camera's retirement, only on a lost race.
