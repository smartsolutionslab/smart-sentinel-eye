# ADR-0182: A terminal, idempotent message write may retry a lost race, once verified to reload

**Status:** **Accepted** by the owner on PR #3085, 2026-10-22 (the owner ruled on 2026-10-21 on #2783: verify first, pin only if needed; on 2026-10-22 adopted the four retry conditions as the decision and made the verification a lane precondition; this records both)
**Date:** 2026-10-21
**Amends:** ADR-0113 (Corrections to ADR-0043, item 2: "Automatic retry is forbidden in both the backend and the SPAs")
**Relates to:** ADR-0088, ADR-0144, spec 296, spec 318, spec 328, issues 2743, 2783

## Context

ADR-0113 removed ADR-0043's retry-once, for a reason that is right where it applies: re-applying a
mutation to freshly loaded state **is** the silent overwrite optimistic concurrency exists to prevent.
Its wording is absolute: *"A conflict surfaces to the caller; the client refetches and the human
decides. Automatic retry is forbidden in both the backend and the SPAs."*

#2783 (found during spec 318) names a case where that wording produces the wrong outcome. Verified by
reading `develop` at `6206ecf9`:

- `RetireStreamCommandHandler` loads the stream, calls `Stream.Retire`, and saves
  (`RetireStreamCommandHandler.cs:24-41`); it is driven by `CameraRetiredIntegrationEventHandler`, a
  Wolverine subscriber to `CameraRetiredV1`.
- `ReportStreamHealthCommandHandler` loads the same row and calls `ReportHealthy`, which assigns
  `LastSuccessAt` on **every** sweep (`Stream.cs:208-232`), so every sweep modifies the row.
- `AggregateVersionInterceptor` bumps `Version` on every modified aggregate root, and EF puts the loaded
  version in the `WHERE`. So a retire whose load precedes a sweep's save **loses**: its `SaveAsync` throws
  `DbUpdateConcurrencyException`.
- The retire handler does not catch it, and no failure policy covers the `CameraRetiredV1` chain (the
  repository's only two, `WallSceneSwitchFailurePolicy` and `WebhookIntegrationRevokedFailurePolicy`, are
  scoped to other chains). So the outcome is **Wolverine's unpinned default**, which spec 328 recorded,
  from decompiling WolverineFx 6.51.0 (the pinned version), as **three immediate attempts, then the dead
  letter queue**.

**What the retire is**, and why the absolute rule fits it badly:

- **Terminal.** `Retire` moves the stream to `Retired`; nothing moves it out.
- **Idempotent.** `Retire` returns without raising if the stream is already retired (`Stream.cs:124-131`),
  and the handler's own comment relies on that for the outbox redelivery after an SFU failure.
- **Not a decision against state.** It does not compute a new value from what it read; it applies one
  fixed transition the camera's retirement has already decided. Re-applying it to fresh state cannot
  overwrite anything a concurrent writer meant, because the only concurrent writer here is a health sweep
  on a stream that is about to stop being swept.

**And what the default costs.** A dead-lettered `CameraRetiredV1` leaves the stream un-retired: the
camera is retired in the catalogue, but its stream stays live and **watchable**, and the watcher keeps
sweeping it. Spec 296's `MoveToErrorQueue` was right for a wall-scene switch, which *is* a decision against
state; copying it here would make this outcome deterministic.

**What is not known.** Whether Wolverine's immediate attempts each get a **fresh** DbContext. If an attempt
reuses the scope, the tracked `Stream` still carries the stale original version, every attempt conflicts
again, and three attempts end in the dead letter queue regardless. If each attempt reloads, the second
attempt almost certainly succeeds and the default is already adequate. **Unverified**: no test forces
this race, and spec 296 §1.10 and spec 318 both left it open.

## Decision

**1. Verify first; this is a precondition, not a step the lane may skip.** Before building anything, the
lane runs a live test that forces a retire/health version conflict on the `CameraRetiredV1` chain and
observes (a) whether the race is real, i.e. a health save between the retire's load and save does make
the retire's save conflict, (b) how many attempts Wolverine makes, and (c) whether each attempt loads the
stream through a new DbContext scope. The answer is recorded on #2783. **If the premise fails** (the race
cannot be produced, or the observation does not settle whether attempts reload), **the lane blocks**
(`agent:blocked`, with the verbatim observation) rather than pinning blind or closing on an assumption.

**2. If every retry attempt is demonstrably fresh-scope, nothing is pinned.** The default already reloads
and re-applies an idempotent terminal transition, which this ADR permits (point 3). The verification test
stays as the characterisation that keeps it true across Wolverine upgrades.

**3. ADR-0113's prohibition is narrowed, for this class of write only (decided 2026-10-22).** Automatic
retry on a concurrency conflict is **permitted** for a backend message handler whose write is **all** of
the following four conditions:

- **terminal**: it moves the aggregate to a state nothing moves it out of;
- **idempotent**: applying it to an aggregate already in that state is a no-op that raises nothing;
- **not computed from what it read**: the transition is fixed by the message, not derived from the
  aggregate's current values;
- **reloading**: each attempt reads the aggregate afresh.

Everything else keeps ADR-0113 unchanged: HTTP commands still surface 409/412 to the caller, the SPAs
still never retry, and a handler that decides against state (spec 296's scene switch) still does not retry.
`CameraRetiredV1` → `RetireStreamCommand` is the **one** write this ADR names; another needs its own
showing that it meets all four conditions.

**4. If the attempts are not fresh-scope, pin a retry on that chain.** An `IHandlerPolicy`, in the shape of
`WallSceneSwitchFailurePolicy`, scoped to exactly the `CameraRetiredV1` handler chain:
`OnException<DbUpdateConcurrencyException>()` retried with a short bounded ladder in a way that does
reload. Which Wolverine mechanism achieves that (`RetryNow`, `RetryWithCooldown` or `ScheduleRetry`) is
for the verification to establish against the pinned 6.51.0; this draft has **not** verified which of them
creates a new scope per attempt. **Never `MoveToErrorQueue` on the first
conflict.** After the ladder, the default dead-letter behaviour applies, as it does for any persistent
failure.

Latency budget: **N/A.** Retirement is control-plane work off every §IV leg.

## Consequences

**Positive: a retired camera cannot stay watchable because a sweep happened to save first.**

**Positive: ADR-0113's rule gets the boundary it always needed.** The prohibition is about re-applying a
*decision* to fresh state; a fixed terminal transition is not one, and the rule now says so instead of
leaving each handler to argue it.

**Negative: a carve-out invites widening.** "Idempotent and terminal" is easy to claim. The four conditions
and the requirement to name each write individually are the guard; a reviewer should refuse a policy that
cites this ADR without the showing.

**Negative: a verification the lane may find hard.** Forcing the race deterministically needs control over
the sweep's timing (spec 318's provision/retire test interposed in `AddPathAsync`); if the lane cannot force
it, it blocks rather than pinning blind.

## Alternatives Considered

**Leave the default unexamined.** Three attempts may all fail on a stale scope, and the outcome is a live
stream for a retired camera. Rejected by the owner.

**Copy spec 296's `MoveToErrorQueue`.** Deterministically produces the bad outcome. Rejected by the owner.

**Stop the health sweep from bumping the version** (e.g. not assigning `LastSuccessAt` when unchanged, or a
non-bumping write as #2743 did for the provision). Removes the race at its source, but changes what the
health record means and what every other concurrency check on the stream sees. A larger, separate change;
not taken here.

**Catch the conflict inside `RetireStreamCommandHandler` and reload.** Same effect, but hides a retry in
Application code where ADR-0113 says none exists; a scoped failure policy keeps it visible in
Infrastructure, beside the other two. Rejected.

## Implementation Notes

What the autonomous lane builds after approval. The owner noted step 1 could be its own issue; this ADR
does not require that split. **Step 1 is a precondition** (Decision 1): the lane runs it before anything
else and blocks if the race is not real or the per-attempt scope cannot be established.

1. **Verification (always, first).** `tests/Integration.Tests/StreamDistribution/RetireHealthRaceIntegrationTests.cs`
   (Aspire fixture; a new class needs a shard-filter entry): force a health save between the retire's load
   and save, then assert the stream ends `Retired`, the MediaMTX path is removed, and nothing reached the
   dead letter queue. Record the attempt count and whether each attempt used a new scope (e.g. by the
   loaded `Version` per attempt in a test-only log or a counting decorator). If it passes on unchanged code,
   it is a **characterisation** (green) and the issue closes with the observation quoted; if it fails, it is
   the **red** for step 2. If the test cannot force the conflict at all, or cannot tell whether attempts
   reload, neither branch applies: the lane blocks with the verbatim output.
2. **Pin (only if step 1 is red).** `src/StreamDistribution/Infrastructure/RetireStreamFailurePolicy.cs`,
   an `IHandlerPolicy` scoped to the `CameraRetiredV1` chain, registered through the context's
   `AddWolverineForContext(..., configureMore:)` hook as LayoutComposition does. If the chosen mechanism is
   `ScheduleRetry`, the listener needs `durableInboxMessageTypes` for `CameraRetiredV1`, as spec 328 found
   for Identity; the lane checks that against the pinned Wolverine version rather than assuming it.
3. **Record.** ADR-0113's header gains *"Amended by ADR-0182"* (done with this ADR); the policy's doc comment
   cites this ADR and states the four conditions it meets.
