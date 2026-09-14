# Plan 150 — A revision that holds more than one label

Implements `spec.md`. The governing precedent throughout is **ADR-0112**: this is the
`Layout`/`Tile` change applied to `Overlay`/`Label`. Where this plan looks
under-argued, the argument is in ADR-0112 and is cited rather than repeated.

---

## Bounded contexts touched

Five, and the boundary rule holds everywhere: no cross-context project reference; all
inter-context traffic through `Shared.Contracts` (`NetArchTest` enforces it).

| Context | What changes | Layers |
|---|---|---|
| **OverlayDesigner** | The revision payload becomes a label set | Domain, Application, Infrastructure, Api |
| **Shared.Contracts** | Two clean `V2` cuts | — |
| **SystemVariables** | The cached label value widens to a list | Application, Infrastructure |
| **LayoutComposition** | The relay and hub message carry the set | Application, Infrastructure |
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

Deliberately **no `DuplicateGeometry`**. `GridViolation.DuplicatePosition` exists
because two tiles in one grid cell is incoherent; two labels at one position is
overlap, which spec FR-004 allows and #2348 is about.

`Overlay.ValidateLabels(IReadOnlyList<Label>) → Option<LabelSetViolation>` is public
and is the single source of truth, exactly as `Layout.ValidateGrid` is. Two tiers,
same as LayoutComposition:

- **Operator tier** — command handlers call `ValidateLabels` first and map each case to
  its `OVERLAY_LABELS_*` 400 through `Result<T, Error>` (ADR-0047). An operator input
  error is a failure result, never a thrown exception.
- **Backstop tier** — a private `RequireValidLabels` called from `CreateDraft` and
  `EditDraft` throws `InvalidOperationException`. Reached only when a caller skips the
  handler, so it is a programmer-error backstop, not a second operator path.

`MaxLabels = 8` lives as a `const` on `Label`, next to `MaximumTextLength` — one source
of truth shared by the invariant and any future designer, the arrangement ADR-0112 §4
required of `GridDimensions.MaxTiles`.

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

Consumers to update in the same feature — the clean cut has no partial-deploy net:
`IntegrationEventAuditHandler` (AuditObservability), LayoutComposition's and
SystemVariables' `OverlayRevisionPublishedV1Handler`, and
`EventMetadataFabDeclarationTests` in Architecture.Tests.

---

## SystemVariables — the value widens, the key does not

This is the part that looks large and is not. Per spec §"The decision", shape 1 keeps
every key at overlay granularity.

| Member | Today | After |
|---|---|---|
| `byName` | `Dictionary<string, HashSet<Guid>>` | **unchanged** |
| `versionByOverlay` | `Dictionary<Guid, long>` | **unchanged** |
| `labelByOverlay` | `Dictionary<Guid, string>` | `Dictionary<Guid, IReadOnlyList<string>>` |
| `UpsertOverlayReferences(Guid, string)` | | `(Guid, IReadOnlyList<string>)` |
| `LookupLabelText(Guid) → string?` | | `LookupLabelTexts(Guid) → IReadOnlyList<string>?` |
| `RemoveOverlay`, `LookupOverlays`, `NextVersionFor`, `CurrentVersionFor` | | **unchanged** |

`UpsertOverlayReferences` already purges all of an overlay's prior name-references
before re-registering; with a list it registers the union of every label's placeholders
against the one overlay id. `RemoveOverlay` still drops the whole overlay in one call —
correct, because the set is archived as a set.

`ResolvedOverlaySnapshotDto` gains `IReadOnlyList<string> ResolvedTexts` in place of
`string ResolvedText`. `GetOverlaySnapshotQueryHandler` resolves each text and bumps the
version **once**. Its 404 (`OverlayNotInReverseIndex`) triggers on a missing overlay,
not a missing label — unchanged.

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
collections. `overlayTextVersionsRef: Map<overlayIdentifier, number>` is **unchanged**
(FR-011). The `upsertQueryData('getOverlaySnapshot', …)` call writes the list.

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

### `apps/kiosk-web/src/features/cell/CellPage.tsx`

`publishedOverlay` still comes from one `.find(r => r.state === 'Published')`. The
`renderOverlay` object becomes a list built by zipping the published revision's
`labels` geometry with the resolved texts, positionally.

**`useLabelDelay` stays one call per tile** (FR-014), taking and returning the set.
This is not a stylistic choice: hooks cannot be called in a loop, so a per-label delay
would force each label into its own component — a larger change, and an inconsistent
one, because the set is resolved and versioned atomically so its members share an age.
ADR-0129's aging semantics are preserved exactly: the set is held back by the tile's
measured frame age.

### `apps/management-web` — the deliberate non-change

`OverlayEditor.tsx` is **not touched** (FR-016). Only
`OverlayEditorDialog.tsx` rewires: `Controller name="label"` → `name="labels.0"`, and
`DEFAULT_INPUT` wraps its label in an array. The shared `OverlayEditor` keeps its
`value: OverlayLabel` / `onChange` props and goes on editing exactly one label; the
dialog decides which one.

**This seam is the reason this spec does not collide with PR #2362**, which rewrites
`OverlayEditor.tsx` (+275/−9: focus handling, arrow-key nudge, `aria-live`
announcements). Keeping the shared editor single-label is what makes the two branches
disjoint. `OverlaysPage.tsx`'s row summary reads `summarised.labels[0].text` for its
truncated preview — a list-aware summary is the follow-on's problem.

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
from normalized coords; flex centering; `rgba(255,255,255,0.85)` and no border;
`rgb(17,24,39)` + weight 600; `clamp(12px, 3vw, 48px)`; `padding: 0px 4px` and
`pointerEvents: none` — and, in Parity, that the wall's label and the editor's preview
agree on all six.

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

**`OverlayEditorCharacterisation.test.tsx` (7) and `OverlayEditorBackdrop.test.tsx`
(7): unmodified, and green throughout.** They cover `OverlayEditor.tsx`, which FR-016
forbids touching. Here they serve a second purpose beyond regression — they are the
mechanical check that this branch stayed out of PR #2362's file. **Any diff to these
two files is a scope breach.**

**`e2e/kiosk-shows-a-label-over-video.spec.ts`: unmodified, green.** It asserts one
tile and reads `getByTestId('camera-viewer-overlay-label').first()`; with a
single-label fixture it must pass untouched. That is the end-to-end characterisation
that the pre-existing wall is unchanged, and it is also the instrument FR-017's figure
comes from.

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
