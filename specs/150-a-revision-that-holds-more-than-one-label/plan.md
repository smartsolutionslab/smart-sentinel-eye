# Plan 150 — A revision that holds more than one label

Implements `spec.md`. The governing precedent throughout is **ADR-0112**: this is the
`Layout`/`Tile` change applied to `Overlay`/`Label`. Where this plan looks
under-argued, the argument is in ADR-0112 and is cited rather than repeated. The
ceiling is **ADR-0164** (`MaxLabels = 8`).

---

## Re-verification, 2026-10-04

This plan was written on 2026-09-14 and brought forward onto `develop` at `d84554c7`,
1096 commits later. Every claim below was re-read against the tree, not carried over.
Where this section and the body disagree, the body has been corrected to match this
section.

**Held, unchanged:**

- **Shape 1 and the migration.** No OverlayDesigner migration has landed since
  `20260907011959_OverlayNameUniquePerLiveChain`. `OverlayConfiguration` still maps the
  label as `OwnsOne` across the six `label_*` columns, and its doc-comment still has the
  "without joins" sentence. Create `overlay_revision_labels` → backfill at `ordinal = 0`
  → drop the six columns is still the right migration, and nothing else in the context
  has moved under it.
- **`overlays.api.ts`.** Still `interface OverlayRevision extends OverlayLabel`, with
  `PublishedOverlay.text: string`, the edit mutation body `{ label }`, and
  `createOverlayDraftSchema`'s `label: overlayLabelSchema`.
- **The contracts.** `OverlayRevisionPublishedV1` (six flattened label fields) and
  `ResolvedOverlayTextChangedV1(Overlay, ResolvedText, Version, Metadata)` are unchanged.
- **FR-011, overlay-granular keying.** Still true. It is now more firmly true:
  `VariableValueChangedDomainEventHandler` advances every affected overlay's version in
  **one** `IOverlayTextVersions.AdvanceAsync` round trip (#2426) and publishes one event
  per overlay. Multi-label resolves several texts under the version it already has.
- **The non-goal on `OverlayEditor.tsx`.** PR #2362 merged 2026-09-14. The file has
  changed since (#2342 tokens, #2361 clamp split, keyboard and undo), but its props are
  still `value: OverlayLabel` / `onChange(next: OverlayLabel)`. The single-label seam
  still exists and this spec still does not touch the file. No open PR touches any file
  this spec touches.
- **#2348.** Scope unchanged since 2026-09-13. FR-005's dense ordinal still covers the half
  of it that cannot wait.

**Moved, and corrected in the body:**

| # | What moved | Where it is corrected |
|---|---|---|
| D1 | **Wall cap 4 → 9** (ADR-0156, `GridDimensions.MaxTiles = 9`, check at `Layout.cs:80`). The per-wall label ceiling is 9 × 8 = 72, not 32. | spec §"The ceiling", §"Latency budget impact"; ADR-0164 |
| D2 | `GridViolation.DuplicatePosition` is now `GridViolation.Overlap` (spans, ADR-0156). | §"Set invariants" |
| D3 | **Version counting left `IReverseIndex`.** `versionByOverlay` / `NextVersionFor` / `CurrentVersionFor` are now the persisted `IOverlayTextVersions` (`6faa64c5`). The reverse index now holds only names → overlays and the cached label text. | §"SystemVariables" |
| D4 | **Missed in the original plan: `ReverseIndexSeederHostedService`.** It reads `GET /overlays?state=Published` as **untyped JSON**, takes `"text"` and `continue`s when that property is absent. Renaming `PublishedOverlayDto.Text` → `Labels` compiles, then on every cold start silently seeds **zero** overlays, so placeholders stop resolving after a SystemVariables restart. | §"SystemVariables", T014 |
| D5 | **Missed in the original plan: the events' real publishers.** `ResolvedOverlayTextChangedV1` is built in `VariableValueChangedDomainEventHandler` and `VariableArchivedDomainEventHandler`, not only in `GetOverlaySnapshotQueryHandler`. | §"SystemVariables", T014 |
| D6 | **Missed in the original plan: AuditObservability's `V1ResourceMap.Conventions`** hand-tweaks `ResolvedOverlayTextChangedV1` → `Overlay`. Compile-driven once V1 is deleted, but the V2 entry must be added, not just the V1 one removed, or the audit row loses its resource. | §"Shared.Contracts", T016 |
| D7 | **The wall's render moved.** `CellPage.tsx` is a 12-line shell. The tile is `Tile` in `apps/kiosk-web/src/features/cell/LayoutGrid.tsx` (spec 258 T063), and the hub handlers are in `useOverlayHubHandlers.ts` (spec 229). The tile now also derives `hasPlaceholder` / `labelTextKnown` / `onLabelVerdict` from the single `text`, and keys `measureOverlayDraw` on that string. | §"Frontend", T018, T020 |
| D8 | **#2353 and #2361 are closed** (spec 294 `9163bf00`; `0ca0e926`). Label type is now `cqw` against an `@container` box, so the "labels overflow into each other" hazard the spec raised is gone. | spec §"How #2353 and #2361 interact" |
| D9 | **Guard-file counts and assertions.** `OverlayEditorCharacterisation` is 10 tests (was 7), `OverlayEditorBackdrop` is 6 (was 7), and `OverlayEditorKeyboard` / `OverlayEditorUndo` / `overlayLabelStyle.test.ts` are new. `OverlayLabelCharacterisation`'s font assertion is now the `cqw` form, not `clamp(12px, 3vw, 48px)`. | §"Phase 4a", T002 |
| D10 | **The kiosk e2e is a nine-tile fixture** (`54b47034`) and writes a render-leg record CI prints (spec 225). Its baseline is in `specs/225-*/figures.md`: mean p50 56.63 ms over 21 runs. | §"Phase 4a", T003, T024 |
| D11 | **Missed in the original plan: `e2e/overlays.spec.ts`** reads `revisions[0].normalizedX` / `normalizedWidth` out of untyped GET JSON in three tests (`~:240`, `~:660`, `~:712`). They move to `revisions[0].labels[0].…` with the **asserted values unchanged**. | new T026 |
| D12 | **Missed in the original plan, and a data-loss path it created:** the console's edit flow builds its target with `labelOf(revision)` (`OverlaysPage.tsx:382`) and PATCHes `{ label: input.label }`. The plan's `name="labels.0"` with a one-element submit would truncate any multi-label overlay to one label on its first console edit, because FR-006 makes an edit wholesale. | spec FR-018; §"management-web", T021 |
| D13 | **#2349 gained scope** (2026-09-26): circle/ellipse and a colour field on a shared primitive base. No change here (spec §"Out of scope"), but whether non-text primitives count toward ADR-0164's cap is #2349's question to answer. | spec §"Out of scope" |

**Not a UI-free change.** The orchestrator's brief asked whether #2345 is still "not a
UI change (backend/domain + migration only)". The issue's own sentence is "it is **not a
UI change** — it reaches the domain, the contracts **and the wall**": not *only* a UI
change. The wall's render (`CameraViewer`, `LayoutGrid`), the shared API and hub types,
and two management-web files are in scope, and they always were. Both engineer types are
needed (see `tasks.md`).

---

## Bounded contexts touched

Six (AuditObservability added 2026-10-04), and the boundary rule holds everywhere: no cross-context project reference; all
inter-context traffic through `Shared.Contracts` (`NetArchTest` enforces it).

| Context | What changes | Layers |
|---|---|---|
| **OverlayDesigner** | The revision payload becomes a label set | Domain, Application, Infrastructure, Api |
| **Shared.Contracts** | Two clean `V2` cuts | — |
| **SystemVariables** | The cached label value widens to a list | Application, Infrastructure |
| **LayoutComposition** | The relay and hub message carry the set | Application, Infrastructure |
| **AuditObservability** | Audits the V2 events; `V1ResourceMap` gains the V2 entry (D6) | Application |
| **Frontend** (`apps/shared`, `kiosk-web`, `management-web`) | The wall renders a list | — |

`ScenarioSimulator` is a seeding client, not a context; it follows the API shape.

---

## OverlayDesigner — Domain

### `Label` becomes an ordered member of a set

`Label` stays a `sealed record … : IValueObject` with `Text`, `Position`, `Size`,
`FontSizePx`. It gains **`ordinal`** — zero-based, dense — as its place in the set.

Follow `Tile` exactly (`src/LayoutComposition/Domain/Layout/Tile.cs`): the ordinal is a
**private field** mapped by EF as a field-backed property, exposed to the domain only
through a value object, never as a bare `int` property. `Tile` documents why in its own
XML comment, and `PrimitiveBoundaryTests` documents that `Tile.Row`/`Col` were once
`int`s and are the regression that shaped the rule.

Note the guard would not actually catch it here — `PrimitiveBoundaryTests` exempts a
member whose **declaring type implements `IValueObject`**, and `Label` does (which is
also why its existing `string Text` and `int FontSizePx` are legal while `Tile`'s ints
were not). **Mirror `Tile` anyway.** Passing a rule by an exemption is not the same as
being right, and the two types should not disagree about how an owned-collection key is
modelled.

`Label.From(...)` keeps its current guards unchanged — text non-empty and
≤ `MaximumTextLength`, font size in range, position and size non-null. Per-label
validation is untouched; only set-level validation is new.

### Set invariants — mirror `Layout.ValidateGrid`

A new `LabelSetViolation` enum beside `Label`, modelled on `GridViolation`:

```
Empty      — a revision must carry at least one label
TooMany    — the set exceeds MaxLabels
```

Deliberately **no `DuplicateGeometry` / `Overlap`**. `GridViolation.Overlap` (formerly
`DuplicatePosition`, renamed by ADR-0156's spans) exists because two tiles claiming one
grid cell is incoherent; two labels at one position is overlap, which spec FR-004
allows and #2348 is about.

`Overlay.ValidateLabels(IReadOnlyList<Label>) → Option<LabelSetViolation>` is public
and is the single source of truth, exactly as `Layout.ValidateGrid` is. Two tiers,
same as LayoutComposition:

- **Operator tier** — command handlers call `ValidateLabels` first and map each case to
  its `OVERLAY_LABELS_*` 400 through `Result<T, Error>` (ADR-0047). An operator input
  error is a failure result, never a thrown exception.
- **Backstop tier** — a private `RequireValidLabels` called from `CreateDraft` and
  `EditDraft` throws `InvalidOperationException`. Reached only when a caller skips the
  handler, so it is a programmer-error backstop, not a second operator path.

`MaxLabels = 8` (ADR-0164) lives as a `const` on `Label`, next to `MaximumTextLength` —
one source of truth shared by the invariant and any future designer, the arrangement
ADR-0112 §4 required of `GridDimensions.MaxTiles`.

### `Revision`

```
Label Label            →  IReadOnlyList<Label> Labels   (private List<Label> labels = [])
EditLabel(Label)       →  ReplaceLabels(IReadOnlyList<Label>)
```

`ReplaceLabels` mirrors `Revision.ReplaceTiles`: keep only the Draft-state guard here;
the aggregate validates the set before calling. Clear-then-AddRange.

`Revision.NewDraft` takes the collection and **clones every element**, the way
LayoutComposition's `NewDraft` clones each `Tile`. This is where FR-007 is satisfied,
and it is the single highest-risk line in the backend change. The existing
`Revision.Branch` comment already explains the failure it prevents — a `Label` is an
EF-owned entity keyed on its owner revision, and `Position`/`Size` are themselves owned
entities keyed on the `Label`, so a shallow `with` reproduces the re-keying throw one
level down. With a collection this must happen **per element**, and `Branch` becomes a
straight delegation to `NewDraft` exactly as LayoutComposition's does.

### `Overlay`

`CreateDraft`, `EditDraft` take `IReadOnlyList<Label>`. `BranchDraft` pre-fills from
the base revision's whole set. `Publish`, `Revert`, `ArchiveRevision`,
`RecomputeArchival` and the at-most-one-Published logic are **untouched** — that is
shape 1's payoff and the reviewer should check nothing crept in.

### Domain event

`OverlayRevisionPublishedDomainEvent.Label` → `Labels`. Domain events carry value
objects (ADR-0040); only the integration event flattens to primitives.

---

## OverlayDesigner — Infrastructure

### The owned collection

In `OverlayConfiguration`, the `revisions.OwnsOne(revision => revision.Label, …)` block
becomes `revisions.OwnsMany(revision => revision.Labels, …)` onto a new
`overlay_revision_labels` table, keyed `(revision_id, ordinal)` — the direct analogue
of `layout_revision_tiles` keyed `(revision_id, row, col)`.

Copy `LayoutConfiguration`'s field-mapping idiom verbatim: `labels.Property<int>
("ordinal")` must use the **field name exactly**, because EF refuses a field-only
property whose name differs — LayoutConfiguration's comment records that this cost
someone a rename.

The nested `Position` / `Size` owned references move down one level with the `Label`.
They stay `OwnsOne` with explicit column names and `Navigation(...).IsRequired()`;
the existing comment explains that the default naming produces `Position_X` and
nullable columns, which is issue #2022's shape. **That comment must survive the move.**

The current `OverlayConfiguration` doc-comment says the Label is flattened across six
columns "rather than mapped as a separate owned entity — kiosks need to render every
Published revision without joins". **That sentence stops being true** and must be
rewritten, not left. The join is now unavoidable and is the same one
LayoutComposition already pays for tiles.

The two existing revision indexes — `ux_overlay_revisions_number` and
`ux_overlay_revisions_one_published` — are unaffected.

### The migration (ADR-0067, one migration, no read window)

Ordered exactly as ADR-0112 §3 ordered the tile migration:

1. Create `overlay_revision_labels` — `revision_id` FK, `ordinal`, `label_text`,
   `label_x`, `label_y`, `label_width`, `label_height`, `label_font_size_px`.
2. **Backfill**: one row per existing revision at `ordinal = 0`, copying the six
   `label_*` columns across. Every revision has exactly one today and the columns are
   `NOT NULL`, so the backfill is total and cannot produce an empty set — which is
   what makes FR-003's invariant safe to switch on immediately.
3. **Drop** the six `label_*` columns from `overlay_revisions` in the same migration.

Hand-write the backfill SQL inside the generated migration. The generated file is one of
the places `Ensure.That` is explicitly not required (ADR-0105's exemption list).

`OverlayQuerySource` returns `dbContext.Overlays.AsNoTracking()` and the doc-comment
claims the owned `Revisions` collection is "pre-included". Owned collections are
included automatically, so this keeps working — but the query now materialises a
second owned level. **Check the generated SQL once** rather than assuming; a split
query or a missing include here is a silent N+1 on the kiosk's read path.

---

## OverlayDesigner — Application and Api

Collection-for-scalar, mechanically, at every hop:

| Type | Change |
|---|---|
| `CreateOverlayDraftCommand` | `Label Label` → `IReadOnlyList<Label> Labels` |
| `EditDraftRevisionCommand` | same |
| `OverlayRevisionDto` | the six flattened fields → `IReadOnlyList<OverlayLabelDto> Labels` |
| `PublishedOverlayDto` | `string Text` → `IReadOnlyList<OverlayLabelDto> Labels` |
| `CreateOverlayRequest` | `LabelRequest Label` → `IReadOnlyList<LabelRequest> Labels` |
| `EditDraftRequest` | same |
| `CreateOverlayDraftErrors` / `EditDraftRevisionErrors` | add the two `OVERLAY_LABELS_*` cases |

`LabelRequest` itself is unchanged.

In `OverlayEndpoints.Commands.cs`, the existing `try { … } catch (ArgumentException ex)`
around `Label.From(...)` becomes a loop over the request's labels inside the same try.
**Keep one try around the whole loop**, so the first bad label rejects the whole set
with the existing `OVERLAY_INVALID_INPUT` 400 naming the offending field — that is the
acceptance scenario "one bad label rejects the whole set", and it falls out of the
existing shape rather than needing new error plumbing.

Set-level validation (`ValidateLabels`) runs in the **handler**, not the endpoint,
because it returns `Result` and the endpoint's job is parsing. That is the split
LayoutComposition already uses for `ValidateGrid`.

Everything else on these endpoints is untouched: scopes (`sse.overlays.read` /
`.write`), `If-Match` via `ConcurrencyHeaders`, the `Idempotency-Key` wiring on
`POST /overlays`, and every declared status code. **No route changes.**

---

## Shared.Contracts — two clean V2 cuts

```
OverlayRevisionPublishedV2(
    Guid Overlay, int RevisionNumber, string Name,
    IReadOnlyList<OverlayLabelV2> Labels,
    DateTimeOffset PublishedAt, Guid PublishedBy, EventMetadata Metadata)

OverlayLabelV2(
    string Text, decimal NormalizedX, decimal NormalizedY,
    decimal NormalizedWidth, decimal NormalizedHeight, int FontSizePx)

ResolvedOverlayTextChangedV2(
    Guid Overlay, IReadOnlyList<string> ResolvedTexts,
    long Version, EventMetadata Metadata)
```

Shaped on `LayoutRevisionPublishedV2` + `LayoutTileV2` — the nested primitive record in
the same file, primitives only (ADR-0040). Delete both V1 files in the same commit as
their V2, the way commit `a2768788` did ("clean V2 cut for LayoutRevisionPublished").

`ResolvedTexts` is a positional list, index-aligned with `Labels`. It carries no
ordinal of its own: the ordinal is dense and the two lists come from the same revision,
so position *is* the ordinal. A parallel-array contract is worth one sentence of
justification — an alternative carrying `(ordinal, text)` pairs would be more explicit,
and is the right change if ordinals ever become sparse, which #2348 could do. **If
#2348 lands first, revisit this.**

`OverlayRevisionArchivedV1` is untouched (FR-010).

Consumers to update in the same feature — the clean cut has no partial-deploy net.
Re-enumerated 2026-10-04 by `grep -rln "OverlayRevisionPublishedV1\|ResolvedOverlayTextChangedV1"`
over `src` and `tests`:

- **AuditObservability**: `IntegrationEventAuditHandler` (two `Handle` overloads), and
  `V1ResourceMap.Conventions.BuildHandTweaks`, which maps `ResolvedOverlayTextChangedV1`
  to `DomainResourceKind.Overlay` by `changed => changed.Overlay`. **Add the V2 entry
  with the same mapping.** Deleting V1 alone compiles once the line is removed, and the
  audit row then silently loses its resource. `V1ResourceMapTests` pins it.
- **LayoutComposition**: `OverlayRevisionPublishedV1Handler`,
  `ResolvedOverlayTextChangedV1Handler`, their registrations in
  `LayoutCompositionInfrastructureModule` (`:119`, `:121`), and a `<see cref>` in
  `WallSceneChangedV1Handler`'s doc-comment.
- **SystemVariables**: `OverlayRevisionPublishedV1Handler`, the two publishers
  (§"SystemVariables" below), and doc-comment references in
  `OverlayRevisionArchivedV1Handler`, `SystemVariableValueRequestedV1Handler`,
  `SystemVariablesInfrastructureModule` and `Log.cs`.
- **OverlayDesigner**: `OverlayRevisionPublishedDomainEventHandler` (the emitter).
- **Tests**: `Shared.Contracts.Tests` (`OverlayRevisionPublishedV1Tests`,
  `SystemVariables/ResolvedOverlayTextChangedV1Tests`), `EventMetadataFabDeclarationTests`,
  the handler tests in LayoutComposition/SystemVariables/OverlayDesigner Application
  tests, `ReverseIndexSeederHostedServiceTests`, and the integration tests
  `OverlayLifecycleIntegrationTests`, `VersionSurvivesARestartTests` and
  `CrossFabReadGuardIntegrationTests`. Integration tests build their request bodies
  through `tests/Integration.Tests/Fixtures/OverlayRequests.cs`, so the request-shape
  change lands there once.

---

## SystemVariables — the value widens, the key does not

This is the part that looks large and is not. Per spec §"The decision", shape 1 keeps
every key at overlay granularity.

`IReverseIndex` (`Application/Resolution/IReverseIndex.cs`, implemented by
`InMemoryReverseIndex`) — re-read 2026-10-04. Version counting is **no longer on this
interface**: it moved to the persisted `IOverlayTextVersions` (`AdvanceAsync`,
`CurrentAsync`, keyed `Guid overlayIdentifier`) in `6faa64c5`, and that interface is
**untouched** by this spec.

| `IReverseIndex` member | Today | After |
|---|---|---|
| `UpsertOverlayReferences(Guid, string labelText)` | | `(Guid, IReadOnlyList<string> labelTexts)` |
| `LookupLabelText(Guid) → string?` | | `LookupLabelTexts(Guid) → IReadOnlyList<string>?` |
| `RemoveOverlay`, `LookupOverlays`, `AllOverlays` | | **unchanged** |
| `IOverlayTextVersions.AdvanceAsync` / `CurrentAsync` | | **unchanged** (FR-011) |

`UpsertOverlayReferences` already purges all of an overlay's prior name-references
before re-registering; with a list it registers the union of every label's placeholders
against the one overlay id. `RemoveOverlay` still drops the whole overlay in one call —
correct, because the set is archived as a set.

**The three places that resolve and publish**, each doing the same thing: look up the
overlay's texts, resolve **each** against the snapshot, and emit **one** result carrying
the list under the version already advanced for that overlay.

- `VariableValueChangedDomainEventHandler`: `AdvanceAsync(affectedOverlays)` once, then
  per overlay `LookupLabelText` → resolve → `ResolvedOverlayTextChangedV1`. The
  `BuildSnapshotAsync(labelText, …)` call becomes one snapshot over the **union** of all
  labels' placeholders, not one snapshot per label. One repository round trip per
  overlay is the existing cost on the event → overlay state leg, and it must stay that.
- `VariableArchivedDomainEventHandler`: same shape (`:54`, `:58`, `:97`, `:100`).
- `GetOverlaySnapshotQueryHandler`: `ResolvedOverlaySnapshotDto` gains
  `IReadOnlyList<string> ResolvedTexts` in place of `string ResolvedText`. Its 404
  (`OverlayNotInReverseIndex`) triggers on a missing overlay, not a missing label —
  unchanged.

**`ReverseIndexSeederHostedService` — the silent one.** On start it calls
`GET /overlays?state=Published` on OverlayDesigner and parses the body as raw
`JsonElement`. For each element it reads `"overlayIdentifier"` and `"text"`, and
**`continue`s when either is absent**. When `PublishedOverlayDto.Text` becomes `Labels`,
nothing fails to compile and nothing throws. The seeder logs `SeededOverlays(0)`, and
after any SystemVariables restart every placeholder on every wall stops resolving until
each overlay is republished. It must read `"labels"` as an array of objects and take
each element's `"text"`. **Red test first** in `ReverseIndexSeederHostedServiceTests`: a
payload in the new shape seeds the overlay with all of its label texts. A
**counterfactual** check proves the test can fail: against the unchanged seeder, the
same payload must seed zero.

`ResolveOverlayTextQueryHandler` is **untouched**: it is keyed on a caller-supplied
string with no overlay lookup, so it is already per-string and a multi-label caller
simply calls it once per label. Spec 148's preview endpoint needs nothing.

---

## LayoutComposition — relay only

`OverlayRevisionPublishedHubMessage` and `ResolvedOverlayTextChangedHubMessage` take the
collection, mirroring their contracts. `SignalRLayoutLifecycleBroadcaster` keeps its
existing per-fab fan-out loop — the fan-out is over fabs, not labels, and does not
change. `ResolvedOverlayTextChangedV1Handler`'s destructure gains a field; the
`metadata.Fab` requirement and its silent FR-015 drop are untouched.

Note for the engineer: these handlers destructure their message first
(`var (overlay, resolvedText, version, metadata) = message;`). Deconstruction binds by
**position**, and `HandlerDeconstructionTests` fails the build if a local is named after
a different field of the same record. Rename deliberately.

---

## Frontend

### `apps/shared/src/api/overlays.api.ts`

```ts
export interface OverlayRevision extends OverlayLabel { … }   // the finding itself
```

becomes `OverlayRevision { …lifecycle fields…; labels: OverlayLabel[] }` — **`extends`
is dropped**. `PublishedOverlay.text` → `labels`. `CreateOverlayDraftInput.label` →
`labels`; the edit mutation body `{ label }` → `{ labels }`. `overlays.schema.ts`'s
`createOverlayDraftSchema` takes `z.array(overlayLabelSchema)` with `.min(1).max(8)` —
the Zod bound must be the same 8 as the domain `const`, and the two being written twice
is exactly how #2361 happened. **Reference a shared constant or add a test that pins
them equal.**

### `apps/shared/src/realtime/layoutHub.ts`

`OverlayRevisionPublishedMessage` and `ResolvedOverlayTextChangedMessage` take the
collections (types only — this file declares the messages, it does not handle them).

The handling moved (spec 229): **`apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts`**
owns `overlayTextVersionsRef: Map<string, number>`, which is **unchanged** (FR-011), and
the `systemVariablesApi.util.upsertQueryData(…, { resolvedText: message.resolvedText, … })`
call, which writes `resolvedTexts`. Its `resolved-text-for-static-label` resilience
event fires when a push arrives for an overlay whose verdict is "no placeholder". The
verdict becomes "**no label** in the set has a placeholder" (see `LayoutGrid` below).
`apps/shared/src/api/systemVariables.api.ts`'s `ResolvedOverlaySnapshot.resolvedText`
→ `resolvedTexts: string[]`. The resolve-preview DTO in the same file (`:100`, `:113`)
is per-string and **unchanged**.

`apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` passes
`OverlayRevisionPublishedMessage` through by type and reads no label field. It changes
only if the compiler says so.

### `apps/shared/src/ui/composites/CameraViewer.tsx` — the render leg

`overlay?: CameraViewerOverlay` → `overlays?: readonly CameraViewerOverlay[]`, and the
single render site becomes a `.map()`.

Three constraints, each a FR and each checkable in the diff:

- **No wrapper element** (FR-012). The spans must stay direct children of the
  `relative aspect-video` div, or the percentage geometry changes containing block. A
  `<>…</>` fragment is fine; a `<div>` is a defect.
- **Zero nodes when the list is empty or absent** (FR-013), not an empty box.
- **`overlayLabelSurfaceStyle` is called once per label and is not otherwise touched.**
  Spec 146 folded the editor and the wall onto this one function and `OverlayLabelParity`
  guards the fold; it stays the single source of the label's appearance.

The `key` for the mapped span is the **ordinal**, not the array index and not the text —
two labels may legitimately share text (FR-004).

### `apps/kiosk-web/src/features/cell/LayoutGrid.tsx` — the `Tile` component

*(Was `CellPage.tsx` in the original plan. `CellPage` is now a 12-line shell; the tile
was extracted into `LayoutGrid.tsx`'s `Tile` (`:321`) by spec 258 T063.)*

`publishedOverlay` still comes from one `.find(r => r.state === 'Published')`. The
`renderOverlay` object becomes a list built by zipping the published revision's
`labels` geometry with the resolved texts, positionally. Five other single-`text`
reads in the same component each change meaning, and each needs saying:

- **`hasPlaceholder`** (`publishedOverlay?.text?.includes('{{')`) → true when **any**
  label contains `{{`. It gates the snapshot fetch. Getting this wrong for labels 2..N
  means a placeholder in the third label never fetches its opening value.
- **`labelTextKnown`** → true when `labels` is an array and every element's `text` is
  a string.
- **`onLabelVerdict(overlayIdentifier, hasPlaceholder)`** stays one verdict per
  overlay, which is consistent with FR-011.
- **`liveText` fallback** (`snapshot?.resolvedText ?? publishedOverlay?.text`) → per
  index: `snapshot?.resolvedTexts[i] ?? labels[i].text`. Use positional zip, not a
  whole-list fallback, so that a snapshot shorter than the set (a race across a
  republish) cannot drop labels.
- **`measureOverlayDraw`'s effect is keyed on `overlayText`, a string** (`renderOverlay?.text`).
  Keying it on the new array would re-run on every render, because a fresh array is a new
  reference each time. That times renders that changed nothing and floods the
  distribution with near-zero samples, which is the exact defect #1888/#1889 fixed and
  ADR-0123 forbids "fixing" in the instrument. **Key it on a stable scalar derived from
  the set** (for example the resolved texts joined with a separator that cannot occur in
  label text), so it fires on a real change and only then.

**`useLabelDelay` stays one call per tile** (FR-014). Its signature today is
`useLabelDelay(text: string | undefined, frameAgeMilliseconds, onHeld?)`. It is
generalised to hold the set (or a stable scalar plus the list) rather than called once
per label. This is not a stylistic choice: hooks cannot be called in a loop, so a
per-label delay would force each label into its own component — a larger change, and an
inconsistent one, because the set is resolved and versioned atomically so its members
share an age. ADR-0129's aging semantics are preserved exactly: the set is held back by
the tile's measured frame age, and `label_delay` is still reported once per tile.

### `apps/management-web` — the deliberate non-change

`OverlayEditor.tsx` is **not touched** (FR-016). The shared `OverlayEditor` keeps its
`value: OverlayLabel` / `onChange` props (re-verified 2026-10-04) and goes on editing
exactly one label; the dialog decides which one.

**The dialog holds the whole set and edits index 0 (FR-018).** The original plan said
"`Controller name="label"` → `name="labels.0"`, `DEFAULT_INPUT` wraps its label in an
array". That is right for the *create* path and wrong for the *edit* path as written,
because it never said what the form holds or submits. Today:

- `OverlaysPage.tsx` builds the edit target with `label: labelOf(revision)` (`:85`
  after a branch, `:209` for an existing draft). `labelOf` (`:382`) destructures the six
  label fields off the revision, which works only because `OverlayRevision extends
  OverlayLabel`.
- `OverlayEditorDialog.tsx` submits `editDraftOverlayRevision({ …, label: input.label })`
  (`:200`).

After: the edit target carries `labels: revision.labels` (a copy of the whole array;
`labelOf` becomes `labelsOf` or is inlined). The form's value is the **whole**
`labels` array, the `Controller` binds `labels.0`, and the PATCH sends `labels:
input.labels`, all N of them, with index 0 edited. Three other reads in the dialog
follow the rename: `useWatch({ name: 'label.text' })` (`:164`, which drives the resolve
preview) → `'labels.0.text'`; `errors.label?.text` (`:323`, `:328`) →
`errors.labels?.[0]?.text`; and `defaultValues.label.text` → `defaultValues.labels[0].text`.

**Red test, in `OverlayEditorDialog.test.tsx`:** open an edit target carrying three
labels, change the visible text, save, and assert the mutation was called with three
labels: index 0 changed, 1 and 2 byte-identical. Against the plan as originally written,
this test fails with a one-element body.

**This seam is still why this spec does not collide with `OverlayEditor.tsx`.** PR
#2362 has merged, so there is no longer a parallel branch to collide with. The reason
to keep the shared editor single-label is now only scope: the multi-label editor is
follow-on work. `OverlaysPage.tsx`'s row summary (`:154`, `summarised.text`) reads
`summarised.labels[0].text` for its truncated preview — a list-aware summary is the
follow-on's problem. `apps/shared/src/format/revisionSummary.ts` (`a8091cec`) summarises
lifecycle fields, not label text. Touch it only if the compiler says so.

### `ScenarioSimulator`

`Seeding/OverlayLabel.cs` is unchanged; `OverlayDesignerClient.EnsureOverlayAsync` and
`CreateOverlayBody` take a list, and `ScenarioSeeder` passes a one-element list. The
seeded scenarios stay single-label.

---

## Phase 4a — the colour, split per artefact

Two colours, split per artefact rather than per branch — the arrangement specs 146, 147
and 148 each used, and the one ADR-0144 asks for when a feature carries both
obligations. Ambiguity resolves to red.

### CHARACTERISATION — green, captured before any change

**`OverlayLabelCharacterisation.test.tsx` (6 tests) and `OverlayLabelParity.test.tsx`
(6 tests).** These are the wall's guard and they are the reason this change is safe.
They pin, per property rather than as one style string: position/left/top/width/height
from normalized coords; flex centering; the surface's background and lack of border;
text colour and weight 600; the type size, which is now the container-relative
`cqw` form since spec 294 (`OverlayLabelCharacterisation.test.tsx:90`), not the old
`clamp(12px, 3vw, 48px)`; padding and `pointerEvents: none` — and, in Parity, that the
wall's label and the editor's preview agree on all six. Re-read the files for the exact
values at T002. This plan does not restate them, because two of them changed after it
was first written.

**Every assertion must be byte-identical after the change.** The `overlay` →
`overlays` prop rename means the *fixture line that constructs the prop* changes in
both files, and that is unavoidable — a prop-shape change cannot leave its call sites
untouched. The rule the reviewer enforces is therefore precise: **the diff on these two
files may touch only the prop construction. An edited assertion is a block, not an
adjustment** — it is evidence the label's appearance moved.

`OverlayLabelParity` is the single most valuable test in this feature: it proves the
wall's now-list-rendered label is still pixel-identical to the editor preview that this
spec does not touch at all.

**Two things the existing guards do not pin, and which must be captured green first:**

1. **Node count.** Both guards render one label and read its computed style; neither
   asserts *how many* nodes appeared. A `.map()` that emitted a stray node, or a
   wrapper, would pass all twelve. Add a characterisation test — observed green before
   the change — asserting exactly **one** `camera-viewer-overlay-label` node for a tile
   with an overlay and **zero** for a tile without. This is FR-013's guard and it is
   the specific gap this feature opens.
2. **DOM position.** That the label is a **direct child** of the `relative aspect-video`
   container. The style assertions read computed style and would survive a new wrapper,
   while the rendered geometry would silently shift with the containing block. Capture
   it green first.

**`OverlayEditorCharacterisation.test.tsx` (10), `OverlayEditorBackdrop.test.tsx` (6),
`OverlayEditorKeyboard.test.tsx` and `OverlayEditorUndo.test.tsx`: unmodified, and green
throughout** (counts re-read 2026-10-04; the original 7/7 were stale, and the last two
did not exist). They cover `OverlayEditor.tsx`, which FR-016 forbids touching. Here they
serve a second purpose beyond regression — they are the mechanical check that this branch
stayed out of the shared editor. **Any diff to these four files is a scope breach.**

**`e2e/kiosk-shows-a-label-over-video.spec.ts`: unmodified, green.** It now seeds a
**nine-tile** wall (`54b47034`, ADR-0156), not one tile. It reads
`getByTestId('camera-viewer-overlay-label').first()`, and its `armOverlayPaint`
(`:619-653`) observes `document.querySelector`'s first label only. With single-label
fixtures it must pass untouched. That is the end-to-end characterisation that the
pre-existing wall is unchanged. It is also the instrument FR-017's figure comes from,
and since spec 225 it writes the render-leg record CI's summary step prints. Its seeding
goes through the management UI (`e2e/support/seed-*-wall.setup.ts`), which uses the
dialog's create path, so FR-018's dialog change must keep that path sending a valid
one-element `labels`.

**`e2e/overlays.spec.ts`: three read paths change, no asserted value does.** Three
tests GET `/overlay-designer/overlays` and read `revisions[0].normalizedX` /
`.normalizedWidth` from untyped JSON (`~:240`, `~:660`, `~:712`). After the cut those
reads are `revisions[0].labels[0].…`. The expected values (`0.2487`, `0.5`, `0.9`)
must not change, by the same rule as the unit guards: an edited expected value is a
block.

**Backend characterisation:** the existing `OverlayRevisionStateMachineTests`,
`OverlayChainArchivalTests` and `TimestampOrderingTests` assert lifecycle, not payload.
They must pass with **only** their `Label.From(...)` call sites wrapped into
single-element lists — assertions untouched. Same rule, same reason: an edited lifecycle
assertion means shape 1 leaked into the state machine, which it must not.

### RED — observed failing, output quoted in the PR

Everything that carries a second label:

- `Overlay.ValidateLabels` — empty set, over-ceiling, and the boundary at exactly
  `MaxLabels`.
- A revision created with three labels; branch deep-copying all three (and each
  label's `Position`/`Size` — the FR-007 trap, which needs an integration test against
  the real EF stack, not a unit test, because it is an EF re-keying failure).
- `ReplaceLabels` wholesale replacement; the Draft-only guard.
- `OverlayRevisionPublishedV2` carrying three labels from one publish; **exactly one**
  event, not three.
- `ResolvedOverlayTextChangedV2` carrying three resolved texts under one version bump.
- The reverse index registering one overlay under placeholders drawn from several
  labels.
- The API's 400s: empty array, over-ceiling, one-bad-label-rejects-the-set.
- The migration: an integration test that a pre-migration single-label row arrives as a
  one-element set with its geometry intact.
- `CameraViewer` rendering three nodes in ordinal order.
- A new e2e: an overlay created with two labels via the API shows two labels on the wall.

The 409/403 scenarios are **existing** behaviour on unchanged code paths; cover them,
but they are regression cover, not new-behaviour red.

---

## Boundary rules the reviewer should check

- No new project reference between contexts. SystemVariables and LayoutComposition
  learn about labels only through `Shared.Contracts` records.
- No value object crosses into `Shared.Contracts` (ADR-0040). `OverlayLabelV2` is
  primitives.
- `Ensure.That(...)` for argument guards, never `ArgumentNullException.ThrowIfNull`
  (ADR-0105) — except in the generated migration.
- Collections declared with an explicit type and a collection expression:
  `List<Label> labels = [];`, not `= new()`. This fails the Release build.
- Handlers reading two or more fields destructure first; discard the unused.
- `Option<T>` over nullable parameters in Domain and Application (ADR-0141, advisory).
  `ValidateLabels` returns `Option<LabelSetViolation>`, matching `ValidateGrid`.
- ADR-0084 metrics: 300 LOC/file, 30 LOC/method, 4 params, complexity ≤ 10, depth ≤ 3.
  `OverlayConfiguration` is already long and gains a nesting level — if it breaches 300
  lines, split the configuration rather than suppressing the analyzer.
- Coverage gates: Domain ≥ 90%, Application ≥ 80%, Shared ≥ 90%.
