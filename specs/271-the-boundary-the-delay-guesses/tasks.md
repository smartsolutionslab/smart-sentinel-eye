# Tasks 271 — The boundary the delay guesses

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2537 · **Phase**: 3 (Tasks)
**Board**: feature-level issue #2537 on Project #13 (no per-task issues).
**Parallelism**: **none.** Every task edits the one file
`tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs` (ADR-0109: shared file ⇒
serial). No foundational Shared.Kernel / Contracts / AppHost task exists.
**Stack**: T001, T005 and T006 need the Aspire stack. One machine, one stack — ask the user to stop any
running AppHost first.

Format: `[ID] [P?] [Story] description — files — depends on`.

## Phase 4a — `test-writer` (tests only; returns verbatim output)

- [ ] **T001** [US1] Capture the baseline: run
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~WhepAuthorizeRateLimitTests"` on the
  untouched tree; record the verbatim summary (6 facts; `Authorize_partitions…` now lives in
  Architecture.Tests). — no file change — depends on: none.
- [ ] **T002** [US1] Introduce the seam and restructure the existing facts onto it, **today's semantics
  preserved** (plan §3.1 4a, §3.2, §3.3): `BeginCountingAsync()` returning `PermitLimit`;
  `ExhaustWindowAsync(int budget)`; `IAsyncLifetime` with `DisposeAsync` = today's
  `WaitUntilAdmittedAgainAsync` (delay kept); per-fact recovery calls removed; FR-002 reorder in
  `A_partition…`, `A_throttled…`, `Repeated…`; FR-004 status assertions (admitted-expected not `429`,
  sentinel `403`, `Repeated…`'s five repeats `429`); FR-005 400-line tail on sentinel failures. No
  expected status or count changes (FR-006). — `WhepAuthorizeRateLimitTests.cs` — depends on: T001.
- [ ] **T003** [US1] Add the adversarial fact
  `A_fact_that_starts_just_before_a_window_boundary_still_counts_one_whole_window` (spec §7, plan §3.2
  last row). — `WhepAuthorizeRateLimitTests.cs` — depends on: T002.
- [ ] **T004** [US1] Run the class filter **three times**; return verbatim output. Expected: the six
  restructured facts green (characterisation — if any is red, stop: T002 moved behaviour); the
  adversarial fact red by straddle ("request N … 429" or "request 51 … 403"). Record every run's
  outcome, including any green adversarial run. — no file change — depends on: T003.

## Phase 4b — `backend-engineer` (receives T004's output; may not edit fact bodies or assertions)

- [ ] **T005** [US1] Implement the seam (plan §3.1 4b): exhaust-until-`429` (bounded), 50 ms
  `AlignmentPollInterval` poll until admitted, `return PermitLimit - 1`. Remove the trailing
  `Task.Delay(Window)` from `WaitUntilAdmittedAgainAsync` (FR-003). Rewrite the class summary and the S9
  comment (FR-008). — `WhepAuthorizeRateLimitTests.cs` — depends on: T004.
- [ ] **T006** [US1] Verify: class filter green three times; then with
  `WhepAuthIntegrationTests|WhepHandshakeLatencyTests` added, green once; counterfactual (seam back to
  `return PermitLimit;` → adversarial fact red, six green; restore; `git diff` empty); record class wall
  time. Diff T004→T005 shows no change inside any `[Fact]` body. — no file change — depends on: T005.

## Phase 5–7

- [ ] **T007** [US1] PR body: spec §1.1 table, the T004 red quoted verbatim, T006 results, SC-3 stated as
  a prediction; closing keyword `Closes #2537`. — depends on: T006.

## Dependency chain

T001 → T002 → T003 → T004 → T005 → T006 → T007 (strictly serial; single file).
