# Tasks 274 — The success that answered 500

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2490 · **Phase**: 3 (Tasks)
**Board**: feature-level issue **#2490** is the one to add to Project #13 (no per-task issues — CLAUDE.md
Phase 3, specs 029+).
**Parallelism**: **none.** One production file and one test file, in a strict red→green order (ADR-0109:
nothing disjoint to fan out). No foundational Shared.Kernel / Contracts / AppHost task.
**Stack**: not needed — unit tests with a faked store.

Format: `[ID] [P?] [Story] description — files — depends on`. Each file-changing task is one commit
(ADR-0030) that builds on its own.

## Phase 4a — `test-writer` (tests only; returns verbatim output)

- [ ] **T001** [US1] Characterisation baseline: run
  `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~IdempotentRequestTests"` on the
  untouched tree; record the verbatim summary (all existing facts green). — no file change — depends on:
  none.
- [ ] **T002** [US1] Add `CompleteThrows` to `RecordingStore` and the four facts of spec §7 (plan §3.2).
  Run the filter; return verbatim output: the four new facts red (quote each failure), every existing fact
  green. A new fact arriving green, or an existing fact going red, stops the phase. Commit:
  `test(service-defaults): …`. — `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` —
  depends on: T001.

## Phase 4b — `backend-engineer` (receives T002's output; may not edit tests)

- [ ] **T003** [US1] Add `RecordQuietlyAsync` and call it from `RunAndRecordAsync` (plan §3.1): catch,
  `Activity.Current?.AddException`, no retry, no release on a failed complete, FR-005 why-comment. Commit:
  `fix(service-defaults): …`. — `src/ServiceDefaults/Idempotency/IdempotentRequest.cs` — depends on:
  T002.
- [ ] **T004** [US1] Verify: filter green (all facts); counterfactuals of spec §8 (remove the catch → facts
  1–3 red; add a release in the catch → fact 4 red; restore, `git diff` empty); `dotnet format
  --verify-no-changes` and Release build clean. Diff T002→T003 touches no test file. — no file change —
  depends on: T003.

## Phase 5–7

- [ ] **T005** [US1] PR to `develop` (`--base develop`): T002's red quoted verbatim, T004 results, spec
  §6's resolved ambiguity (Activity vs `ILogger`) and §9's residual risk stated; `Closes #2490`.
  — depends on: T004.

## Dependency chain

T001 → T002 → T003 → T004 → T005 (strictly serial).
