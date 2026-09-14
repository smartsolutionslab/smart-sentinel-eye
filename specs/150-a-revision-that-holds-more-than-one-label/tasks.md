# Tasks 150 — A revision that holds more than one label

`[ID] [P?] [Story]` — `[P]` marks tasks owning **disjoint files** that may run
concurrently (ADR-0109). Everything in Phase 1 is foundational and blocks the fan-out;
say so to the orchestrator rather than discovering it.

**Colour** is per artefact, per `plan.md` §"Phase 4a". `C` = characterisation, observed
**green before** the change and passing unmodified after. `R` = red, observed failing,
output quoted in the PR. `—` = implementation, no colour of its own.

**Engineers:** `backend-engineer` (T004–T016, T022), `frontend-engineer` (T001,
T017–T021, T023). T002/T003/T024/T025 are measurement, run by whoever holds the stack.

---

## Phase 0 — capture the baseline (must complete before any change)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T001** | [P] | US3 | **C** | New `OverlayLabelNodeCountCharacterisation.test.tsx` in `apps/shared/src/ui/composites/`. Assert **exactly one** `camera-viewer-overlay-label` node for a tile with an overlay, **zero** for a tile without, and that the node is a **direct child** of the `relative aspect-video` container. Observe green. This is the gap the existing four guards leave (`plan.md` §Phase 4a) and it is FR-013's only guard. |
| **T002** | [P] | US3 | **C** | Run and record green: `OverlayLabelCharacterisation`, `OverlayLabelParity`, `OverlayEditorCharacterisation`, `OverlayEditorBackdrop`, and the backend `OverlayRevisionStateMachineTests` / `OverlayChainArchivalTests` / `TimestampOrderingTests`. Quote the pass counts in the PR — this is the "before" half of the characterisation obligation. |
| **T003** | [P] | US1 | — | Record the **pre-change** `overlay_draw` p50/p95 from `e2e/kiosk-shows-a-label-over-video.spec.ts` on a single-label wall. Run twice (memory: the first run after machine churn reads like a regression). This is the comparison basis for T024. |

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
| **T014** | [P] | US2 | **R** | SystemVariables: `labelByOverlay` → `Dictionary<Guid, IReadOnlyList<string>>`; `UpsertOverlayReferences(Guid, IReadOnlyList<string>)`; `LookupLabelText` → `LookupLabelTexts`. **`byName`, `versionByOverlay`, `RemoveOverlay`, `LookupOverlays`, `NextVersionFor`, `CurrentVersionFor` are unchanged** — FR-011, and the reviewer should check a finer key did not creep in. `ResolvedOverlaySnapshotDto.ResolvedText` → `ResolvedTexts`; `GetOverlaySnapshotQueryHandler` resolves each and bumps the version **once**. `ResolveOverlayTextQueryHandler` untouched. |
| **T015** | [P] | US2 | **R** | LayoutComposition: `OverlayRevisionPublishedHubMessage` and `ResolvedOverlayTextChangedHubMessage` carry the collections; handlers relay them. Per-fab fan-out loop unchanged. Watch the destructuring — `HandlerDeconstructionTests` fails the build on a local named after another field of the same record. |
| **T016** | [P] | US1 | — | Update the remaining V1 consumers the clean cut leaves stranded: `IntegrationEventAuditHandler` (AuditObservability), `EventMetadataFabDeclarationTests`, `Shared.Contracts.Tests`' `OverlayRevisionPublishedV1Tests`. Compile-driven; finish before the frontend lands. |

---

## Phase 4 — the wall and the clients (depends on T007/T008; T019 depends on T001)

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T017** | [P] | US1 | **R** | `overlays.api.ts`: **drop `OverlayRevision extends OverlayLabel`** — the finding itself — for `labels: OverlayLabel[]`. `PublishedOverlay.text` → `labels`; `CreateOverlayDraftInput.label` → `labels`; edit body `{ label }` → `{ labels }`. `overlays.schema.ts`: `z.array(overlayLabelSchema).min(1).max(8)`. **Pin the 8 against the domain `const` with a test** — two hand-copied bounds is how #2361 happened. |
| **T018** | [P] | US2 | **R** | `layoutHub.ts`: the two message types take collections; `upsertQueryData` writes the list. **`overlayTextVersionsRef` stays `Map<overlayIdentifier, number>`** (FR-011). |
| **T019** | | US1 | **R** + **C** | `CameraViewer.tsx`: `overlay?` → `overlays?: readonly CameraViewerOverlay[]`, single render site → `.map()`. **No wrapper element** (a fragment is fine, a `<div>` is a defect); **zero nodes for an empty/absent list**; `key` is the **ordinal**, not the index and not the text (FR-004 permits duplicate text). `overlayLabelSurfaceStyle` unchanged and called once per label. **T001, `OverlayLabelCharacterisation` and `OverlayLabelParity` must pass with only their prop-construction lines edited — an edited assertion is a block.** Depends on T001, T017. |
| **T020** | | US1/US2 | **R** | `CellPage.tsx`: build the render list by zipping the published revision's `labels` geometry with the resolved texts positionally. **One `useLabelDelay` per tile, taking and returning the set** (FR-014) — hooks cannot be called in a loop, and the set is versioned atomically so it shares an age. Depends on T017–T019. |
| **T021** | [P] | US1 | — | `management-web`: `OverlayEditorDialog.tsx` only — `Controller name="label"` → `name="labels.0"`, `DEFAULT_INPUT` wraps in an array. `OverlaysPage.tsx` row summary reads `labels[0].text`. **`OverlayEditor.tsx` must not appear in the diff** (FR-016) — it is PR #2362's file, and `OverlayEditorCharacterisation`/`OverlayEditorBackdrop` staying byte-identical is the mechanical check. |
| **T022** | [P] | US1 | — | `ScenarioSimulator`: `OverlayDesignerClient.EnsureOverlayAsync` + `CreateOverlayBody` take a list; `ScenarioSeeder` passes one element. `Seeding/OverlayLabel.cs` unchanged. |

---

## Phase 5 — observe it, and pay §IV

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T023** | | US1 | **R** | New e2e: create a **two-label** overlay through the API, bind it to a tile, assert **two** `camera-viewer-overlay-label` nodes in ordinal order with the second's text resolved from a variable. **`kiosk-shows-a-label-over-video.spec.ts` must pass unmodified** — it asserts one tile and `.first()`, and is the end-to-end characterisation that the single-label wall did not move. |
| **T024** | | US1 | — | **FR-017.** Re-read `overlay_draw` p50/p95 with tiles carrying `MaxLabels` labels. **Run twice**, quote both runs beside T003's baseline and ADR-0123's p50 54.2 / p95 79.2 ms. This is how §IV's "demonstrate the budget still holds" is discharged without #2337 existing. |
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
  T009 → T010     ├─ T014 [P] T015 [P] T016 [P] │  T017 [P] T018 [P] T021 [P] T022 [P]
  T011 → T012     │                             │        │
       → T013     │                             │        └─ T019 → T020
                  │                             │
                  └──────────── T023 → T024, T025
```

**Fan-out points:** after T006+T007+T008, Phases 2, 3 and 4 are three independent
lanes — backend persistence/API, the two consuming contexts, and the frontend — and
only T023 rejoins them.

**The two scope tripwires**, both checkable without reading code:
`OverlayEditor.tsx` must not appear in the diff (FR-016, PR #2362's file), and the four
existing guard files must show **no assertion changes**.
