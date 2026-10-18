# Plan 410: The health sweep holds a path's first absence for one tick

**Spec**: [spec.md](./spec.md). **Issue**: #3021 item 5 (items 2-4 excluded, spec §8).
**Baseline**: `f8d67354`.

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on a domain model | not touched: no Domain change. The new state is an Infrastructure field of `MediaMtxPath` values. |
| Boundaries (no cross-context refs) | not touched: one Infrastructure file in StreamDistribution. |
| §IV latency | N/A (spec §6). |
| §Testing / ADR-0139 | behaviour-changing, red first (tasks T001). |
| ADR-0144 lane limits | no ADR, no constitution change, no gate weakened, no existing test edited. |
| ADR-0171 rate budget | no extra MediaMTX call; the existing "one batch call for 250 cameras" test stays green unmodified. |
| ADR-0084 metrics (advisory) | `DispatchAsync` is about 34 statement lines today. The hold adds a branch to the loop; extract it (§3) so the method does not grow. |
| ADR-0141 `Option<T>` | no new parameter. |

No new ADR is needed. The choice of mechanism (§2) is a plan-level choice inside the watcher's
existing responsibility, with no budget cost; it is recorded here so the owner can overrule it.

## 2. Mechanism: options considered

| Option | Closes remove-only skip | Closes remove + add | Extra MediaMTX calls | Verdict |
|---|---|---|---|---|
| A. Watcher holds a first absence one tick (chosen) | yes | yes | none | smallest; no budget, no gateway change |
| B. Gateway re-reads when `itemCount` drops between pages | yes | **no** (count unchanged) | one extra read per churn | incomplete; touches `ReadPagesAsync` (spec 409's file) |
| C. Watcher confirms each absence with `GetPathHealthAsync` | yes | yes | one per absent stream per tick: 50 broken cameras = 1,500/min against the 2,000/min `api` partition | an operational-budget change: excluded |
| D. Gateway throws when `itemCount` changes | partly | no | none, but the tick is lost and logs `HealthSweepFailed` | turns a retire into a Warning; #3013 territory |
| E. Build FR-008's 10 s sustain | yes | yes | none | a locked-spec behaviour decision: owner Q4 |

## 3. Design

**File:** `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`.

- New field beside `degradedSince`: the set of paths present in the last successful health read.
  Declared with an explicit type and a collection expression (`HashSet<MediaMtxPath> ... = [];`),
  no underscore. `MediaMtxPath` is a record value object, so set membership is by value.
- In `DispatchAsync`, after `observed` is assigned (the `HealthSweepFailed` early return keeps the
  set unchanged, spec §3 "failed tick"):
  - capture the previous set and **immediately** replace the field with `observed.Keys`, **before**
    the per-camera loop (as built: `StreamHealthWatcher.cs` L123-124). The comparison is still
    against the previous sweep, because the loop reads the captured local, not the field;
  - in the loop, a stream whose path is absent from `observed` and present in the previous set is
    skipped (`continue`) before `ShouldDeclareOffline`, before the per-camera scope is created.

  **Why before, not after (corrected at phase 4).** This plan first said "replace after the loop".
  That is wrong: a handler that throws for one camera escapes `DispatchAsync`, so an
  after-the-loop assignment would never run on a tick that throws. A camera whose handler throws on
  every tick would then freeze the set forever, and a genuinely removed path that was in the frozen
  set would be held on every tick and never reported. Replacing before the loop matches spec §3's
  definition ("the most recent tick whose `GetAllPathsHealthAsync` returned"): the read returning is
  what updates the set, not the dispatch completing. Pinned by
  `StreamHealthWatcherAbsenceHoldInvariantTests.A_handler_that_throws_for_a_later_camera_does_not_stop_a_held_camera_being_reported_on_its_second_absent_sweep`
  (`bd795ab3`).
- The present/absent/held decision is a small private static method (or local function) so the loop
  body does not grow; the existing absent mapping to `"path not registered"` stays where it is.
  As built: `IsHeldAbsence`, plus the per-camera scope and handler call extracted into
  `ReportInOwnScopeAsync`, which keeps `DispatchAsync` under S138 (ADR-0084, advisory).
- The set records what MediaMTX returned (`observed.Keys`), not what was dispatched. A held path is
  absent from `observed`, so it is not in the next set, so a second absence is reported. That is the
  whole rule; no counters.
- Comment the *why* once at the skip: offset pagination shifts names under a concurrent removal
  (#3021 item 5's mechanism), so a single absence of a path just seen is not evidence. No issue
  numbers in code (CLAUDE.md, ADR-0036).
- The class summary gains one sentence on the hold; the `IRtspGateway.GetAllPathsHealthAsync` doc
  comment is not edited.

**Not changed:** `PollOnceAsync`, the Retired filter, `ShouldDeclareOffline`, the handler, the
domain, the gateway, `Log.cs`, DI.

**Concurrency.** `ExecuteAsync` runs ticks sequentially; `DispatchAsync` is never re-entered. The
field needs no lock, the same as `degradedSince`.

## 4. Messaging

No new message. The effect is the **absence** of one `Stream.ReportDegraded` → domain transition →
`StreamHealthChangedV1` (Shared.Contracts, via the outbox) pair per skip, and of its reverting pair
on the next tick.

## 5. Tests (owned by test-writer, T001)

One new file, `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldTests.cs`.
Two harnesses, both already in the folder, mirrored rather than shared (existing files are not
edited):

- **Stub gateway** (as `StreamHealthWatcherScopeTests`): a sequence of `GetAllPathsHealthAsync`
  answers or a throw, one per sweep, on **one** watcher instance, with the capturing handler
  recording which cameras were dispatched with which observation per sweep. Covers spec §3
  scenarios 2-7.
- **Real gateway over a stub `HttpMessageHandler`** (as `StreamHealthWatcherCutShortReadTests`): two
  sweeps; on the second, page 1's body omits the path that a removal would shift onto page 0.
  Covers scenario 1 end-to-end through `ReadPagesAsync`. The stub ignores `itemsPerPage`, so small
  pages with `pageCount: 2` are enough.

Red on `f8d67354`: scenarios 1, 2, 3 and 5 (the held camera is dispatched as `"path not
registered"`). Green on `f8d67354` and kept as guards: scenarios 4, 6, 7. Spec §Testing allows
guards in a red file; the PR quotes which is which.

Regression net run unmodified at T020: every class under `HealthWatcher/`, plus
`MediaMtxRtspGatewayPaginationTests`, `MediaMtxRtspGatewayListReaders*`,
`MediaMtxRtspGatewayHealthReadCharacterisationTests`, and the StreamDistribution Application/Domain
health tests.

## 6. Risks

| Risk | Mitigation |
|---|---|
| An integration test deletes a MediaMTX path and expects Degraded within a tight bound | searched `tests/Integration.Tests`: path deletes appear in reconciler and source-repair tests, which do not wait on the watcher's Degraded; `ProvisionRetireRaceIntegrationTests` seeds rows already Degraded with `"path not registered"` and never present, so the hold never applies. T020 runs the StreamDistribution integration classes to confirm. |
| Spec 409 edits `DispatchAsync`'s catch | rebase; no semantic overlap (spec §9). |
| The owner prefers option E (FR-008) | then this hold is subsumed and can be removed in that spec. |
