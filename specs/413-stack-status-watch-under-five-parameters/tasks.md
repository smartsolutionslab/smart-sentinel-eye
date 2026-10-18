# Tasks 413: the stack-status watch loop under five parameters

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3028 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). Covering tests are
captured green on unchanged code (C0), and must pass with `git diff C0 -- tests/` empty after
T010. An assertion that would have to change is a **block**, not an edit (ADR-0144).

**Engineer:** infra-engineer (T010). **Test-writer:** T000a, T001. **Not security-sensitive**:
Phase 6 runs `/code-review` only.

Legend: `[P]` = owns files disjoint from other `[P]` tasks of its phase (ADR-0109). No foundational
task and no parallelism: one production file, one test file, strictly sequential.

## Phase 1: Baseline (architect; done)

- [x] **T000** On `00a66722`, AppHost Release `--no-incremental`: one Sonar hit under
  `src/AppHost`, `StackStatusReport.cs(201,49) S107` (6 params); 0 errors. T020 re-runs it and
  diffs the solution-relevant sorted-unique list.

## Phase 2: Characterisation, observed green (test-writer)

- [ ] **T000a** [US1] Spike A-1 (timebox 30 min). If feasible: create
  `tests/Integration.Tests/StackStatusReportWatchCharacterisationTests.cs` with plan §4.1 cases
  1-5, `FixtureLogic` trait, an inclusion entry appended to `ci-shards/shard-3.filter`; observe green
  on unchanged code; run CF1-CF3, each red, quoted verbatim. Commit
  `test(3028): characterise the stack-status watch loop before grouping its parameters (green)` =
  `C0`. If infeasible: commit nothing; return the spike's verbatim failure and switch to plan §4.2.
  Depends on T000.
- [ ] **T001** [US1] On C0 (or base for §4.2): run `--filter "Category=FixtureLogic"` on
  Integration.Tests and `AppHostExtractionBoundaryTests` + the AppHost text guards on
  Architecture.Tests; capture summary lines verbatim. For §4.2 also record the base-commit body
  hash and the base commit's e2e run URL with the four "Wait for the stack" steps green. Writes no
  file. Depends on T000a.

## Phase 3: The change (infra-engineer; one commit)

- [ ] **T010** [US1] `src/AppHost/StackStatusReport.cs` only (plan §2): add `StatusReportTarget`
  above `ResourceStatusLine`; reshape `WatchAndWriteAsync(notifications, report, logger,
  cancellationToken)` opening with `var (path, expectedNames, instances) = report;`; hoist
  `var report = new StatusReportTarget(path, expectedNames, instances);` in `Attach`. Commit
  `refactor(3028): the stack-status watch loop takes its report as one argument`. Depends on T001.

## Phase 4: Verify (orchestrator / test-writer)

- [ ] **T020** Plan §5: S107 diff (only the target removed), 0 errors, format clean, T001 commands
  re-run unmodified and green, `git diff C0 -- tests/` and `AppHost.cs` diff empty. For §4.2: tip
  body hash equals base; PR tip's e2e gate steps green (cite run). Depends on T010.
