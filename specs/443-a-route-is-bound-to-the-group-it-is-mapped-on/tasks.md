# Tasks 443: A route is bound to the group it is mapped on

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3101 (feature-level, on Project #13)
**Slice:** 1 of 1. **Engineer:** backend-engineer (C# test-guard code; no infra). **Colour:** red for the new binding,
characterisation for the 106 existing cases (plan §"Phase 4a"). **Reviewers:** backend-reviewer
(security-reviewer optional — no runtime change).

**No parallelism:** every task edits the one file `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`
(ADR-0109 disjoint-file rule), so the order below is strict. Nothing foundational outside it.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T000 [US1] Run and quote green, unmodified: `EndpointScopeDeclarationTests` (expect 106) and the nine
      `*RouteTableCharacterisationTests` — no file edited
- [ ] T001 [US1] Seam: `Read(string file)` → `Read(file, ReadRepositoryFile(file))` + `Read(string file, string source)`;
      re-run T000's set, quote green and unchanged totals — `EndpointScopeDeclarationTests.cs` (depends T000)

## Phase 4a-2 — new behaviour, observed red (test-writer; blocks 4b)

- [ ] T002 [US1] Fixture facts via `Read(file, source)`, one per spec scenario: control (helper called with its
      namesake group → write scope; green, labelled control), mismatch (`MapX(reads)` → `Scope.Sse.Cameras.Read`,
      and a `Required scope: sse.cameras.write` summary is reported by the mismatch rule), two-groups → Unreadable,
      non-group argument (inline `MapGroup` and unknown identifier) → Unreadable, never-called → Unreadable,
      multi-parameter method receiver colliding with a group local → no group authorization. Sentence-style names
      (ADR-0053); raw-string fixtures (ADR-0054). Run; **quote the five non-control reds verbatim** — same file
      (depends T001)

## Phase 4b — implementation (backend-engineer; may not edit T000-T002's tests or fixtures)

- [ ] T003 [US1] `GroupHelper` record + helper/parameter-span discovery in `Read(file, source)` (plan §Entities) —
      same file (depends T002 red)
- [ ] T004 [US1] Call-site resolution + "parameters are never name-bound" in `ReadMapping`'s group lookup
      (`:1535`); Unreadable details name helper/argument/groups. T002 green; T000 set green **unmodified**. If an
      existing case goes red, stop and report the `src/` misbinding verbatim (plan §"Phase 4a") — same file
      (depends T003)
- [ ] T005 [US1] Class doc: replace `:218-222` "No such indirection exists today"; rewrite `:282-301` for call-site
      binding, keep the duplicate-declaration history — same file (depends T004)
- [ ] T006 [US1] Run plan §Counterfactuals 1-4 on scratch edits, quote each failure, revert; `git diff --stat src/`
      empty — no committed change (depends T004)

## Phase 5 — verify

- [ ] T007 Full `dotnet test tests/Architecture.Tests` green; Release build clean (`TreatWarningsAsErrors`; IDE0007,
      collection expressions, no `_` fields); quote totals

## Phase 6 — review

- [ ] T008 backend-reviewer: binding cannot credit a group by parameter name in any shape; every unresolved shape is
      Unreadable (red), never a pass; no existing assertion edited

## After merge

- [ ] T009 File follow-up issue: `StatusProducerDeclarationTests.cs:866-868` (group-level declaration credited by
      receiver name) and `RouteValueRefusalDeclarationTests.cs:745` (prefix by name, message only) share the defect;
      cite this spec's resolver as the pattern. Add it to Project #13 without `agent:ready`.
