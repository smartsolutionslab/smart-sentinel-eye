# Tasks: A probe timeout carries the resource snapshot

**Feature**: 430 | **Issue**: #3076 | **Spec**: `./spec.md` | **Plan**: `./plan.md`
**Branch**: `test/3076-fixture-probe-snapshot`
**Worktree**: `D:/Github/sse-3076` (cut from `origin/develop` at `6206ecf9`)

## The three declarations (ADR-0144)

1. **Engineer: `infra-engineer`.** `AspireFixture.cs` and its Docker-free tests;
   specs 062, 084, 098 and 424 changed the same file for the same reason and were
   `infra-engineer`. No bounded-context work.
2. **ADR: none.** Owner ruled "No ADR"; a diagnostic on an existing failure path
   applies ADR-0103/0068.
3. **Phase 4a colour: RED (behaviour-changing).** A failing probe's message gains
   the snapshot. Passing runs are unaffected, but that is what every
   failure-path change looks like; the observable behaviour under test is the
   message, and it changes. Red is shown Docker-free under
   `--filter "Category=FixtureLogic"` (CI's "Docker-free fixture logic tests"
   step). To make the reds *assertion* failures rather than compile errors,
   T001 lands the seam first (spec 424's / 084's precedent).

## Phase 1 — Seam (blocks everything)

- [ ] **T001 [US1]** `refactor(3076)`: add `ProbeExhaustedAsync` +
  `FormatProbeTimeoutMessage` to `AspireFixture.cs` **with no call site**, where
  `FormatProbeTimeoutMessage` returns `probeFailure` alone (today's message).
  Behaviour-preserving: `Category=FixtureLogic` green before and after, unmodified.

## Phase 2 — Red (test-writer; quote output verbatim)

- [ ] **T002 [US1]** `test(3076) (red)`: tests 1-4 of plan §Tests in
  `AspireFixtureResourceCaptureTests.cs`. Expected red on T001:
  1-3 fail — message lacks `Resource states:`;
  4 fails — `throw new TimeoutException(` count is 4, not 1, and
  `ProbeExhaustedAsync(` has 0 call sites, not 3.
  Depends on T001.

## Phase 3 — Green (infra-engineer; may not edit T002's tests)

- [ ] **T003 [US1]** `fix(3076)`: `FormatProbeTimeoutMessage` appends
  `Resource states:` + `FormatResourceStates(...)`; tests 1-3 green. Depends on T002.
- [ ] **T004 [US1]** `fix(3076)`: route `WaitForKeycloakRealmAsync`,
  `WaitForMediaMtxAsync`, `WaitForServiceHealthAsync` through
  `throw await ProbeExhaustedAsync(...)` with their sentences unchanged; test 4
  green. Depends on T003 (same file — **not [P]**).

## Phase 4 — Counterfactuals (record each observed red in the PR)

- [ ] **T005** Revert one probe site to `throw new TimeoutException(...)` → test 4 red.
- [ ] **T006** Drop the `FormatResourceStates` term from `FormatProbeTimeoutMessage` → tests 1-3 red.
- [ ] **T007** Swap `captured.Health` for `null` → test 1's health assertion and test 2 red.
- [ ] **T008** Make `FormatProbeTimeoutMessage` put the snapshot *before* the probe
  sentence → test 1's `ShouldStartWith(probe sentence)` red (guards spec A3's order).

## Phase 5 — Verify

- [ ] **T009** `Category=FixtureLogic` green in Release; `dotnet format --verify-no-changes`;
  CI's Docker integration job green (discharges "passing runs unaffected").
  Live boot to provoke a real probe exhaustion is **not** required and not
  possible locally (Docker down) — say so in the PR's verification note.

No `[P]` tasks: every change is in one file pair, in sequence.
