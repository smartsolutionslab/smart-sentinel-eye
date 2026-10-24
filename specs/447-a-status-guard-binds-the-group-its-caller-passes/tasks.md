# Tasks 447: A status guard binds the group its caller passes

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3127 (feature-level, on Project #13)
**Slice:** 1 of 1. **Engineer:** backend-engineer (C# test-guard code; no infra). **Colour:** red for the new
StatusProducer binding; characterisation for the 33 + 21 existing cases and the comment-only RouteValueRefusal edit
(plan §"Phase 4a"). **Reviewers:** backend-reviewer (security-reviewer optional — no runtime change).

**Parallelism:** T004 [P] owns `RouteValueRefusalDeclarationTests.cs` alone and may run beside T001-T003; every
other task edits `StatusProducerDeclarationTests.cs`, so their order is strict (ADR-0109). Nothing foundational.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a-1 — characterisation, captured green first (test-writer)

- [ ] T000 [US1][US2] Run and quote green, unmodified: `StatusProducerDeclarationTests` (expect 33) and
      `RouteValueRefusalDeclarationTests` (expect 21); record the comment-stripped SHA-256 of
      `RouteValueRefusalDeclarationTests.cs` — no file edited

## Phase 4a-2 — new behaviour, observed red (test-writer; blocks 4b)

- [ ] T001 [US1] Fixture facts via `RouteGroups(...)` + `Mappings(...)` (existing private seam), one per spec scenario:
      control (`MapX(reads)` → Authorized, `/b/`; green, labelled control), mismatch (`MapX(writes)` → Unclassified,
      `/a/`), two groups → Unclassified, `MapX(app.MapGroup("/c").RequireAuthorization())` → Unclassified,
      never-called → Unclassified, `MapY(RouteGroupBuilder reads, string label)` → Unclassified. Sentence-style names
      (ADR-0053); raw-string fixtures (ADR-0054). Run; **quote the five non-control reds verbatim** —
      `StatusProducerDeclarationTests.cs` (depends T000)
- [ ] T002 [US1] Planted violation, scratch only: spec procedure step 2 on the unchanged reader; quote StatusProducer
      green (the defect) — no committed change (depends T000)

## Phase 4b — implementation (backend-engineer; may not edit T000-T001's tests or fixtures)

- [ ] T003 [US1] Port call-site binding (plan §"The reader's change"): `ParameterScope`, `GroupHelperDeclaration`,
      `RouteBuilderParameter`, `EqualsAssignment`, `ParameterListEnd`, `BodyAfter`, `ResolveHelperGroup`; replace the
      `:866-868` lookup; add L5's unresolved-helper clause; rewrite class doc `:62-70`. T001 green; T000's 33 green
      **unmodified**. If an existing case goes red, stop and report the `src/` misbinding verbatim — do not adjust —
      `StatusProducerDeclarationTests.cs` (depends T001 red)
- [ ] T004 [P] [US2] `PrefixOf` doc (plan §"RouteValueRefusal"); re-run the 21 green; comment-stripped hash equal to
      T000's — `RouteValueRefusalDeclarationTests.cs` (depends T000)
- [ ] T005 [US1] Plan §Counterfactuals 1-3 on scratch edits, quote each failure, revert; `git diff --stat src/` empty
      (depends T003)

## Phase 5 — verify

- [ ] T006 Full `dotnet test tests/Architecture.Tests` green; Release build clean (`TreatWarningsAsErrors`; IDE0007,
      collection expressions, no `_` fields); quote totals

## Phase 6 — review

- [ ] T007 backend-reviewer: no shape lets a parameter be credited by name; every unresolved helper is Unclassified
      (red); no existing assertion edited; US2 is comment-only
