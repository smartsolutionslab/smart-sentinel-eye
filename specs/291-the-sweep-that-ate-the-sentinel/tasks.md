# Tasks: Spec 291, the sweep that ate the sentinel

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2656 · **Lane:** autonomous (ADR-0144)

**Phase-4a colour:** **SPLIT.** T004 is **red** (observed failing on the ring-backed skeleton;
failure quoted in the PR body). T002-T003 and T005 are **characterisation** (observed green
before, must pass with predicates **unmodified** after). Ambiguity resolves to red — there is
none: the split follows plan §4 fact by fact.

**Engineers:** `test-writer` (4a: T001-T006), then `backend-engineer` (4b: T007-T010).
No production C#; every file is under `tests/Integration.Tests`.

One story (US1), so every task is `[US1]`. `[P]` marks tasks owning disjoint files with no
ordering dependency on their neighbour (ADR-0109).

**Board.** Issue #2656 is on Project #13 (`agent:ready`, verified 2026-09-28).

**Contention file** (ADR-0109): `tests/Integration.Tests/Fixtures/AspireFixture.cs`. Check no
parked PR edits it before opening the PR.

**Foundational:** T001 (extraction + skeleton API) blocks T004 and all of 4b. 4b is blocked by
the whole of 4a.

---

## Phase 4a: tests first (`test-writer`)

### Characterisation baseline (behaviour-preserving)

- [ ] **T001 [US1]** Plan D1/D2 skeleton. In `tests/Integration.Tests/Fixtures/AspireFixture.cs`,
  extract `TailResourceLogsAsync`'s per-line enqueue-and-trim into
  `internal void RecordLogLine(string resourceName, string content)` in the new partial
  `tests/Integration.Tests/Fixtures/AspireFixture.LogCapture.cs`; the loop calls it. Add
  `LogCapture` (`tests/Integration.Tests/Fixtures/LogCapture.cs`) and
  `public LogCapture CaptureLogs(string resourceName)` with **ring-backed** semantics:
  `Lines` / `Contains` read `RecentLogs(resourceName, lines: 400)` on demand; FR-004's throw for
  an untailed name. `400` and `RecentLogs` unchanged.
- [ ] **T002 [US1]** Run `LogTailDeliversIntegrationTests` (three existing facts) on T001's tree.
  Record verbatim: all green. Characterises the extraction.
  - **Depends on:** T001.
- [ ] **T003 [P] [US1]** Run `WhepAuthorizeRateLimitTests` (all seven facts) on the **unedited**
  test file. Record verbatim: all green. This is the characterisation baseline for T008; note in
  the record that an isolated run lacks the 40-stream noise (plan §4).
  - Disjoint from T002 (read-only run); may run in the same boot.

### Red (behaviour-changing)

- [ ] **T004 [US1]** Add `A_capture_keeps_a_line_the_tail_has_already_evicted` to
  `tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs` per plan D3: real
  delivery of an invented camera name into the capture, 401 labelled synthetic lines via
  `RecordLogLine`, counterfactual (`RecentLogs(…, lines: 400)` lacks the token) asserted
  **before** `capture.Contains(token)`. Class doc comment gains one paragraph for the fact.
  - **Depends on:** T001.
- [ ] **T005 [US1]** Run the four `LogTailDelivers…` facts. Record verbatim: the three existing
  green; T004 **red at the `capture.Contains` assertion** (not at the counterfactual, not at
  delivery). A red anywhere else is a phase-4a failure — report, do not adjust.
  - **Depends on:** T004.
- [ ] **T006 [US1]** Commit 1 (plan §5): `test(integration): red a log capture that keeps lines
  the tail evicted`. Builds on its own; `dotnet build -c Release` clean.
  - **Depends on:** T002, T003, T005.

## Phase 4b: the capture and its consumer (`backend-engineer`)

- [ ] **T007 [US1]** Re-run T005 to confirm the red brief. Replace the skeleton with plan D1's
  storage: `LogCapture` holds a `ConcurrentQueue<string>`; `RecordLogLine` fans each line out to
  the resource's active captures after updating the ring; `Dispose` unregisters. `RecordLogLine`
  must not be able to throw. T004 green; the three existing facts green **unmodified**
  (`git diff <commit1> -- LogTailDeliversIntegrationTests.cs` empty). Commit 2:
  `fix(integration): keep every line a log capture was open for`.
  - **Depends on:** T006.
- [ ] **T008 [US1]** Plan D4 in `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`:
  a capture per log-reading fact (after `BeginCounting…`, before the sentinel); the three log
  helpers take the capture; `CapturedEvidence(...)` replaces every `RecentLogs` dump; ring-describing
  doc comments rewritten. Every assertion predicate and expected value, the timings, the request
  sequence and `ThrottleTransitionMarkers` byte-identical.
  - **Depends on:** T007.
- [ ] **T009 [US1]** Run `WhepAuthorizeRateLimitTests` (seven facts) and `LogTailDelivers…`
  (four). All green. SC-4: `grep -n RecentLogs` on the Whep file returns nothing. SC-5:
  `git diff <commit2> -- WhepAuthorizeRateLimitTests.cs` shows no predicate or expected-value
  change. `dotnet build -c Release` clean. Commit 3:
  `fix(integration): read the WHEP rate-limit log evidence from a capture`.
  - **Depends on:** T008.
- [ ] **T010 [US1]** Each of commits 1-3 builds on its own (ADR-0087): check out each and build.
  - **Depends on:** T009.

## Phase 5 (`/verify`)

- [ ] **T011 [US1]** SC-2 and SC-3 per plan §7 — the deterministic reproduction of #2656 and
  #2654's signatures on the pre-fix helpers, their absence on the fixed helpers, the false-pass
  counterfactual, and the FR-009 mutation. Uncommitted; outputs verbatim in the verification
  note; patch reverted (`git status` clean).
- [ ] **T012 [US1]** After push: read all four integration shards (SC-6). Shard 1 is the one that
  carries the real 40-stream noise.

## PR body must carry

- The verbatim red (T005), characterisation-green (T002, T003) and SC-2/SC-3 output.
- `Latency: N/A — test fixture and test file only, no leg touched.`
- The root-cause table from spec §1.2 (the 400-line tail is one 50 ms health sweep).
- **Left for a human / follow-up issue:** stream-distribution's production log volume from
  `StreamHealthWatcher` + `IRtspGateway` HTTP-client and Polly `Information` logging (spec §1.5,
  ADR-0118); optionally extending `LogTailCoverageTests` to `CaptureLogs(...)` (plan D5).
- `Closes #2656`.
