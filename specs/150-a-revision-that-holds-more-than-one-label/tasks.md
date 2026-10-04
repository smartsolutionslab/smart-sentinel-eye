# Tasks 150 — A revision that holds more than one label

`[ID] [P?] [Story]` — `[P]` marks tasks owning **disjoint files** that may run
concurrently (ADR-0109). Everything in Phase 1 is foundational and blocks the fan-out;
say so to the orchestrator rather than discovering it.

**Colour** is per artefact, per `plan.md` §"Phase 4a". `C` = characterisation, observed
**green before** the change and passing unmodified after. `R` = red, observed failing,
output quoted in the PR. `—` = implementation, no colour of its own.

**Engineers:** `backend-engineer` (T004–T016, T022), `frontend-engineer` (T001,
T017–T021, T023, T026). T002/T003/T024/T025 are measurement, run by whoever holds the stack.
This is **not** a backend-only change. The wall's render and two console files are in
scope (plan §"Re-verification", last paragraph).

**Phase 4a colour: two colours, per artefact.** Characterisation (green before, unmodified
after) on the wall's single-label render, the shared editor's guards and the existing
e2e. **Red** on everything that carries a second label, including the two silent-break
guards this re-verification added (T014's seeder test, T021's FR-018 test).

**Re-verified 2026-10-04** against `develop` `d84554c7`. Tasks changed: T002, T003, T014,
T016, T018, T020, T021, T023, T024. Task added: T026. The reasons are in plan.md
§"Re-verification, 2026-10-04" (D1–D13), cited per task below.

---

## Phase 0 — capture the baseline (must complete before any change)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T001** | [P] | US3 | **C** | New `OverlayLabelNodeCountCharacterisation.test.tsx` in `apps/shared/src/ui/composites/`. Assert **exactly one** `camera-viewer-overlay-label` node for a tile with an overlay, **zero** for a tile without, and that the node is a **direct child** of the `relative aspect-video` container. Observe green. This is the gap the existing four guards leave (`plan.md` §Phase 4a) and it is FR-013's only guard. |
| **T002** | [P] | US3 | **C** | Run and record green: `OverlayLabelCharacterisation` (6), `OverlayLabelParity` (6), `OverlayEditorCharacterisation` (10), `OverlayEditorBackdrop` (6), `OverlayEditorKeyboard`, `OverlayEditorUndo`, and the backend `OverlayRevisionStateMachineTests` / `OverlayChainArchivalTests` / `TimestampOrderingTests`. Quote the pass counts in the PR — this is the "before" half of the characterisation obligation. *(D9: counts re-read; the two editor guards are new since the original plan.)* |
| **T003** | [P] | US1 | — | Record the **pre-change** `overlay_draw` p50/p95 on the **nine-tile** single-label fixture of `e2e/kiosk-shows-a-label-over-video.spec.ts`. Primary basis: `specs/225-the-render-leg-ci-never-reads/figures.md`'s 21-run `develop` baseline (mean p50 56.63 ms, σ 8.55). Also record the render-leg record from this branch's own first CI run **before** any Phase 1 commit, if one exists. A local run, if taken, runs twice (the first run after machine churn reads like a regression). This is the comparison basis for T024. *(D10.)* |

T001–T003 are disjoint and parallel. **All three block Phase 1.**

---

## Phase 1 — foundational (blocks Phases 2, 3 and 4)

Nothing downstream compiles until the domain and the contracts settle. Run T004–T006
in order (same files); T007/T008 are parallel to them and to each other.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T004** | | US1 | **R** | `Label`: add the private `ordinal` field + its value-object accessor, mirroring `Tile.Row/Col` (`plan.md` explains why, including why the primitive guard would not have caught it). Add `const MaxLabels = 8` beside `MaximumTextLength`. Add `LabelSetViolation { Empty, TooMany }` beside `GridViolation`'s shape. Tests first, red. |
| **T005** | | US1 | **R** | `Revision`: `Label` → `IReadOnlyList<Label> Labels` with a private backing `List<Label> labels = []`; `EditLabel` → `ReplaceLabels` (Draft-guard only, clear-then-AddRange, mirroring `ReplaceTiles`). `NewDraft` **clones every element including each `Position` and `Size`**; `Branch` delegates to it. FR-007 — the highest-risk line in the backend; its integration proof is T013. |
| **T006** | | US1 | **R** | `Overlay`: `ValidateLabels(IReadOnlyList<Label>) → Option<LabelSetViolation>` (public, single source of truth) + private `RequireValidLabels` backstop. `CreateDraft` / `EditDraft` / `BranchDraft` take and pre-fill the set. `OverlayRevisionPublishedDomainEvent.Label` → `Labels`. **Publish/Revert/Archive/RecomputeArchival must not change** — a diff there is a defect. |
| **T007** | [P] | US1 | **R** | `Shared.Contracts`: add `OverlayRevisionPublishedV2` + nested `OverlayLabelV2` (primitives only, ADR-0040), **delete `OverlayRevisionPublishedV1` in the same commit** — the `LayoutRevisionPublishedV2` precedent (`a2768788`). Leave `OverlayRevisionArchivedV1` alone. |
| **T008** | [P] | US2 | **R** | `Shared.Contracts`: add `ResolvedOverlayTextChangedV2` carrying `IReadOnlyList<string> ResolvedTexts` + one `Version`; delete V1 in the same commit. Note in the XML doc that the list is index-aligned with the revision's labels, and that #2348 landing first would argue for `(ordinal, text)` pairs instead. |

---

## Phase 2 — OverlayDesigner persistence, application, API (US1)

Sequential: T009→T010 share the schema, T011→T012 share the request path. Depends on
T004–T007.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T009** | | US1 | — | `OverlayConfiguration`: `OwnsOne(Label)` → `OwnsMany(Labels)` onto `overlay_revision_labels`, key `(revision_id, ordinal)`. Copy `LayoutConfiguration`'s field-mapping idiom — `Property<int>("ordinal")` must use the field name **exactly**. Carry the `Position`/`Size` `Navigation(...).IsRequired()` comment (#2022) down with them. **Rewrite the class doc-comment**: its "flattened … rather than a separate owned entity … without joins" sentence stops being true. |
| **T010** | | US3 | **R** | One EF migration (ADR-0067): create the table → **backfill one row per revision at `ordinal = 0`** from the six `label_*` columns → **drop those six columns in the same migration**. Hand-write the backfill SQL. Red test: a pre-migration single-label row arrives as a one-element set with geometry intact. |
| **T011** | | US1 | **R** | Application: `CreateOverlayDraftCommand` / `EditDraftRevisionCommand` take `IReadOnlyList<Label>`; `OverlayRevisionDto` and `PublishedOverlayDto` carry `IReadOnlyList<OverlayLabelDto>`; add the two `OVERLAY_LABELS_*` cases to both `*Errors.cs`. Handlers call `ValidateLabels` **before** mutating (the `ValidateGrid` split). |
| **T012** | | US1 | **R** | Api: `CreateOverlayRequest` / `EditDraftRequest` take `IReadOnlyList<LabelRequest>` (`LabelRequest` itself unchanged). In `OverlayEndpoints.Commands.cs` loop `Label.From` **inside the one existing `try`**, so the first bad label 400s the whole set naming its field. **No route, scope, `If-Match` or `Idempotency-Key` changes.** |
| **T013** | | US1 | **R** | Integration tests against the Aspire fixture: create-with-three-labels → GET returns three in order → publish emits **exactly one** `OverlayRevisionPublishedV2` with all three. **Plus the FR-007 proof**: branch a three-label revision and save — this must be an integration test, because the failure it guards is EF re-keying an owned entity, which a unit test cannot produce. Extend `OverlayRevisionLifecycleIntegrationTests` / `OverlayLifecycleIntegrationTests`. |

---

## Phase 3 — the consuming contexts (depends on T007/T008)

All three own disjoint files and run concurrently.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T014** | [P] | US2 | **R** | SystemVariables. `IReverseIndex`: `UpsertOverlayReferences(Guid, IReadOnlyList<string>)`, `LookupLabelText` → `LookupLabelTexts`; `InMemoryReverseIndex` follows. **`RemoveOverlay`, `LookupOverlays`, `AllOverlays` and the whole of `IOverlayTextVersions` are unchanged** — FR-011, and the reviewer should check a finer key did not creep in. *(D3: versions now live in `IOverlayTextVersions`.)* **The three publishers**: `VariableValueChangedDomainEventHandler` and `VariableArchivedDomainEventHandler` resolve every label of each affected overlay under the version `AdvanceAsync` already returned (still **one** `AdvanceAsync` per fan-out, **one** snapshot build per overlay over the union of placeholders, **one** V2 event per overlay); `GetOverlaySnapshotQueryHandler`: `ResolvedOverlaySnapshotDto.ResolvedText` → `ResolvedTexts`. *(D5.)* **`ReverseIndexSeederHostedService`**: read `"labels"` (array) → each element's `"text"` instead of `"text"`. Red test first in `ReverseIndexSeederHostedServiceTests`: a new-shape payload seeds the overlay with all of its texts. Prove the test by counterfactual: against the unchanged seeder it seeds **zero** — that is the silent cold-start failure this guards. *(D4.)* `ResolveOverlayTextQueryHandler` untouched. |
| **T015** | [P] | US2 | **R** | LayoutComposition: `OverlayRevisionPublishedHubMessage` and `ResolvedOverlayTextChangedHubMessage` carry the collections; handlers relay them; re-register the renamed handlers in `LayoutCompositionInfrastructureModule` (`:119`, `:121`). Per-fab fan-out loop unchanged. Watch the destructuring — `HandlerDeconstructionTests` fails the build on a local named after another field of the same record. |
| **T016** | [P] | US1 | — | Update the remaining V1 consumers the clean cut leaves stranded (re-enumerated 2026-10-04, plan §"Shared.Contracts"): `IntegrationEventAuditHandler`'s two overloads; **`V1ResourceMap.Conventions.BuildHandTweaks` — add `Add<ResolvedOverlayTextChangedV2>(…, changed => changed.Overlay)`, do not merely delete the V1 line** (D6), and `V1ResourceMapTests`; `EventMetadataFabDeclarationTests`; `Shared.Contracts.Tests`' two V1 test files; the handler tests; `OverlayLifecycleIntegrationTests`, `VersionSurvivesARestartTests`, `CrossFabReadGuardIntegrationTests`; request bodies via `tests/Integration.Tests/Fixtures/OverlayRequests.cs`; doc-comment `<see cref>`s. Compile-driven apart from the `V1ResourceMap` entry; finish before the frontend lands. |

---

## Phase 4 — the wall and the clients (depends on T007/T008; T019 depends on T001)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T017** | [P] | US1 | **R** | `overlays.api.ts`: **drop `OverlayRevision extends OverlayLabel`** — the finding itself — for `labels: OverlayLabel[]`. `PublishedOverlay.text` → `labels`; `CreateOverlayDraftInput.label` → `labels`; edit body `{ label }` → `{ labels }`. `overlays.schema.ts`: `z.array(overlayLabelSchema).min(1).max(8)`. **Pin the 8 against the domain `const` with a test** — two hand-copied bounds is how #2361 happened. |
| **T018** | [P] | US2 | **R** | `apps/shared/src/realtime/layoutHub.ts`: the two message **types** take collections. `apps/shared/src/api/systemVariables.api.ts`: `ResolvedOverlaySnapshot.resolvedText` → `resolvedTexts` (the resolve-preview DTO stays per-string). `apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts`: the `upsertQueryData` writes `resolvedTexts`; **`overlayTextVersionsRef` stays `Map<string, number>` keyed by overlay** (FR-011). *(D7: handling moved out of `layoutHub.ts`/`CellPage` into this hook.)* |
| **T019** | | US1 | **R** + **C** | `CameraViewer.tsx`: `overlay?` → `overlays?: readonly CameraViewerOverlay[]`, single render site → `.map()`. **No wrapper element** (a fragment is fine, a `<div>` is a defect); **zero nodes for an empty/absent list**; `key` is the **ordinal**, not the index and not the text (FR-004 permits duplicate text). `overlayLabelSurfaceStyle` unchanged and called once per label. **T001, `OverlayLabelCharacterisation` and `OverlayLabelParity` must pass with only their prop-construction lines edited — an edited assertion is a block.** Depends on T001, T017. |
| **T020** | | US1/US2 | **R** | **`apps/kiosk-web/src/features/cell/LayoutGrid.tsx`, the `Tile` component** (not `CellPage.tsx`, now a shell — D7). Build the render list by zipping the published revision's `labels` geometry with `snapshot?.resolvedTexts[i] ?? labels[i].text`, positionally. `hasPlaceholder` = **any** label contains `{{`; `labelTextKnown` = every label's `text` is a string; `onLabelVerdict` stays one verdict per overlay. **One `useLabelDelay` per tile, generalised to hold the set** (FR-014) — hooks cannot be called in a loop, and the set is versioned atomically so it shares an age. **`measureOverlayDraw`'s effect must be keyed on a stable scalar derived from the set, never on the array** — an array dependency re-fires every render and floods `overlay_draw` with no-op samples (#1888/#1889, ADR-0123). Red tests for: a placeholder in label 3 only still fetches the snapshot; the draw measurement fires once per real change, not per render. Depends on T017–T019. |
| **T021** | [P] | US1 | **R** | `management-web`, **FR-018**. `OverlaysPage.tsx`: the edit target (`:85`, `:209`) carries `labels` (the whole array; `labelOf` at `:382` becomes a whole-set copy); row summary (`:154`) reads `summarised.labels[0].text`. `OverlayEditorDialog.tsx`: the form holds the **whole** `labels` array, `Controller` binds `labels.0`, `DEFAULT_INPUT` wraps in an array, `useWatch('label.text')` → `'labels.0.text'`, `errors.label?.text` → `errors.labels?.[0]?.text`, and the PATCH sends **`labels: input.labels`** (all N). **Red test first** in `OverlayEditorDialog.test.tsx`: an edit target with three labels, edit the visible text, save → the mutation receives three labels, index 0 changed, 1–2 byte-identical. *(D12: the original task's wording would have truncated every multi-label overlay to one on its first console edit.)* **`OverlayEditor.tsx` must not appear in the diff** (FR-016); its four guard files staying byte-identical is the mechanical check. |
| **T022** | [P] | US1 | — | `ScenarioSimulator`: `OverlayDesignerClient.EnsureOverlayAsync` + `CreateOverlayBody` take a list; `ScenarioSeeder` passes one element. `Seeding/OverlayLabel.cs` unchanged. |
| **T026** | [P] | US3 | **C** | `e2e/overlays.spec.ts`: three tests read `revisions[0].normalizedX` / `.normalizedWidth` from untyped GET JSON (`~:240`, `~:660`, `~:712`). Move the read path and its inline cast type to `revisions[0].labels[0].…`. **The expected values (`0.2487`, `0.5`, `0.9`) must not change** — an edited expected value is a block. Observe the three green on `develop` before the change. *(D11: missed by the original plan; it compiles, and fails only at e2e time with `undefined`.)* |

---

## Phase 5 — observe it, and pay §IV

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T023** | | US1 | **R** | New e2e: create a **two-label** overlay through the API, bind it to a tile, assert **two** `camera-viewer-overlay-label` nodes in ordinal order with the second's text resolved from a variable. New test classes/specs need their shard-filter entry. **`kiosk-shows-a-label-over-video.spec.ts` must pass unmodified** — it now seeds a nine-tile single-label wall and reads `.first()`, and is the end-to-end characterisation that the single-label wall did not move. |
| **T024** | | US1 | — | **FR-017.** Re-read `overlay_draw` p50/p95 on the nine-tile fixture with every tile carrying `MaxLabels` labels (9 × 8 = 72 nodes, ADR-0164's worst case). Do not edit the characterisation e2e to do it: use a local variant or a dedicated spec, and read it through `e2e/support/render-leg.ts`. **Run twice**, quote both beside T003's baseline (spec 225: mean p50 56.63 ms), ADR-0123's p50 54.2 / p95 79.2 ms, and this PR's own CI render-leg summary. This is how §IV's "demonstrate the budget still holds" is discharged while #2337's gate is report-only. *(D1, D10.)* |
| **T025** | | US2 | — | Quote `NFR_VariableResolutionLatencyTests`' printed median, run twice, for the event → overlay state leg (spec 148's precedent for the same loop). |

---

## Dependency summary for the orchestrator

```
T001 T002 T003            (parallel, all block Phase 1)
        │
        ├─ T004 → T005 → T006      ─┐  foundational: everything waits
        ├─ T007 [P]                 │
        └─ T008 [P]                ─┘
                  │
        ┌─────────┼─────────────────────────────┐
        │         │                             │
  T009 → T010     ├─ T014 [P] T015 [P] T016 [P] │  T017 [P] T018 [P] T021 [P] T022 [P] T026 [P]
  T011 → T012     │                             │        │
       → T013     │                             │        └─ T019 → T020
                  │                             │
                  └──────────── T023 → T024, T025
```

**Fan-out points:** after T006+T007+T008, Phases 2, 3 and 4 are three independent
lanes — backend persistence/API, the two consuming contexts, and the frontend — and
only T023 rejoins them.

**The two scope tripwires**, both checkable without reading code:
`OverlayEditor.tsx` must not appear in the diff (FR-016), and the existing guard files
(`OverlayLabelCharacterisation`, `OverlayLabelParity`, the four `OverlayEditor*` tests,
and `e2e/overlays.spec.ts`'s expected values) must show **no assertion changes**.

**The two silent-break tripwires** this re-verification added, both of which compile
cleanly when wrong: `ReverseIndexSeederHostedService` reading `"labels"` (T014), and the
console's edit PATCH carrying the whole set (T021).
