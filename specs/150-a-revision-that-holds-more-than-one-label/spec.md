# Spec 150 — A revision that holds more than one label

**Issue:** #2345 · **Branch:** `feat/2345-a-revision-that-holds-more-than-one-label`
**ADRs:** ADR-0112 (the governing precedent), ADR-0104, ADR-0073, ADR-0040,
ADR-0123, ADR-0113, ADR-0067, ADR-0142, ADR-0129. Constitution §II, §III, §IV, §VII.

**Phase 4a colour:** **two colours.** CHARACTERISATION (green, captured before any
change) on the wall's single-label render and on the editor it must not touch; RED on
everything that carries a second label. Reasoned out in `plan.md` §"Phase 4a".

---

## The decision this spec had to make first

The issue asks which of two shapes a multi-label overlay takes, and says the spec must
say *why* rather than assume.

**Shape 1 — a revision holds many labels, published atomically — is chosen.**

It is chosen on precedent, not on taste. **LayoutComposition already made this exact
decision for this exact kind of change.** ADR-0112 took a `Layout` revision that bound
*one* camera plus one optional overlay and extended it to hold an ordered, non-empty
set of `Tile`s. Its §1 decision — *extend the existing aggregate's payload; no new
aggregate* — is shape 1. Its rejected **Alternative A** — a separate aggregate
composing N independent chains — is shape 2, rejected because it "fractures the
lifecycle (publish/archive/highlight would have to coordinate across N independent
chains)" and "re-introduces the exact 'is a wall one thing or N things' ambiguity".

Every word of that transfers. ADR-0104 is the other half: it measured the
OverlayDesigner/LayoutComposition duplication and found the revision **payload** is
precisely the part that legitimately varies between the two contexts — "Overlay
carries a `Label`; Layout carries a camera + an optional overlay binding". Changing
the payload from a scalar to a collection is the variation both ADRs anticipated. No
new aggregate appears, so ADR-0104's rule-of-three is not triggered.

Three consequences follow from shape 1, and the third is the one that shrinks this
feature by more than anything else in it:

1. **Publish stays one action on one chain.** The Draft→Published→Archived state
   machine, the at-most-one-Published partial unique index, branch-on-edit and revert
   are all untouched. Only the payload changes.
2. **Editing one label creates a new revision of all of them.** This is the honest
   cost of shape 1 and it is accepted: an overlay is "this camera's annotation", one
   thing with one history.
3. **The resolution pipeline keeps its per-overlay key.** A survey of SystemVariables
   found `IReverseIndex` keyed on `Guid overlayIdentifier` at every axis —
   `labelByOverlay`, `versionByOverlay`, `LookupLabelText`, `NextVersionFor` — and that
   reads at first like the crux of the change. Under shape 1 it is not. Labels are
   resolved and pushed **as a set, under one version bump per overlay**, so the index's
   *key* never changes: only its cached *value* widens from one string to a list. The
   kiosk's `overlayTextVersionsRef: Map<overlayIdentifier, number>` is likewise
   untouched. Shape 2 would have forced a finer key through five interface members, a
   new per-label version counter, and a fan-out the broadcaster does not have.

### What happens to existing single-label revisions

They become one-label revisions, losslessly, in a single EF migration — the pattern
ADR-0112 §3 already used for tiles. Add `overlay_revision_labels`; backfill every
existing revision's six `label_*` columns into exactly one row at `ordinal = 0`; drop
the six columns **in the same migration**. No read window, no follow-up retirement
task, zero data loss. Every overlay published before this feature keeps rendering,
because it is now a one-element set and a one-element set renders as it always did.

---

## User stories

### US1 — An operator annotates a camera with more than one fact (P1)

*As a control-room operator, I want one overlay to carry several labels, so that a
camera cell can show a station name and a live reading without me creating and
publishing two unrelated overlays.*

This is the whole of P1 and the whole of this spec's user-visible value. It is
independently shippable and independently observable: create an overlay with three
labels through the API, publish it once, and see three labels on the wall.

### US2 — A published multi-label overlay resolves every placeholder (P1)

*As an operator, I want `{{...}}` placeholders in the second and third labels to
resolve the same way they do in the first, so that a multi-label overlay is not a
downgrade.*

Carried by P1 because a label that renders its placeholder literally is not "rendering
correctly on the wall".

### US3 — The existing single-label overlays keep working (P1)

*As an operator, I want every overlay I published before today to look exactly as it
did.* The migration and the characterisation guards are this story's implementation.

### Out of scope, deliberately

- **Authoring a second label in the management editor.** The issue's own scope line is
  *"a revision can carry more than one text label, published atomically, rendering
  correctly on the wall"* — it does not mention the editor, and #2343 is the tracker
  that holds editor work. `OverlayEditor.tsx` is **not touched by this spec**; the
  create dialog sends a one-element list. See §"Follow-on work".
- **Z-order (#2348)** and **non-text primitives (#2349)** — filed separately.

---

## Acceptance scenarios

### Happy path

```gherkin
Scenario: A revision is created with three labels and published once
  Given an operator holding scope sse.overlays.write
  When they POST /overlays with a name and three labels
  Then the response is 201 Created
  And GET /overlays/{id} returns one revision whose labels array has three entries
  And the entries are in the order they were submitted
  When they POST /overlays/{id}/revisions/1/publish with a matching If-Match
  Then the response is 200 OK
  And exactly one OverlayRevisionPublishedV2 is emitted, carrying all three labels
  And no per-label publish event is emitted
```

```gherkin
Scenario: The wall renders every label of a published revision
  Given a published overlay carrying three labels bound to a tile
  When the kiosk renders that tile
  Then three overlay-label nodes are painted inside the tile
  And each is positioned from its own normalized geometry
  And they are painted in ordinal order
```

```gherkin
Scenario: A placeholder in the third label resolves like one in the first
  Given a published overlay whose first and third labels each contain {{line-1-temp}}
  When the value of line-1-temp changes
  Then one ResolvedOverlayTextChangedV2 is emitted for that overlay
  And it carries the resolved text of all three labels
  And its version is the overlay's version incremented once
  And both the first and the third label show the new value on the wall
```

```gherkin
Scenario: A single-label overlay published before this feature is unchanged
  Given an overlay published before the migration ran
  When the kiosk renders its tile
  Then exactly one overlay-label node is painted
  And its computed style and geometry are identical to before the migration
```

### Conflict

```gherkin
Scenario: A stale If-Match loses the race
  Given two operators holding the same overlay at version 4
  When the first edits the draft's label set and succeeds
  And the second edits the draft's label set with If-Match: 4
  Then the second receives 409 Conflict
  And the label set is the first operator's, entire — not a merge of the two
```

```gherkin
Scenario: Editing a published revision's labels is refused
  Given a revision in the Published state
  When an operator PATCHes its label set
  Then the response is 409 Conflict
  And the published label set is unchanged
```

### Bad request

```gherkin
Scenario: A revision with no labels is refused
  When an operator POSTs /overlays with an empty labels array
  Then the response is 400 Bad Request
  And the problem detail names the empty label set
```

```gherkin
Scenario: More labels than the ceiling allows is refused
  When an operator POSTs /overlays with MaxLabels + 1 labels
  Then the response is 400 Bad Request
  And the problem detail names the ceiling
```

```gherkin
Scenario: One bad label rejects the whole set
  When an operator POSTs /overlays with two valid labels and a third of zero width
  Then the response is 400 Bad Request
  And no overlay is created
  And the problem detail names normalizedWidth, as it does for a single label today
```

### Auth

```gherkin
Scenario: A caller without the write scope cannot create a multi-label overlay
  Given a caller authenticated without sse.overlays.write
  When they POST /overlays with two labels
  Then the response is 403 Forbidden
```

```gherkin
Scenario: A caller without the read scope cannot read one
  Given a caller authenticated without sse.overlays.read
  When they GET /overlays/{id}
  Then the response is 403 Forbidden
```

### Idempotency (ADR-0142, already wired on this route)

```gherkin
Scenario: A retried create with the same key returns the first answer
  Given an operator POSTs /overlays with three labels and Idempotency-Key: k1
  When the same operator replays the identical request with Idempotency-Key: k1
  Then the response is the original 201 and the original identifier
  And exactly one overlay exists
```

---

## Functional requirements

- **FR-001** A revision carries an ordered set of 1..`MaxLabels` labels. `MaxLabels`
  is a domain constant, not configuration.
- **FR-002** `MaxLabels = 8`. Justified in §"The ceiling" below.
- **FR-003** A revision with zero labels is refused, at the aggregate and at the
  command boundary — `Result<T, Error>` 400 for the operator, an
  `InvalidOperationException` backstop inside the aggregate. This is
  `Layout.ValidateGrid`'s two-tier shape (`GridViolation.Empty`) reproduced.
- **FR-004** Two labels **may** overlap and **may** share geometry or text. No
  uniqueness invariant is introduced. Overlap is the premise of #2348, not a defect
  this spec prevents.
- **FR-005** Label order is explicit and stable: an `ordinal`, zero-based and dense,
  which is both the EF key discriminator and the paint order. It is not an
  operator-facing z-order control — that is #2348 — but it makes paint order
  *deterministic* rather than incidental, which is the half of #2348 that cannot wait.
- **FR-006** An edit replaces the whole label set wholesale. There is no per-label
  mutator, matching `EditDraftRequest`'s existing "an overlay edit always replaces the
  Label wholesale" and `Revision.ReplaceTiles`.
- **FR-007** Branching a draft deep-copies every label of the base revision, including
  each label's owned `Position` and `Size`. `Revision.Branch`'s existing comment
  documents why a shallow copy re-keys an EF-owned entity and throws; with a
  collection this applies per element.
- **FR-008** `OverlayRevisionPublishedV1` is replaced by `OverlayRevisionPublishedV2`
  carrying `IReadOnlyList<OverlayLabelV2>`. V1 is **deleted in the same feature** — a
  clean V2 cut, per ADR-0112 §3 and the `LayoutRevisionPublishedV2` precedent, which
  deleted its V1 in the same commit. There is no dual-publish window: every consumer
  is in this repo.
- **FR-009** `ResolvedOverlayTextChangedV1` is replaced by
  `ResolvedOverlayTextChangedV2` carrying the resolved text of the whole set under one
  `Version`. Same clean cut.
- **FR-010** `OverlayRevisionArchivedV1` is **unchanged** — it carries no label
  payload, exactly as `LayoutRevisionArchivedV1` was left unchanged by ADR-0112.
- **FR-011** The reverse index's key stays `Guid overlayIdentifier`; only its cached
  value widens to a list. One version bump per overlay per change.
- **FR-012** The wall renders every label of the bound published revision, each a
  direct child of the tile's aspect-ratio box so the existing percentage geometry keeps
  its containing block. No wrapper element is introduced.
- **FR-013** A tile with no bound overlay, or an unavailable overlay, renders **zero**
  label nodes — an empty list must not emit a stray node or an empty wrapper.
- **FR-014** The whole label set ages as a unit through the existing label-delay hook
  (ADR-0129): one `useLabelDelay` per tile, not one per label. The set is resolved and
  versioned atomically, so a shared age is the consistent reading, and it keeps the
  React hook count per tile fixed.
- **FR-015** The migration backfills each existing revision into exactly one label at
  `ordinal = 0` and drops the six `label_*` columns in the same migration.
- **FR-016** `OverlayEditor.tsx` is not modified by this spec.
- **FR-017** The PR quotes a measured `overlay_draw` figure taken at full cardinality.
  See §"Latency budget impact".

### The ceiling

`MaxLabels = 8`, set the way ADR-0112 §4 set `MaxTiles = 4`: a domain invariant with a
stated reason, not a config knob, and raised only by a future measured ADR.

An overlay annotates one camera cell with a handful of facts — a station name, a
reading, a state. Eight is generous for that and still bounds the wall: a wall is
capped at `MaxTiles = 4` (ADR-0112 §4, a domain invariant), so the absolute ceiling
this feature puts on a wall is **4 × 8 = 32 label nodes**, against the 4 it can carry
today. Nothing about the 250-camera fab target bears on this number, for the reason
ADR-0112 already gave: a wall is a handful of correlated cameras, not the fab.

---

## Locked tech choices

Nothing new is chosen. Everything below is an existing decision this spec applies.

| Concern | Choice | Authority |
|---|---|---|
| Aggregate shape | Extend the existing `Overlay`; no new aggregate | ADR-0112 §1, ADR-0104 |
| Label identity | Value object, no identity of its own; natural composite key | ADR-0112 §2, `Tile` |
| Persistence | EF Core owned collection → `overlay_revision_labels`, key `(revision_id, ordinal)` | `LayoutConfiguration`'s `layout_revision_tiles` |
| Migration | One EF migration via `MigrationRunner`; backfill then drop | ADR-0067, ADR-0112 §3 |
| Contract versioning | Clean `V2` cut, V1 deleted in the same feature | ADR-0073, ADR-0112 §3 |
| Wire types | Primitives only in `Shared.Contracts` | ADR-0040 |
| Set invariants | `Option<LabelSetViolation>` + two-tier guard | `Layout.ValidateGrid` / `GridViolation` |
| Concurrency | `If-Match` expected version + EF token; no retry-on-conflict | ADR-0113, ADR-0043 |
| Idempotency | Existing `Idempotency-Key` on `POST /overlays`, unchanged | ADR-0142 |
| Guards | `Ensure.That(...)`; `Result<T, Error>` at the command boundary | ADR-0105, ADR-0047 |
| Label aging | One delay per tile, set aged as a unit | ADR-0129 |

---

## Latency budget impact (constitution §IV)

**Two legs are touched. Both must be cited in the PR with a figure.**

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No — tile count is unchanged; no new peer connection |
| Presentation buffer | ≤ 200 ms | No — FR-014 keeps one delay per tile |
| Event → overlay state | ≤ 200 ms | **Yes** — the resolution loop substitutes over a set |
| **Overlay composite + render** | **≤ 50 ms** | **Yes** — up to 8 label nodes per tile instead of 1 |
| Headroom | ≤ 150 ms | Arithmetic remainder |

### Overlay composite + render — what the per-tile cost becomes

Today: **one** absolutely-positioned `<span>` per tile, styled by
`overlayLabelSurfaceStyle`. After: **up to `MaxLabels` such spans**, from the same
style function, with no new compositing layer, no filter, no animation and no change
to the containing block (FR-012). The per-wall ceiling moves from 4 nodes to 32.

**This leg is the only one in §IV marked `Implemented: yes | Measured: yes`,** and the
figure behind that `yes` is ADR-0123's real-wall reading: **p50 54.2 ms, p95 79.2 ms,
max 164.6 ms** (#1891). That p50 is already **above** the 50 ms budget, which is why
this spec does not get to call a small increment free. ADR-0123 also explains why the
figure is what it is: the leg is defined as *the operator's wait, frame-wait included*,
so it is dominated by frame cadence — ADR-0123 derives ≥30 Hz for the median to hold —
rather than by node count. Twenty-eight additional small spans, sharing one style
function and one containing block, are a style-recalc and paint cost of a different
order from a 33 ms frame wait.

That is the *expectation*. §IV requires a demonstration, so:

**FR-017 — the PR must quote a measured `overlay_draw` figure taken at full
cardinality.** The instrument already exists — `reportKioskLatency('overlay_draw', …)`
in `apps/shared/src/observability/kioskLatency.ts`, the same one that produced
ADR-0123's numbers — and `kiosk-shows-a-label-over-video.spec.ts` already reads it
against `budget 50 ms (section IV composite + render)`. Run it against a wall whose
tiles carry `MaxLabels` labels, and quote the p50 and p95 beside ADR-0123's figures.
**Run it twice** and quote both: the first run after machine churn reads exactly like a
regression (spec 148 set this precedent for its own NFR figure).

### Does this need to wait on #2337?

**No — and the reason is worth stating precisely, because the issue framed this as an
open question.**

#2337 proposes a **CI regression gate**: a committed baseline with run-id and SHA that
fails the build when a change spends the leg. It does not exist. But §IV's actual
obligation on a PR is not "a CI gate exists" — it is *"cite which leg it affects and
demonstrate the budget still holds"*, a per-PR measurement. The instrument for that
demonstration **does** exist and has been used before (ADR-0123). FR-017 discharges
§IV by the means the constitution actually asks for.

Two caveats, both recorded rather than waved past:

- **Without #2337 nothing will catch the *next* change to this leg**, including a later
  raise of `MaxLabels`. That is #2337's point and this spec does not fix it.
- **§VII's dashboard obligation on this leg is outstanding and this spec does not
  discharge it.** Composite+render reads `Dashboard: no` while being an implemented
  leg, and §VII says a leg in that state "cannot ship further work". Spec 146 touched
  this same leg and recorded the same pre-existing gap (#1940 is where it is settled,
  and it is not settled here). This spec inherits that position rather than inventing a
  new one — but it is a real outstanding obligation and the reviewer should see it named.

### Event → overlay state

The resolution loop runs the same substitution over `N` strings instead of 1, per
affected overlay, and publishes **one** event where it published one before (FR-011 —
the fan-out count is unchanged; only the payload widens). Quote
`NFR_VariableResolutionLatencyTests`' printed median in the PR, run twice, as spec 148
required for the same loop.

---

## How #2353 and #2361 interact with this

Both are open, both are made more visible by a collection, and neither is fixed here.

**#2353 — box tile-relative, type viewport-relative.** A label's box is a percentage of
its tile; its font size is `clamp(min(12, fontSizePx/4)px, (fontSizePx/16)vw,
fontSizePx px)`, where `vw` is the **viewport**. With one label per tile the mismatch
is cosmetic on dense grids: the box shrinks as the grid densifies while the type does
not, so the text overflows its own box. **With N labels it stops being cosmetic**,
because N boxes shrink together inside one tile while N pieces of fixed-size type do
not — so the labels begin to overflow *into each other*, which a single label could
never do. This spec does not change either relativity, and must not: #2353's fix
(`cqw` + `container-type: inline-size`) repaints every published overlay and adds
layout containment to the render leg, and #2353 is itself blocked on #2337.

The practical consequence is a note for the operator-facing follow-on, not a code
change here: on a dense grid, eight labels in one tile will collide. Which leads
directly to:

**#2348 — z-order.** FR-005's ordinal partly defuses it. The issue's sharpest form is
that overlapping labels "would fall to accidental DOM/serialization order"; with a
dense zero-based ordinal that is both the EF key and the paint order, the order is
**deterministic and stable across a round-trip** from the day the collection exists.
What #2348 still owns is the *operator control* — bring-forward/send-back — and the
question of whether overlap should be prevented at all. This spec deliberately answers
the second half of that with FR-004: overlap is allowed.

**#2361 — `clamp01` allows a zero-sized label the domain rejects.** Pre-existing, and
this spec does not touch `clamp01` or `OverlayEditor.tsx` (FR-016), so it adds no new
way to reach it. It does multiply the exposure once an operator can author N labels —
N chances per session instead of one — so **#2361 should be fixed before the
multi-label editor follow-on**, not before this spec.

---

## Independent end-to-end test procedure

Runnable by a person against the run-mode Aspire stack, without reading any test code.

1. Boot the stack. Mint a token with `sse.overlays.write` and `sse.overlays.read` from
   Aspire's **proxied** Keycloak endpoint, not the container's mapped port.
2. `POST /overlays` with a name and **three** labels: one plain text at the top of the
   cell, one containing `{{<an existing variable>}}` in the middle, one plain text at
   the bottom. Expect **201**.
3. `GET /overlays/{id}`. Expect one revision whose `labels` array has three entries in
   submission order. Confirm the response no longer carries scalar `text` /
   `normalizedX` fields on the revision.
4. `POST /overlays/{id}/revisions/1/publish` with the `If-Match` from step 3's ETag.
   Expect **200**.
5. Bind the overlay to a tile of a published layout and open the kiosk wall.
   **Expect three labels painted in the tile**, at their three positions, the middle one
   showing the variable's *value* rather than `{{...}}`.
6. Change that variable's value. **Expect the middle label to update** and the other two
   to stay put. Confirm in the Aspire dashboard that **one**
   `ResolvedOverlayTextChangedV2` was emitted for the overlay, not three.
7. Read the run's `overlay_draw` p50 and p95 and record them beside ADR-0123's
   54.2 / 79.2 ms. Repeat the whole run once and record both sets (FR-017).
8. **The regression check:** open a wall tile bound to an overlay published *before*
   this feature. Expect exactly one label, looking as it always did.
9. **The empty check:** open a wall tile with no bound overlay. Expect **no** label node
   in the tile — not an empty box.

---

## Follow-on work this spec creates

Neither is in scope; both should be filed before this merges so the programme's
dependency graph stays honest.

1. **The overlay editor authors many labels.** `OverlayEditor.tsx` + the create dialog
   gain add / remove / select-a-label. **Depends on PR #2362 merging** (it rewrites
   `OverlayEditor.tsx`, +275/−9) and pairs naturally with #2348's z-order control and
   #2361's `clamp01` fix. Until it lands, a multi-label overlay is authored through the
   API — which is exactly the boundary the issue's own scope line draws.
2. **Raise `MaxLabels`, or don't.** A future measured ADR, gated on #2337's gate
   existing, in the shape ADR-0112 §4 used for `MaxTiles`.
