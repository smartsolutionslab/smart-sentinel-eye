# Tasks 410: the health sweep holds a path's first absence for one tick

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3021 (feature-level;
already on Project #13; no per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T001 writes the new test file on unchanged code
(`f8d67354`) and runs it. Scenarios 1, 2, 3, 5 must fail with the held camera dispatched as
`"path not registered"`; scenarios 4, 6, 7 are guards and pass. A red scenario that arrives green is
a phase-4 failure: stop and report. The output is quoted verbatim in the PR.

**Roles:** test-writer T001 (writes the one new file, runs, returns verbatim output).
**backend-engineer** T010-T011 (may not edit any file under `tests/`). Phase 5: T030. Phase 6:
`/code-review` (backend-reviewer). No security review (spec header).

**Forbidden in every task:** editing any existing file under `tests/`; editing `MediaMtxRtspGateway.cs`,
`IRtspGateway.cs`, `Log.cs` (either), the handler, the domain; changing `MaxListPages`, `PollInterval`,
`OfflineAfter`; adding a MediaMTX call; a deadline or linked CTS; a new log event; any suppression;
ADRs, the constitution, CLAUDE.md. Items 2, 3, 4 and FR-008 are owner questions (spec §8).

Legend: `[P]` = owns disjoint files (ADR-0109). **No foundational task.** One production file and
one test file, strictly sequential (red must exist before the fix). Nothing parallelises usefully.

**Test filter (T001, T020):**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~HealthWatcher|FullyQualifiedName~MediaMtxRtspGatewayPaginationTests|FullyQualifiedName~MediaMtxRtspGatewayListReaders|FullyQualifiedName~MediaMtxRtspGatewayHealthReadCharacterisationTests"
```

## Phase 4a: red (test-writer, on `f8d67354`)

- [ ] **T001 [US1]** Create
  `tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherAbsenceHoldTests.cs`.
  Mirror (do not reference or edit) the stub gateway / capturing handler of
  `StreamHealthWatcherScopeTests` and the `PagesHandler` + real `MediaMtxRtspGateway` of
  `StreamHealthWatcherCutShortReadTests`. Every scenario drives **one** watcher instance through
  successive `DispatchAsync` calls and asserts per sweep which cameras were dispatched and with what
  `LastError`. Scenarios, numbered as spec §3:
  1. real gateway: sweep 1 pages 0-1 list A, B, C, D; sweep 2 page 0 lists A, B and page 1 lists D
     (C shifted out). Assert C not dispatched on sweep 2; A, B, D dispatched Healthy. **Red.**
  2. B present, absent, absent → not dispatched on sweep 2, `"path not registered"` on sweep 3. **Red.**
  3. B present, absent, present → no `"path not registered"` for B on any sweep. **Red.**
  4. C never present, absent on sweep 1 → `"path not registered"` on sweep 1. **Guard.**
  5. B present; sweep 2 throws `HttpRequestException`; sweep 3 B absent → not dispatched; sweep 4
     absent → `"path not registered"`. **Red.**
  6. read throws → nothing dispatched, exactly one `HealthSweepFailed` Warning. **Guard.**
  7. 250 cameras, one held → one batch call, zero `GetPathHealthAsync`. **Guard.**
  Run the filter above; return the verbatim output. Every assertion must be able to fail
  independently of its input (counterfactual: a watcher that dispatches every absence fails 1, 2, 3,
  5; a watcher that never dispatches an absence fails 4).

## Phase 4b: implementation (backend-engineer)

- [ ] **T010 [US1]** (depends T001) In `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`
  add the last-successful-sweep set and the hold, exactly as plan §3: skip before
  `ShouldDeclareOffline` and before the per-camera scope; replace the set with `observed.Keys` after
  the loop; leave the `HealthSweepFailed` branch untouched so a failed tick keeps the set. Extract the
  decision so `DispatchAsync` does not grow (ADR-0084, advisory). One *why* comment at the skip.
- [ ] **T011 [US1]** (depends T010) Add one sentence on the hold to the class summary. Comment-only.
  Separate commit (ADR-0030: `docs(3021): ...`), so T010's commit is the fix alone.

## Phase 4c: green

- [ ] **T020** (depends T011) Run the T001 filter: all green, no `tests/` file modified
  (`git diff --stat f8d67354 -- tests` shows only the new file). Then the full
  `StreamDistribution.Infrastructure.Tests`, `StreamDistribution.Application.Tests`,
  `StreamDistribution.Domain.Tests` and `Architecture.Tests` in Release, and the StreamDistribution
  integration classes (`--filter "FullyQualifiedName~Integration.Tests.StreamDistribution"`) on the
  Aspire fixture. `dotnet format --verify-no-changes` on the touched projects. Each commit builds on
  its own (ADR-0087).

## Phase 5: verify

- [ ] **T030** (depends T020) Live stack (one Aspire stack on the machine; stop it before building).
  Provision two simulator cameras, wait for both Healthy. Delete one camera's path with the MediaMTX
  control API (`/v3/config/paths/delete/{path}`). Observe its Degraded `StreamHealthChangedV1`
  arrives (not lost), and that its timestamp is at least one poll interval (2 s) after the delete,
  while the control camera stays Healthy. Record both timestamps in `verification.md`. State that the
  mid-sweep race itself is covered by T001 scenario 1, not by the live run (spec A3).

## Phase 6-7

- [ ] **T040** `/code-review` (backend-reviewer) on the diff; findings resolved or accepted in writing.
- [ ] **T050** PR to `develop` (`--base develop`), body quotes T001's red output and T020's green,
  lists spec §8 Q1-Q4 as left with the owner, and says #3021 stays open for items 2-4 (no closing
  keyword).
