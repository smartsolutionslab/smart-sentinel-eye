# Tasks 312: The underscore the tests kept

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2556

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2556 carries `agent:ready`, scope narrowed on the issue to the
  rename sweep. Enforcement is **#2764** — no `.editorconfig` / analyzer edits here.
- **Engineers:** `test-writer` (4a: baseline capture only, writes no test) → `backend-engineer`
  (4b: the rename). No frontend, no infra.
- **New ADR:** no.
- **Phase 4a colour: CHARACTERISATION (green).** Behaviour-preserving pure rename. No test
  assertion may be edited; an assertion that needs editing means behaviour moved — block.
- **Latency:** N/A.
- **Board:** #2556 is on Project #13 (Todo, verified 2026-10-07). No per-task issues.
- **Granularity:** one PR, three code commits (plan §2). Each commit builds on its own.
- **Follow-up to record (not fix):** `src/Automation/Infrastructure/Cache/InMemoryRuleCache.cs:33`
  `_byTrigger` — comment on #2764 (spec §1.3).

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — characterise (test-writer). Writes nothing; quote output verbatim.

- [ ] **T001** [US1] On the branch **before any rename**, `dotnet build -c Release` the solution,
      then `dotnet test -c Release --no-build` each of the 17 unit test projects in plan §3, plus
      `Integration.Tests --filter FullyQualifiedName~AspireFixtureReportSelectionTests`. Record
      passed/failed/skipped per project verbatim for the PR body. All green, or stop.

## Phase 4b — rename (backend-engineer). Release builds only. Tests may not be edited beyond the rename.

Rules for every task: rename **by declared name, per file** — never a `\b_\w+` regex (spec §2 H5).
Where a parameter shares the new name, write `this.x = x;` (H1/H2). Do not change anything else on
the touched lines.

- [ ] **T002** [P] [US1] Builders — the 11 files in plan §3 (70 fields). Apply H1 (21 sites),
      H2 (18 sites), and H3 (`LayoutBuilder.Build`: `this.tiles` on the right-hand side).
      Commit 1.
- [ ] **T003** [P] [US1] Fakes and in-memory repositories — the 26 files in plan §3 (36 fields,
      incl. tuple-typed `_entries` ×3 and `_byTrigger`). H1 in `FakeClock` and
      `FakeCameraFabGuard` constructors. Commit 2.
- [ ] **T004** [P] [US1] Integration fixtures — `AspireFixture.cs`, `AspireFixture.Auth.cs`,
      **`AspireFixture.LogCapture.cs`** (reference-only), `KeycloakAdminTokenProviderTests.cs`
      (H1 constructor); update the three comments (`AspireFixture.cs:250`, `:545`;
      `AspireFixtureReportSelectionTests.cs:44`). Leave the static helpers' `exitCodes`
      parameters alone (H4). Commit 3.
- [ ] **T005** [US1] `dotnet build -c Release` clean; re-run T001's exact commands; totals
      identical per project. Depends on T002–T004.

T002–T004 own disjoint files and are formally `[P]`, but one engineer on one branch commits them in
sequence (memory: *one branch, one index*); the marker means order is free, not that worktrees
are needed. Verify **each commit** builds in Release on its own (ADR-0087), not only the tip.

## Phase 5 — verify

- [ ] **T006** Run plan §4's two greps (both empty), after proving grep 2 by counterfactual.
      `git diff --stat origin/develop` shows only `tests/**` and `specs/312-*`;
      `git diff -G'"[^"]*_' origin/develop -- tests` shows no string literal changed. Write the
      verification note on the PR with T001/T005 totals side by side. Latency: N/A.

## Phase 6 — review

- [ ] **T007** `backend-reviewer`: walk every H1 site in spec §2 and confirm `this.` on each; confirm
      no assertion line changed (`git diff origin/develop -- tests | grep -E '^\+.*Should'` should
      show only renamed identifiers).

## Phase 7

- [ ] **T008** PR to `develop` (`--base develop`), body quotes T001/T005 output, `Closes #2556`,
      and records the `_byTrigger` finding. Comment the finding on #2764.

## Dependencies

T001 → T002–T004 (any order) → T005 → T006 → T007 → T008.
