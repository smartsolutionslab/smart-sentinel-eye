# Plan 402: A 404 on the source-repair PATCH is "path gone" only when the database agrees

**Spec**: [spec.md](./spec.md). **Issue**: #3013 (item 1 only).

## 1. Context and layers

- **Bounded context**: StreamDistribution. **Layer**: Infrastructure only
  (`src/StreamDistribution/Infrastructure/Reconciler/`). No Domain, Application, Api,
  `Shared.Contracts`, AppHost or other-context change.
- **Files that change**: `MediaMtxSourceRepair.cs`, `SourceRepairFailureLog.cs`. Nothing else in
  `src/`.
- **Files that must not change**: `IStreamSourceSnapshot.cs`, `StreamSourceSnapshot.cs`,
  `IRtspGateway.cs`, `MediaMtxRtspGateway.cs`, `MediaMtxSourceRepairSweep.cs`,
  `StreamDistributionInfrastructureModule.cs`, every `Log.cs` template, the sweep/startup wiring.

## 2. Types and invariants

No new types. `MediaMtxPath`, `StreamSourceUrl`, `Option<StreamSourceUrl>` (ADR-0141) are reused.

Invariants (from spec 388, kept):
- `SourceRepairFailureLog` is owned by one `MediaMtxSourceRepair`, sequential, not thread-safe, no
  `static` mutable state.
- After `PassCompleted` the state holds only paths that failed (not "gone") in that pass.

New invariant: **"path gone" requires the database's confirmation.** A 404 clears state only when
`ReadCommittedSourceAsync` returned None in the same pass, after the 404.

## 3. Design

### 3.1 `SourceRepairFailureLog`

- `Failed(path, exception)` loses its 404 special case (`SourceRepairFailureLog.cs:24-29`): every
  exception it receives, a 404 included, goes through the FR-002 outcome logic.
- New `Gone(path)`: `logger.ReconcilerRepairPathGone(path); failures.Remove(path);` — the body of
  the removed branch, unchanged. Does not touch `failedThisPass`.

The 404 decision moves out of the log because it now needs an async database read and the log is a
synchronous, I/O-free recorder.

### 3.2 `MediaMtxSourceRepair.TryRepairPathAsync`

The existing `catch (Exception ex) when (!cancellationToken.IsCancellationRequested)` at
`MediaMtxSourceRepair.cs:96` calls a new private method instead of `failureLog.Failed` directly:

```
RecordFailureAsync(snapshot, path, ex, ct):
  if ex is not HttpRequestException { StatusCode: NotFound }  -> failureLog.Failed(path, ex); return
  try   committed = await snapshot.ReadCommittedSourceAsync(path, ct)
  catch (Exception readFailure) when (!ct.IsCancellationRequested)
        -> failureLog.Failed(path, readFailure); return          // spec FR-003, [ASSUMPTION A1]
  if committed.HasValue -> failureLog.Failed(path, ex)            // persistent 404: Warn/re-Warn
  else                  -> failureLog.Gone(path)                  // retire race
```

The shape is a sketch for the engineer, not prescribed code. Constraints it must meet:
- one confirmation read per 404, never for any other failure (FR-002);
- a read inside a `catch` block is not covered by the outer `catch`, so the read's own failure must
  be handled explicitly (FR-003) or it escapes `RepairAsync` and fails the whole pass;
- cancellation is never swallowed (FR-004);
- the method stays inside ADR-0084's advisory limits (≤ 30 LOC, ≤ 4 parameters, depth ≤ 3); a
  `readonly record struct` is not needed for four arguments;
- `Ensure.That(...)` only where the file already guards (private methods take already-guarded
  inputs; no new guards needed).

No retry is added, the patch count per path per pass stays ≤ 2, and the successful-path flow
(`MediaMtxSourceRepair.cs:76-95`) is untouched.

## 4. Messaging

None. No domain or integration event; no `Shared.Contracts` change.

## 5. Boundary rules

Infrastructure-internal. No cross-context reference; NetArchTest and
`StreamDistributionRegistrationCharacterisationTests` must stay green unmodified (no registration
changes). `PrimitiveBoundaryTests` unaffected (no domain model change).

## 6. Existing assertions this change contradicts

Every existing assertion over a 404 on the repair PATCH was found by
`grep -rn "NotFound\|PathGone\|404\|gone" tests/StreamDistribution.Infrastructure.Tests tests/Integration.Tests/StreamDistribution`
and by reading every `CommittedReads` assertion. Each is classified:

| # | Test (file:line) | Assertion | Under this change | Retire justified? |
|---|---|---|---|---|
| C1 | `MediaMtxSourceRepairAdversarialTests.A_retire_that_removes_the_path_before_our_patch_is_skipped_and_the_pass_continues` (`tests/StreamDistribution.Infrastructure.Tests/Reconciler/MediaMtxSourceRepairAdversarialTests.cs:49`) | `snapshot.CommittedReads.ShouldNotContain(PathA, "a refused patch is not followed by a re-read")` | **Fails**: the confirmation read *is* the mitigation | **Yes.** The issue's own decision is "one DB read, only on the 404 branch". Replace with `snapshot.CommittedReads.Count(read => read == PathA).ShouldBe(1, "a 404 is confirmed by exactly one read of that path")` (not `ShouldBe([PathA])`: PathB's post-patch re-read also lands in `CommittedReads`, so that could never pass). The other assertions in this test (`repaired == 1`, PathB repaired, no add, no remove; lines 44-48) hold unchanged |
| C2 | `MediaMtxSourceRepairFloodAdversarialTests.A_404_on_the_re_patch_is_path_gone_at_debug_and_never_a_warning` (`…/MediaMtxSourceRepairFloodAdversarialTests.cs:87`; asserts at :105 no entry ≥ Warning, :106-108 one Debug "path gone") | Its arrangement keeps the stream **live** (`CommittedReadOverride` returns `Some(newer)` on every read, :91) | **Fails** at :105 and :108: a live-stream 404 now Warns | **Yes**, it pins "every 404 is gone", which is exactly the behaviour #3013 retires and which spec 388's owner decision never asked for (it said "a 404 *from a retire race*"). Change only the arrangement so the stream is retired after the re-patch: override `(_, read) => read == 1 ? Some(newer) : None`. Assertions :103-108 then hold unchanged. Its live-stream counterpart becomes new test R2 |
| C3 | `MediaMtxSourceRepairFloodAdversarialTests.A_404_clears_a_warned_path_so_its_next_refusal_warns_again` (`…:112`, assert `RepairWarnings().ShouldBe(2)` at :125) | Path stays live through the 404 pass | **Fails**: 400 → 404 (live) → 400 is three outcome changes, 3 Warnings | **Yes**, same reason as C2. Change only the arrangement: retire the path for the 404 pass (`snapshot.Retire(path)` in a `BeforePatchApplied` hook) and `snapshot.Commit(path, Persisted)` before the third pass. `ShouldBe(2)` then holds unchanged and still pins its title. The live-stream sequence becomes new test R4 |

**Guards that must pass unmodified** (they pin the half of FR-003 this change keeps):
- `MediaMtxSourceRepairLogVolumeTests.A_404_from_a_retire_race_is_path_gone_at_debug_never_a_warning`
  (`…/MediaMtxSourceRepairLogVolumeTests.cs:105`): retires in `BeforePatchApplied`, so the read sees
  None. **Unchanged.**
- `MediaMtxSourceRepairAdversarialTests.A_source_that_keeps_moving_never_gets_more_than_two_patches_even_when_the_last_re_read_is_retired`
  (`:125`, `CommittedReads.Count.ShouldBe(1)` at :136): no 404 occurs, so no extra read. **Unchanged.**
- `MediaMtxSourceRepairRaceTests.A_path_never_receives_more_than_two_patches_in_one_pass_however_often_the_source_changes`
  (`CommittedReads.Count.ShouldBeLessThanOrEqualTo(1)` at `MediaMtxSourceRepairRaceTests.cs:133`):
  no 404. **Unchanged.**
- Every row of spec 388 plan §8.1 (non-404 failures, cancellation, redaction, sweep, startup) and
  `MediaMtxSourceRepairLogVolumeIntegrationTests` (a 400 refusal): **unchanged**.

C1-C3 are the **only** permitted edits to existing tests. C2 and C3 edit arrangement lines only;
C1 replaces one assertion. Each is named in the PR body with this table's reason. Any other existing
assertion that has to change is a **BLOCK**, not an adjustment.

## 7. Tests (phase 4a: RED)

New tests R1-R7 (unit, `StreamDistribution.Infrastructure.Tests`, no Docker) and R8 (integration,
Aspire fixture) are specified in tasks.md. All except the guard R6 must be observed red against
unchanged production code; R6 is a declared guard that passes today.

## 8. Risks

| Risk | Mitigation |
|---|---|
| The read in the 404 branch throws and fails the whole pass | FR-003, test R5 |
| A Warning burst on upgrade (N diverged paths × one Warning) | Intended: it is the first-failure rule of spec 388 §6, and the very signal #3013 asks for; then throttled to one per 10 min per path |
| Extra DB load | One indexed single-row read per 404 per path per pass; zero on every other branch (R6) |
