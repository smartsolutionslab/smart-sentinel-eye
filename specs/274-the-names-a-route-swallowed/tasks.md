# Tasks: Spec 274, the names a route swallowed

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2358 · **Lane:** supervised (ADR-0037)
**Phase-4a colour: RED (behaviour-changing).** Every 4a test is observed failing before
T010/T011, and the failure is quoted verbatim in the PR body (ADR-0139). The engineer may
not edit a 4a test to pass.

`[P]` = disjoint files, may run concurrently (ADR-0109). `[US1]` = the only story.
Every task references #2358. No per-task issues (CLAUDE.md, Phase 3). The board gate is
#2358 on Project #13, left to the orchestrator.

**No foundational task.** Nothing touches Shared.Kernel, Shared.Contracts or AppHost.
Backend (T001-T004, T010) and frontend (T005, T011) own disjoint files and fan out in
parallel.

**Per-commit rule (ADR-0087):** see plan §5. Commit 1 = T010 + T011 + all 4a edits;
commit 2 = T020. Conventional Commits (ADR-0030), no Co-Authored-By trailer (ADR-0086).

---

## Phase 4a: red first (`test-writer`)

- [ ] **T001 [P] [US1]** New `tests/Architecture.Tests/SystemVariableRouteShadowingTests.cs`: for every same-verb, same-length pair of `/system-variables` endpoints where one template has a literal at the other's unconstrained `{name}` position, assert `VariableName.From(literal)` throws (plan §3.1). Run it and quote the red. It must name `snapshot` and `resolve`. #2358
  - **Depends on:** nothing. **Blocks:** T010.
- [ ] **T002 [P] [US1]** New `tests/Integration.Tests/SystemVariables/ShadowedVariableNameIntegrationTests.cs`: a theory over `snapshot` and `resolve`. Define in `munich` (201 or 409), `GET /system-variables/{name}?fabId=munich` → 200, `name` matches, `ETag` present, then archive to clean up (plan §3.2). Add the class to one `tests/Integration.Tests/ci-shards/shard-N.filter` (choose the lightest; see `ci-shards/README.md`). Quote the red (400 on both). #2358
  - **Depends on:** nothing. **Blocks:** T010.
- [ ] **T003 [P] [US1]** `tests/Architecture.Tests/SystemVariableReadScopeTests.cs:40-43`: `Snapshot` → `/system-variables/-/snapshot`; add `Resolve = "/system-variables/-/resolve"` to `Reads()`. Quote the red ("registered no GET"). #2358
  - **Depends on:** nothing. **Blocks:** T010.
- [ ] **T004 [P] [US1]** Integration callers to the new paths: `Fixtures/OverlaySnapshotReadiness.cs:27`, `SystemVariables/VersionSurvivesARestartTests.cs:141`, `SystemVariables/VariableReadScopeIntegrationTests.cs:57` (and add a `/-/resolve?text=x` row), `SystemVariables/ResolveOverlayTextTests.cs:187,240,250,258`. Record which go red before the move. The read-scope row does **not**, because `GetOne` also 403s (plan §2). Say so rather than claim red. #2358
  - **Depends on:** nothing. **Blocks:** T010.
- [ ] **T005 [P] [US1]** Frontend assertions: `apps/shared/src/api/systemVariables.api.test.ts:95`, tighten to the full pathname `/system-variables/system-variables/-/resolve` and add a pathname assertion for `getOverlaySnapshot` (`.../-/snapshot`). `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx:152`, same tightening. Run `pnpm test` in both packages and quote the red. #2358
  - **Depends on:** nothing. **Blocks:** T011.

## Phase 4b: implement

- [ ] **T010 [P] [US1]** (`backend-engineer`) `src/SystemVariables/Api/SystemVariableEndpoints.cs:67,81`: `"/snapshot"` → `"/-/snapshot"`, `"/resolve"` → `"/-/resolve"`. Nothing else in the file. T001-T004 go green unmodified. `dotnet build -c Release` clean. #2358
  - **Depends on:** T001-T004 red observed.
- [ ] **T011 [P] [US1]** (`frontend-engineer`) `apps/shared/src/api/systemVariables.api.ts:202,225`: `'/snapshot'` → `'/-/snapshot'`, `'/resolve'` → `'/-/resolve'`. T005 goes green unmodified. `pnpm lint && pnpm typecheck && pnpm test && pnpm format:check`. #2358
  - **Depends on:** T005 red observed.
- [ ] **T020 [US1]** Comment-only path updates (plan §2, "Doc comments"): `ResolvedOverlaySnapshotDto.cs:4`, `ResolvedTextPreviewDto.cs:4`, `ResolveOverlayTextQueryHandler.cs:10`, `OverlaySnapshotReadiness.cs:14`, `NFR_VariableResolutionLatencyTests.cs:43,131`, `TwoPlaceholdersInOneLabelTests.cs:7`, `ResolveOverlayTextTests.cs:14`, `ResolveOverlayTextQueryHandlerTests.cs:13`, `CellPage.test.tsx:1061`. Characterise by stripping comments and hashing (memory: *comment-only change*). Do not touch ADR-0115/0145. #2358
  - **Depends on:** T010, T011 (the same files are edited in T004, so this runs after them).

## Phase 5: verify

- [ ] **T030 [US1]** Boot the stack and run spec §4 "Independent end-to-end test": define `snapshot` in munich, read it back (200); a kiosk wall opening label resolves (the `/-/snapshot` fetch shows in the browser network log or the gateway trace); the management-web preview resolves (`/-/resolve`). Cite `NFR_VariableResolutionLatencyTests`' figure from the PR's CI TRX against the last green `develop` run (spec §6 caveat). #2358
  - **Depends on:** T010, T011, T020.
