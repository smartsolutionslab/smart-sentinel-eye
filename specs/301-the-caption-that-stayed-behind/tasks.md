# Tasks 301: The caption that stayed behind

`[ID] [P?] [Story]`. `[P]` marks tasks that own **disjoint files** and may run concurrently
(ADR-0109).

**Colour:**
- `C`: characterisation, observed green before the change and unmodified after.
- `R`: red, observed failing, with the output quoted in the PR.
- `—`: implementation or measurement.

**Engineers:** `frontend-engineer` (T004–T006, T013–T015), `backend-engineer`
(T008–T012, T016). In PR-B both work the **same branch**, backend first, then frontend,
because the contract cut and the kiosk type change land in one commit. Tests go to `test-writer` first, per phase 4a (T002, T003, T007 and the `R`
halves). **No infra engineer is needed.**

**PRs:**
- **PR-A** = US1 + US3 (T000–T007).
- **PR-B** = US2 (T008–T017). Cut from `develop` after PR-A merges, because both edit
  `LayoutGrid.tsx`'s `Tile`.

US2 can be dropped without touching PR-A.

**Precondition:** spec 300 (#2349) PR-A merged to `develop`. Re-check that the number `301`
is still free, then cut the branch from `develop`, never from the #2349 branch.

**Tracking:** one feature-level issue per PR on Project #13. No per-task issues (the
post-028 convention). PR-A: #2348 (closed by PR #2717, US1 + US3). **PR-B: #2720**
(T008–T017), one branch and one PR for backend and kiosk alike.

**PR-B re-verification (2026-10-07, `develop` `0d1864f5`):** the plan holds in substance.
Six clerical drifts were corrected in `plan.md` and below: the inline lookup is not a
named `liveTextFor`; the audit mapping is a hand-tweak; T010's open question is answered;
four construction-only test files were missing; stale V2 doc comments are listed; and the
ADR-0073 citation for the same-commit cut was wrong (plan §"Why no ADR" item 3; human
sign-off recorded on #2720; ADR-0073 text fix tracked as #2738).

---

## Phase 0: gate housekeeping and baseline (blocks everything)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T000** | | all | — | **Human, at the gate.** Accept or amend the re-scope: the editor half and the overlap-warning question go to #2343, and #2348 becomes the renderer half. Then: comment on #2343 with spec decisions 1–3 and the open question, file follow-on 2 (`renderElementsKey` omits geometry), and confirm #2348 and #2343 are both on Project #13 (`--limit 2000`). |
| **T001** | [P] | all | **C** | Record green on the post-#2349 `develop`, and quote pass counts in the PR: `OverlayRevisionLifecycleIntegrationTests` (the four `..._in_ordinal_order` methods), `OverlayLabelCharacterisation`, `OverlayLabelParity`, `OverlayLabelNodeCountCharacterisation`, `overlayLabelStyle.test.ts`, `useLabelDelay.test.ts`, `CellPage.test.tsx`, the `OverlayEditor*` and `OverlayEditorDialog*` tests, and `e2e/kiosk-shows-shapes-over-video.spec.ts` / `kiosk-shows-two-labels-over-video.spec.ts`. |
| **T002** | [P] | US1 | — | Baseline `overlay_draw` p50/p95: spec 225's 21-run mean, plus this branch's first CI render-leg record before any production commit. |

---

## Phase 1: US3, the reorder path pinned (PR-A; no production change)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T003** | [P] | US3 | **C** | Add a method to `tests/Integration.Tests/OverlayDesigner/OverlayRevisionLifecycleIntegrationTests.cs` (existing class, so no shard entry): create `[Box, Text, Ellipse]`, branch, `PATCH` `[Ellipse, Text, Box]` with `If-Match`, `GET` in that order, publish, and assert that the captured `OverlayRevisionPublishedV3.Elements` kinds are `[Ellipse, Text, Box]`. For the 409-on-stale-reorder scenario, **cite** the existing stale-replace test if one exists, and add one only if none does. **Expected green on the first run. A red means the spec's premise is false: stop and report.** |
| **T004** | [P] | US3 | **C** | New `e2e/kiosk-paints-a-reordered-overlay.spec.ts` (ADR-0162 API helpers): seed `[Box, Text "Zone A", Ellipse]` with overlapping geometry and expect **201** (FR-002). Publish and bind, then assert the DOM order of the overlay nodes by `data-kind`. Republish reordered, assert the new order, and assert `zIndex === 'auto'` on every overlay node. Add a shard entry if e2e has a shard list. Expected green, same stop rule as T003. |

---

## Phase 2: US1, the hold carries the paired set (PR-A)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T005** | | US1 | **R** | **Premise test first.** A `kiosk-web` Vitest render test of a `Tile` / `LayoutGrid`, with frame age held at 120 ms and fake timers. The cases are in plan §"Phase 4a, RED, US1": the two-caption reorder mis-join, the Box/Text swap empty caption, the move-only republish held, and one hold per change. **Run on unmodified code and quote the failures.** If the reorder case arrives green, US1's premise is false: stop and report. |
| **T006** | | US1 | **R** | `LayoutGrid.tsx` `Tile` per plan §"US1, After":<br>- build `liveElements` (paired, unheld);<br>- add a stable key over `kind\|color\|x\|y\|w\|h\|fontSizePx\|text`;<br>- call `useLabelDelay` on the paired set;<br>- leave `renderElementsKey` untouched.<br>**`useLabelDelay.ts` and `CameraViewer.tsx` must not appear in the diff.** T005 turns green. **Counterfactual:** drop `x` from the key, show the move-only case go red, then restore it. If an existing `CellPage.test.tsx` assertion encodes "geometry paints before the hold", change it here and list it in the PR with before and after quoted (plan §"Existing tests that may encode the defect"). Depends on T005. |
| **T007** | | US1 | — | **FR-011.** `overlay_draw` p50/p95 on the nine-tile fixture, **run twice**. Quote them beside T002, spec 225's 56.63 ms and ADR-0123's 54.2/79.2 ms. Re-run T001's guards and show them unmodified. Run the spec's independent procedure, steps 1–4 and 7, and attach the pairing recorder's log. Depends on T003, T004, T006. |

**PR-A gate:**
- T003/T004 green and unmodified;
- T005 red, then green;
- the T006 counterfactual quoted;
- T007 figures quoted;
- **no** `src/` diff outside `apps/kiosk-web`.

---

## Phase 3: US2 contract and backend (PR-B; after PR-A merges)

`Shared.Contracts` (T008) is the **foundational, blocking** task. The solution does not
compile between V2's deletion and its consumers' migration, so **T008–T012 land as one
commit** (each commit must build on its own). The `[P]` markers below are for parallel
*authoring* in separate worktrees, integrated into one commit.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T008** | | US2 | **R** | `src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV3.cs` (`ResolvedOverlayTextChangedV3` + `ResolvedOverlayTextV3(Template, Resolved)`), with the doc comment carrying the pair-by-template rule. **Delete V2.** Paste the `grep -rn ResolvedOverlayTextChangedV2` output into the PR. Tests: the shape, and V2 absent. **Blocks T009–T012.** |
| **T009** | [P] | US2 | **R** | SystemVariables: `ResolvedTextPairs` (skip `""`, drop duplicates by `Ordinal`, keep order) and its unit tests (plan §"Phase 4a, RED, US2"). `ResolvedOverlayTextDto`, and `ResolvedOverlaySnapshotDto.Texts`. Depends on T008. |
| **T010** | | US2 | **R** | SystemVariables producers: `VariableValueChangedDomainEventHandler` (`:85`), `VariableArchivedDomainEventHandler` (`:102`) and `GetOverlaySnapshotQueryHandler` call `ResolvedTextPairs`. Red tests: pairs emitted, one event per overlay. The #2426 version-before-text assertion is unedited. **`SystemVariableValueRequestedV1Handler` does not emit** (it dispatches `SetVariableValueCommand`, `:83-98`); its production change is the stale V2 comment at `:95` only, but `SystemVariableValueRequestedV1HandlerTests.cs:350-351` asserts on V2 and migrates to V3 (type/accessor only). Migrate `VariableValueChangedPreCommitTests.cs:76,127` the same way (construction/capture lines only, FR-008). Correct the stale V2 / index-alignment doc comments: `IOverlayTextVersions.cs:5`, `SystemVariablesInfrastructureModule.cs:31`, the `:17` crefs in both domain-event handlers, and `ReverseIndexSeederHostedServiceTests.cs:215` (comment only). Depends on T009. |
| **T011** | [P] | US2 | **R** | LayoutComposition:<br>- `ResolvedOverlayTextChangedV3Handler` (destructure first);<br>- the `ResolvedOverlayTextChangedNotification` pairs plus the `ResolvedOverlayText` record;<br>- the hub message `Texts`;<br>- the broadcaster mapping and the module registration;<br>- correct the notification's stale "republished with new references" doc sentence.<br>**Confirm `PrimitiveBoundaryTests` excludes the notification records**, and record the finding. Do not suppress anything. Update `LifecycleNotificationTests`. Depends on T008. |
| **T012** | [P] | US2 | **R** | AuditObservability: `IntegrationEventAuditHandler` V3 overload replacing V2 (`:55`). The V2 mapping is a **hand-tweak**, registered explicitly at `V1ResourceMap.Conventions.cs:74`: edit that line to `Add<ResolvedOverlayTextChangedV3>(…)` directly. **Change the existing case 16** in `V1ResourceMapTests.cs:441-453` (`ResolvedOverlayTextChangedCase`) from V2 to V3 (type, and the constructor's text list becomes one `ResolvedOverlayTextV3` pair); **do not add a new case**. The expected `ResourceKind.Overlay` and resource id stay unedited. Depends on T008. |

---

## Phase 4: US2 kiosk side (PR-B)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T013** | | US2 | **R** | **Premise test first.** Kiosk test: a tile holds the old revision's resolved texts, a reorder is published, and the snapshot is not refreshed. Each caption must show its own template's value. **Observe red on today's positional shape and quote it.** After T014, only the fake server's response builder changes shape, and the assertion is not edited. Can be written before T008 lands. If it arrives green on today's code, US2's premise is false: stop and report. |
| **T014** | | US2 | **R** | `apps/shared` types (`systemVariables.api.ts` `texts`, `layoutHub.ts` `texts`, with their doc comments at `systemVariables.api.ts:77-83` and `layoutHub.ts:83-94` (names V2 at `:86`) rewritten to the pair-by-template rule), `useOverlayHubHandlers.ts` upsert shape (`:202`), and in `LayoutGrid.tsx` `Tile` replace the **inline positional lookup at `:446`** (inside the `liveElements` map; there is no named `liveTextFor` function) with the template lookup, deleting the positional read and `""` padding (plan §"Frontend (US2)"); update PR-A's "stays positional in US1" comment at `:432-444`. Plus the "new template shows its raw template, never a neighbour's value" red test. Construction-only test migrations (FR-008, no `expect` edited): the hub-handler tests in `CellPage.test.tsx`, `useLayoutLifecycle.test.tsx:63`, `systemVariables.api.test.ts:11`, and `LayoutGridLabelPairing.test.tsx` (fixtures + comment wording at `:17`, `:312` only; assertions byte-identical). **Same commit as T008–T012's hub message change** (plan §"PR split"). Depends on T008, T013. |
| **T015** | [P] | US2 | **R** | New `apps/kiosk-web/src/features/cell/useTemplateMissRefetch.ts` + `.test.ts` (fake timers): 1/2/4 s refetch, early stop, exactly one `resolved-text-template-miss`, reset on a new publication key, timers cleared on unmount, no `refetch` while the query is skipped. Wire it into `Tile` after T014: **`Tile` does not yet take `refetch`** from `useGetOverlaySnapshotQuery` (`LayoutGrid.tsx:427-430` destructures only `data`), so add it there. Hook and test files are disjoint, so they can be authored in parallel with T014. |
| **T016** | | US2 | **C** | Integration tests whose payload accessor changes: `ResolvedTextReachesItsFabTests`, `VersionSurvivesARestartTests`, `OverlaySnapshotReadiness`(+`Tests`). Only the accessor changes. **The fab and version assertions are unedited.** Capture them green on `develop` before T008 (pass counts in the PR), then green again after. |
| **T017** | | US2 | — | **FR-011.** Quote `NFR_VariableResolutionLatencyTests`' median, **run twice**. Run the spec's independent procedure, steps 5–6 (`system-variables` stopped). Attach the pairing recorder's log, and the console's `resolved-text-template-miss` line. Depends on T010–T016. |

**PR-B gate:**
- T008/T013 red, then green;
- the T011 `PrimitiveBoundaryTests` finding recorded (T010's is already answered);
- T016 assertions unedited; every construction-only file in plan §"CHARACTERISATION"
  shows no edited `expect`/`Should*` line;
- `grep -rn "ResolvedOverlayTextChangedV2" src tests apps e2e` pasted into the PR: no type
  reference left, and every remaining hit is a comment narrating history (e.g.
  `VariableArchivedDomainEventHandlerTests.cs:45`, `VariableDefinedDomainEventHandlerTests.cs:73`)
  rather than describing current behaviour;
- T017 figures quoted;
- **no OverlayDesigner `src/` diff, no `CameraViewer.tsx` diff, no `OverlayEditor*` diff.**

---

## Dependency summary for the orchestrator

```
#2349 PR-A merged ──► T000 (human) ──► T001 T002 [P]
                                        │
PR-A:   T003 [P]   T004 [P]   T005 ──► T006 ──► T007
            └──────────┴──────────────────────────┘
                         (PR-A merges)
PR-B:   T013 (premise, can start early)
        T008 ──► T009 ──► T010
          ├────► T011 [P]
          ├────► T012 [P]
          └────► T014 ◄── T013      T015 [P] ──► (wired after T014)
                   └──► T016 ──► T017
```

**Foundational, blocking:**
- T000, which needs a human.
- `Shared.Contracts` (T008) in PR-B. After it, SystemVariables (T009/T010),
  LayoutComposition (T011), AuditObservability (T012) and the kiosk (T014/T015) are
  independent authoring lanes, landing together.

**Parallel:** in PR-A, T003, T004 and T005 are mutually disjoint.

**Scope tripwires, checkable without reading code:**
- no `CameraViewer.tsx`, `useLabelDelay.ts`, `OverlayEditor*` or `OverlayEditorDialog*`
  diff;
- no OverlayDesigner `src/` diff;
- no `z-index` added anywhere;
- no new configuration key.

**Silent-break tripwires** (they compile cleanly when wrong):
- the stable key omitting a painted field (T006's counterfactual);
- `Template`/`Resolved` transposed in a deconstruction (T011, `HandlerDeconstructionTests`);
- the kiosk still reading `resolvedTexts` anywhere after T014. Grep for it and paste the
  empty result.
