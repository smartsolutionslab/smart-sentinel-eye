# Tasks 453: One group binder the two guards share

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3133 (feature-level, on Project #13)
**Slice:** 1 of 1. **Engineer:** backend-engineer (C# test-guard code only; no infra, no `src/`). **Colour:**
**characterisation** — behaviour-preserving; the 214-case net is captured green before any change and must pass
**unmodified** after every commit. A case that goes red is a regression: stop and report it verbatim; never edit an
assertion, fixture or `InlineData`. **Reviewers:** backend-reviewer; security-reviewer not needed (no runtime change).
**New ADR:** no.

**Parallelism:** none. T002 creates `RouteGroupBinding.cs`, which T003 consumes; T002 and T003 also each re-run the
same net. Strict order T001 → T002 → T003 → T004 → T005 (ADR-0109: no disjoint-file split worth the merge risk).
Nothing foundational outside this slice.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a — characterisation, captured green first (test-writer; blocks 4b)

- [ ] T001 [US1] At the base commit, before any edit: run plan §"Phase 4a"'s filter with `--logger trx`; quote the
      totals verbatim (expect `Passed: 214`); save the sorted test-name list and, via a scratch script in the
      scratchpad (not committed), the comment-stripped SHA-256 of every class member of
      `EndpointScopeDeclarationTests.cs`, `StatusProducerDeclarationTests.cs` and the code hash of `RouteChainReader.cs`
      — no file edited

## Phase 4b — implementation (backend-engineer; may not edit any test member, fixture or InlineData)

- [ ] T002 [US1] Create `RouteGroupBinding.cs` per plan §"The shared file" (444's binder, name-returning, reasons moved
      verbatim); switch ES to it per plan §"Edits by file"; add the file to `SharedSourceScanFiles`. Release build
      clean; net 214 green, same names. Commit 1 —
      `tests/Architecture.Tests/RouteGroupBinding.cs`, `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`
      (depends T001)
- [ ] T003 [US1] Switch SP to `RouteGroupBinding.Read`; `GroupScopes` → `GroupDeclarationSites` (walk-back kept);
      delete SP's copies; move `BoundGroup`'s differences doc. Release build clean; net 214 green, same names. Commit 2 —
      `tests/Architecture.Tests/StatusProducerDeclarationTests.cs` (depends T002)
- [ ] T004 [US1] Comment-only update of `RouteChainReader.cs`'s "resolver is NOT here" paragraph; code hash equal to
      T001's. Commit 3 — `tests/Architecture.Tests/RouteChainReader.cs` (depends T003)

## Phase 5 — verify

- [ ] T005 [US1] Member-hash diff vs T001: only plan-listed members changed, every `[Fact]`/`[Theory]` member equal;
      the four reason strings byte-equal in `RouteGroupBinding.cs`; counterfactuals C1-C3 on scratch edits, quote each
      red, revert; full `dotnet test tests/Architecture.Tests` green; `dotnet build -c Release` clean; `git diff --stat
      origin/develop -- src/` empty; no new test class, so no shard-filter entry needed — confirm (depends T004)

## Phase 6 — review

- [ ] T006 backend-reviewer: no verdict can differ (D1-D3 table holds); no test member edited; the scan-list entry is the
      only test-data edit and it widens, not narrows; out-of-scope binders (`PrecedingGroupPrefix`, `PrefixOf`) untouched
