# Tasks 335: Running is not listening

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2777

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2777 carries `agent:ready`.
- **Engineer:** `backend-engineer` only (4b: the `AspireFixture.cs` edit). No frontend, no infra.
- **New ADR:** no — decision already made by the user on the issue (2026-10-08).
- **Phase 4a colour: CHARACTERISATION (green).** No product code changes, no fact's assertion
  changes anywhere. A test that had to be edited to pass would be evidence this moved behaviour —
  block, don't adjust.
- **Latency:** N/A.
- **Board:** #2777 on Project #13 (In Progress). No per-task issues (spec 028+ convention).
- **Granularity:** one PR; the fixture edit as one commit, docs as another; each builds on its own.

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — characterise (test-writer role, folded into this pass per plan.md §3)

- [x] **T001** Confirm `AbsentDeviceIdentifierIsRefusedIntegrationTests.cs` and
      `RegisteredClientConcurrencyIntegrationTests.cs` both still have the shape the issue
      describes, on this tree, before any edit (spec.md §1). No test file changes.

## Phase 4b — the edit (backend-engineer)

- [ ] **T002** `tests/Integration.Tests/Fixtures/AspireFixture.cs`: add
      `WaitForServiceHealthAsync("identity", cts.Token)` after the existing `identity` → `Running`
      wait, per plan.md §2. No other line changes.

## Phase 4c / 5 — verify

- [ ] **T003** `dotnet build -c Release` on `tests/Integration.Tests` clean. `git diff` shows only
      the one fixture addition (plus this spec/plan/tasks). Local live-boot verification deferred
      to CI per plan.md §3 (known orphaned-container state on this machine, concurrent sibling
      pipelines). Record the CI result once observed.
