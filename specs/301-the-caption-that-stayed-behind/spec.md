# Spec 301: The caption that stayed behind

**Issue:** #2348 ("Overlapping overlay labels have no z-order") · **Branch:** not yet cut.
Cut it from `develop` **after spec 300 (#2349) has merged**, never from the #2349 branch
(see §"Dependency on spec 300").

**ADRs:** ADR-0112 §2 (an owned member has no identity, so it gets no per-member
mutator), ADR-0164 and ADR-0165 (the element set and its cap), ADR-0129 (labels are aged
as a set, and the hold fails open), ADR-0073 / ADR-0040 (contract versioning, primitives
on the wire), ADR-0123 (the composite-and-render measurement). Also spec 150 FR-004,
FR-005 and FR-006, and spec 300 FR-006, FR-009 and FR-012. Constitution §II, §III, §IV, §VII.
**No new ADR is needed** (plan §"Why no ADR").

**Written 2026-10-05.** The code it describes is the spec 300 tree (`D:\Github\sse-2349`,
branch `feat/2349-the-overlay-that-could-only-be-text`, head `e3671aef`). That branch had
**not** merged to `develop` (`a2be928d`) and had no PR when this was written. Every
`file:line` below is from that tree.

**Phase 4a colour:** **two colours, split per story** (plan §"Phase 4a").
- **RED:** US1 and US2. Both change what the wall paints during a republish.
- **CHARACTERISATION:** US3, which pins an ordering path that already works.

If the colour of an artefact is unclear, it is red.

---

## The premise, checked

The issue was filed before spec 150. It asks for four things. Three of them already
exist, and one exists in a form that is not safe.

| The issue asks for | State on the spec 300 tree |
|---|---|
| "An explicit ordering on the element collection" | **Exists.** A dense, zero-based `ordinal` is the EF key discriminator and the paint order (spec 150 FR-005). `Revision.Elements` sorts by it on every read (`Revision.cs:35`). It is guarded against heap order by `A_label_set_written_out_of_physical_order_still_reads_back_in_ordinal_order`. |
| "Carried through the contract" | **Exists.** `OverlayRevisionPublishedV3.Elements` is documented as "in paint order", and list position is the ordinal. A JSON array keeps its order. |
| "Authored in the editor (bring forward / send back, or an ordered list)" | **Missing, and premature.** Nothing in the console can select a second element (below). Through the API, a reorder already works: `PATCH` the whole set in the new order and `ReplaceElements` reassigns dense ordinals from list position (`Revision.cs:130-139`, `CloneWithOrdinals` at `:85`). |
| "Honoured by the wall's renderer rather than inherited from array order" | **Half true. This is the defect this spec fixes.** |

### What "honoured by the renderer" gets wrong today

Stacking is correct. `CameraViewer` paints each element as an absolutely positioned
direct child, in array order (`CameraViewer.tsx:424`). Nothing in `apps/` sets a
`z-index` on an overlay node (`tokens.css:281`: "Nothing in the tree sets a z-index
today"). So DOM order is paint order, and array order is ordinal order.

**What is not correct is how an element is paired with its text.** The wall builds each
painted node from two sources, matched **by array index**:

1. **Geometry, kind and colour** come from the published revision (`publishedOverlay.elements`).
   They change as soon as the overlay query refetches.
2. **The text** comes from `resolvedTexts[index]` (`LayoutGrid.tsx:478`). That is a
   positional list with two other timings:
   - **(a) It is aged.** `useLabelDelay` holds the text list back by the tile's frame
     age, up to `LABEL_DELAY_CAP_MS` = 200 ms (`LayoutGrid.tsx:469`). Geometry is not held.
   - **(b) It comes from a different context.** When an overlay has a placeholder, the
     text list is SystemVariables' snapshot or push (`LayoutGrid.tsx:438`). That list is
     index-aligned with whatever revision **SystemVariables** last indexed, and it
     carries nothing that says which revision that was (`ResolvedOverlayTextChangedV2`,
     `ResolvedOverlaySnapshotDto`).

As long as positions never change, pairing by index is harmless. **A reorder is exactly
the edit that changes them.** Take a revision `[Text "Line 1 temp: {{t1}}" at P, Text
"Line 2 temp: {{t2}}" at Q]` that is republished as `[Line 2 at Q, Line 1 at P]`:

- **Through (a), on every reorder:** for up to the tile's frame age, the node at Q
  paints *Line 1's* reading, and the node at P paints *Line 2's*. On a wall that shows
  readings beside the machines they belong to, that is a reading at the wrong machine.
- **Through (b), occasionally, with no time limit:** the kiosk's snapshot refetch
  (`useOverlayHubHandlers.ts:128`) and SystemVariables' own consumption of
  `OverlayRevisionPublishedV3` (`OverlayRevisionPublishedV3Handler`) run on independent
  queues. If the refetch reaches SystemVariables first, it returns the **old** order
  under the current version, the kiosk caches it, and nothing invalidates it until a
  placeholder's variable next changes. For a slow-moving variable, that can last the
  whole shift.
- **With a shape involved** (`[Box, Text "Zone A"]` becomes `[Text "Zone A", Box]`): the
  Text node now reads index 0, which held the Box's `""`. Because `"" ?? element.text`
  is `""`, **the caption paints empty** for the window.

**This is derived from reading code, not observed.** No one has watched a wall do it.
Both stories therefore open with a red test that has to fail on unmodified code. If
either one arrives green, the premise for that story is false: stop and report instead
of building (plan §"Phase 4a").

The issue warned that order would be "stable until something reorders it, then silently
different on the wall". Spec 150 made the order stable. What is still "silently
different on the wall" is not *which node is in front*. It is *which text is in which
node*.

---

## The decisions this spec makes

### 1. Overlapping elements are ordered, not prevented

The issue left this open: *"whether overlapping elements should be **prevented or merely
ordered**"*. **They are ordered. Overlap stays legal, and the later ordinal paints on
top.** This ratifies spec 150 FR-004 and does not reverse it. The reasons, strongest
first:

1. **Since spec 300, overlap is the main use, not the mistake.** Spec 300 US1 is "outline a
   machine, a doorway or a restricted zone" with a box and a caption. A caption *inside*
   its box, or an ellipse around a gauge with the reading inside it, overlaps by
   construction. A rule that refused overlap would refuse the feature that just shipped.
   The issue's "nearly always a mistake" was written when every element was an opaque
   text chip. It was true then. It is not true now.
2. **Rectangles are not ink.** An element's geometry is a box, not what is painted.
   - A text element's box is usually larger than its glyphs.
   - A Box or Ellipse is a stroke with a transparent interior (spec 300 FR-012).

   So "these two rectangles intersect" both over-reports (a caption inside an outline
   collides with nothing) and under-reports (two chips that merely touch can still
   crowd each other). A domain invariant built on it would refuse valid overlays and
   miss bad ones. That is the wrong precision for a write-time refusal.
3. **The only author of multi-element overlays today is the API** (spec 150 US1,
   spec 300 US1). A domain refusal would break scripted authoring, and there is no UI
   in which to fix the result.
4. **Spec 150 decided FR-004 one day before spec 300 shipped.** Reversing it needs a new
   fact. Reasons 1 and 2 are new facts, and both point the other way.

**A warning at authoring time is the right shape. This spec does not build it, and it is
the one product question this spec raises (§"Open question").** A warning is advisory,
so it belongs where an author can see it and act. Today there is no such place:
- the console edits exactly one `Text` element (spec 300 FR-019), so it cannot produce
  text-on-text overlap;
- the API answers with `Result<T, Error>`, either success or a 4xx. A "201 with
  warnings" would be new API vocabulary, and new vocabulary needs its own decision.

The warning therefore belongs to #2343's multi-element editor, which is the first
surface that can produce the overlap it would warn about.

### 2. Reordering is not a domain operation

An editor reorders by permuting the array on the client and sending the whole set. That
is FR-006 of spec 150 (wholesale replace) and the existing `PATCH`. A `MoveElement`
mutator would be the first per-member mutator on the collection, and it would need
something to name "the element to move". ADR-0112 §2 says a member has no identity. The
only candidate is its ordinal, and that is exactly what a concurrent edit changes. The
existing `If-Match` already serialises wholesale replaces correctly (spec 300's conflict
scenario). **No new domain operation, no contract field, no ADR.**

### 3. The editor half of #2348 moves to #2343, and should be an ordered list

#2343 tracks the multi-element editor. **Building bring-forward/send-back now would put a
control on a selector that does not exist.** `OverlayEditorDialog` locates "the first
`Text` element" (`textIndexOf`, `OverlayEditorDialog.tsx:86`) and `OverlayEditor.tsx`
edits one label. There is no way to say "element 2 of 3".

The recommendation recorded for #2343 is an **ordered layer list** with move-up and
move-down controls, rather than canvas-only bring-forward/send-back:
- **When elements overlap exactly, the one underneath cannot be clicked on the canvas.**
  A list is the only way to select it, so the list *is* the selector #2343 needs anyway.
- **Move up / move down are keyboard-reachable.** #2343 names WCAG 2.2 2.1.1 as the
  editor's sharpest gap. Drag-to-reorder may be added, but it must not be the only path.

"Bring forward" and "send back" are then the list's move-up and move-down, one step at a
time.

### 4. The wall pairs each element with its own text

This is what this spec builds. It has two independent halves, one per timing source
above:

- **US1:** the label hold ages the **paired** set, geometry and text together, instead of
  the text list alone.
- **US2:** resolved text names the **template** it was resolved from. The wall then
  pairs by template, not by position.

Pairing by template (US2) is deliberately chosen over pairing by revision number:
- **A revision number does not identify an element list.** `Revert` returns a Published
  revision to Draft under the *same* number (`Revision.cs:111`). It can then be edited
  and republished with a different set.
- **`PublishedAt` is not a reliable key either.** It crosses a Postgres round trip (the
  seeder reads it back) whose precision differs from the in-memory clock value carried
  on the event.
- **The template is what a resolved text depends on.** It is enough on its own, and
  needs no clock or counter.
- **A pure reorder becomes correct at once,** even when SystemVariables is stale or down:
  the templates are the same strings in a new order. Only a template that is genuinely
  new has to wait.

---

## User stories

### US1: A reorder never paints one element's text in another element's place (P1)

*As a control-room operator, I want every caption on the wall to stay with the element
it belongs to while an overlay is republished, so that a reading is never shown beside
the wrong machine, not even for a moment.*

This fixes timing source (a). Kiosk-only. Independently shippable and observable: publish
a reorder of a two-caption overlay onto a tile whose frame age is readable, and record
every painted `(text, position)` pair. None may pair one revision's text with the other
revision's position.

### US2: A republish never strands the wall on another revision's text (P2, separate PR)

*As an operator, I want a republished overlay's live readings to land in the right
captions even if the variable service is behind or briefly down, and never to stay
wrong until the next value change.*

This fixes timing source (b). It needs a clean contract cut (`ResolvedOverlayTextChangedV3`)
and touches SystemVariables, LayoutComposition, AuditObservability and the kiosk. It is
cuttable from this spec without touching US1. It is P2 because its window is
occasional and its premise is a race, which the red test must demonstrate first.

### US3: Reordering through the API reaches the wall in the new order (P1, characterisation)

*As an integrator scripting overlays, I want to reorder an overlay's elements by
sending them in a new order, and see the wall stack them that way.*

This already works. The story pins it so that the editor work in #2343 builds on a
guarded path. No production code changes.

### Out of scope, with reasons

- **Reorder controls in the console.** Moved to #2343 (decision 3). There is nothing to
  attach them to until #2343 builds element selection.
- **Overlap prevention.** Rejected (decision 1).
- **An overlap warning.** Deferred to #2343, and the open product question below.
- **A z-index field, sparse ordinals, or a `MoveElement` endpoint.** Rejected (decision 2).
  `ResolvedOverlayTextChangedV2`'s doc comment anticipated "(ordinal, text) pairs if
  #2348 makes ordinals sparse". Ordinals stay dense, and US2 moves to (template, text)
  pairs for a different reason.
- **Re-measuring `overlay_draw` on a geometry-only republish.** `renderElementsKey`
  (`LayoutGrid.tsx:507`) leaves geometry out, so a move-only republish is not measured.
  This was found while reading for US1. It is a measurement gap, not a pairing defect,
  so it is **filed separately and not fixed here** (ADR-0036: smallest change).

---

## Acceptance scenarios

### Happy path

```gherkin
Scenario: A reorder republished onto an aged tile keeps every caption with its element (US1)
  Given a wall tile whose measured frame age is 120 ms
  And it shows a published overlay whose elements are
    | ordinal | kind | text          | x    | y    |
    | 0       | Text | Line 1: 21 °C | 0.10 | 0.10 |
    | 1       | Text | Line 2: 35 °C | 0.60 | 0.10 |
  When a new revision is published with the same two elements in the opposite order
  Then at no rendered state does the node at x 0.10 show "Line 2: 35 °C"
  And at no rendered state does the node at x 0.60 show "Line 1: 21 °C"
  And once the hold has elapsed, the DOM order is [Line 2, Line 1]
```

```gherkin
Scenario: A caption that moves past a shape is never painted empty (US1)
  Given a wall tile whose measured frame age is 120 ms
  And it shows a published overlay whose elements are [Box, Text "Zone A"]
  When a revision [Text "Zone A", Box] is published
  Then no rendered state paints a caption node with empty text
```

```gherkin
Scenario: The whole set is held as one unit (US1, ADR-0129 FR-014 of spec 150)
  Given a wall tile whose measured frame age is 120 ms
  When a revision is published that moves one element without changing any text
  Then the move appears after the same hold a text change would get, not before it
  And exactly one hold is reported for the change, not one per element
```

```gherkin
Scenario: A reorder lands correctly while SystemVariables is behind (US2)
  Given a published overlay [Text "L1 {{t1}}" at P, Text "L2 {{t2}}" at Q] on a wall tile
  And the kiosk holds a resolved-text snapshot of it
  And SystemVariables has not yet indexed the next revision
  When the revision [Text "L2 {{t2}}" at Q, Text "L1 {{t1}}" at P] is published
  Then the node at Q shows t2's resolved value
  And the node at P shows t1's resolved value
  And this holds before SystemVariables indexes the new revision
```

```gherkin
Scenario: A genuinely new template is not given another template's text (US2)
  Given a published overlay whose only element is Text "Temp: {{t}}" showing "Temp: 21"
  When a revision whose only element is Text "Temperature: {{t}}" is published
  And the kiosk's resolved texts do not yet include "Temperature: {{t}}"
  Then the caption never shows "Temp: 21"
  And the kiosk requests the snapshot again, at most 3 times with backoff
  And once a snapshot with "Temperature: {{t}}" arrives, the caption shows "Temperature: 21"
```

```gherkin
Scenario: A resolved-text push names the templates it resolved (US2)
  Given a published overlay [Box, Text "A {{x}}", Text "B {{x}}"]
  When the value of x changes to 7
  Then exactly one ResolvedOverlayTextChangedV3 is emitted for the overlay
  And its texts are [("A {{x}}", "A 7"), ("B {{x}}", "B 7")]
  And no entry exists for the Box
```

```gherkin
Scenario: Elements PATCHed in a new order read back, publish and paint in that order (US3)
  Given a draft revision [Box "#D32F2F", Text "Zone A", Ellipse "#FFA000"]
  When an operator PATCHes it with [Ellipse, Text "Zone A", Box] and a matching If-Match
  Then GET returns the elements in order [Ellipse, Text, Box]
  When the revision is published
  Then OverlayRevisionPublishedV3.Elements is [Ellipse, Text, Box]
  And the wall tile's overlay nodes are in DOM order [Ellipse, Text, Box]
  And no overlay node sets a z-index
```

### Conflict

```gherkin
Scenario: Two concurrent reorders do not merge (US3, unchanged behaviour)
  Given two operators holding the same overlay at version 4
  When the first PATCHes a reorder and succeeds
  And the second PATCHes a different reorder with If-Match: 4
  Then the second receives 409 Conflict
  And the element order is the first operator's, entire
```

```gherkin
Scenario: A published revision cannot be reordered in place (US3, unchanged behaviour)
  Given a revision in the Published state
  When an operator PATCHes it with its elements in a new order
  Then the response is 409 Conflict
  And the published order is unchanged
```

### Bad request

```gherkin
Scenario: A reorder that drops an element is an edit, not a reorder (US3, unchanged)
  Given a draft revision of three elements
  When an operator PATCHes it with two of them in a new order
  Then the response is 200 and the revision holds those two
  # There is no reorder operation that could refuse this. The wholesale PATCH is the
  # contract (spec 150 FR-006), and an editor that means "reorder" must send every element.
```

```gherkin
Scenario: Overlapping geometry is accepted (decision 1)
  When an operator POSTs /overlays with a Box at (0.1, 0.1, 0.5, 0.5)
  And a Text at (0.2, 0.2, 0.3, 0.1) inside it
  Then the response is 201 Created
  And no warning or problem detail mentions overlap
```

### Auth

```gherkin
Scenario: A caller without the write scope cannot reorder (US3, unchanged)
  Given a caller authenticated without sse.overlays.write
  When they PATCH a draft's elements in a new order
  Then the response is 403 Forbidden
```

```gherkin
Scenario: A caller without the read scope cannot read resolved texts (US2, unchanged)
  Given a caller authenticated without sse.variables.read
  When they GET /system-variables/-/snapshot for an overlay
  Then the response is 403 Forbidden
```

---

## Functional requirements

- **FR-001** **Ordering is unchanged.** Paint order is the dense ordinal, which is list
  position on the API, on the contract and in the DOM (spec 150 FR-005, spec 300 FR-012).
  No z-index field, no sparse ordinals, no per-element mutator.
- **FR-002** **Overlap is legal.** No domain rule, database constraint, API validation or
  Zod schema refuses or warns on overlapping geometry. This ratifies spec 150 FR-004.
- **FR-003** *(US1)* The kiosk ages the tile's **paired render set** as one unit: kind,
  colour, geometry and displayed text of every element. At every rendered state, each
  painted node's text belongs to the same revision as its geometry. Implementation: the
  value `useLabelDelay` holds is the paired set, made stable by a key over every painted
  field (plan §"US1").
- **FR-004** *(US1)* ADR-0129's fail-open rules are unchanged:
  - a tile with no readable frame age, or one past `LABEL_DELAY_CAP_MS`, paints at once;
  - a removed overlay is not held;
  - one hold is reported per change, per tile.

  A move or reorder is now held like a text change. That is the only behaviour that
  widens.
- **FR-005** *(US2)* `ResolvedOverlayTextChangedV2` is replaced by
  **`ResolvedOverlayTextChangedV3(Guid Overlay, IReadOnlyList<ResolvedOverlayTextV3>
  Texts, long Version, EventMetadata Metadata)`**, with
  `ResolvedOverlayTextV3(string Template, string Resolved)`. **V2 is deleted in the same
  feature.** Every consumer is in this repo: the spec 150 and spec 300 precedent,
  ADR-0073's semantic shift.
  - `Texts` holds one entry per **distinct** `Text` template of the revision, in ordinal
    order of first appearance.
  - Shapes contribute nothing. Spec 300 FR-009's `""` padding rule is retired with V2.
- **FR-006** *(US2)* `GET /system-variables/-/snapshot` returns `texts: [{ template,
  resolved }]` instead of `resolvedTexts`. Both routes keep their authorisation as is,
  and so does the version. #2426's read-version-before-text order is preserved.
- **FR-007** *(US2)* The kiosk resolves each `Text` element's displayed text by looking up
  its **raw template** in the latest accepted texts (push or snapshot). There is no
  index lookup anywhere on this path.
  - **A template without `{{`** displays as itself.
  - **A placeholder template with no entry** displays its raw template, the same as a
    tile's first load today. It **never** displays another template's text. It triggers a
    snapshot refetch, at most 3 attempts with 1 s / 2 s / 4 s backoff per publish
    *(assumption: the figures are a guess; see plan)*. If no match arrives, the kiosk
    logs the resilience event `resolved-text-template-miss` once per overlay per
    publish, then stops.
- **FR-008** *(US2)* The kiosk's version guard (`useOverlayHubHandlers.ts`), its fab
  filter (ADR-0145) and its static-label disagreement report (spec 141) are unchanged in
  behaviour. Their tests change only in message construction.
- **FR-009** *(US3)* The API reorder path is pinned end to end by characterisation tests:
  1. `PATCH` a permuted list;
  2. `GET` returns it in that order;
  3. `OverlayRevisionPublishedV3` carries it in that order;
  4. the wall's DOM order matches;
  5. no overlay node sets `z-index`.
- **FR-010** **No console UI changes.** `OverlayEditor.tsx`, `OverlayEditorDialog.tsx` and
  their guard files are absent from the diff.
- **FR-011** The PR quotes these figures, each run twice:
  - an `overlay_draw` p50/p95 on the nine-tile fixture (US1 changes the render path's
    inputs);
  - *(US2 PR)* `NFR_VariableResolutionLatencyTests`' median.

---

## Locked tech choices

| Concern | Choice | Authority |
|---|---|---|
| Paint order | Dense ordinal = list position = DOM order; no `z-index` | spec 150 FR-005, spec 300 FR-012 |
| Overlap | Legal; ordered, not prevented | spec 150 FR-004, ratified here (decision 1) |
| Reorder | Client permutes, wholesale `PATCH` under `If-Match` | spec 150 FR-006, ADR-0112 §2, ADR-0113 |
| Label aging | One hold per tile over the paired render set | ADR-0129, spec 150 FR-014 |
| Resolved-text contract | Clean `ResolvedOverlayTextChangedV3` of `(Template, Resolved)` pairs; V2 deleted | ADR-0073, ADR-0040, spec 150 FR-009 precedent |
| Errors / guards | `Result<T, Error>`; `Ensure.That` | ADR-0047, ADR-0105 |
| Handlers | Destructure first; `HandlerDeconstructionTests` | CLAUDE.md house rule |
| Frontend state | RTK Query snapshot cache, `upsertQueryData` on push, unchanged tags | ADR-0075 |
| Tests | xUnit + Shouldly; integration on the Aspire fixture; Vitest; Playwright | ADR-0052, ADR-0103 |

---

## Latency budget impact (constitution §IV)

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No |
| Presentation buffer | ≤ 200 ms | **US1, scope only.** The same per-tile hold, capped by the same `LABEL_DELAY_CAP_MS`, now covers geometry as well as text. A value change waits exactly as long as it does today; a pure move now waits as long as a text change. Nothing is held longer. |
| Event → overlay state | ≤ 200 ms | **US2, payload only.** Same fan-out and one event per overlay; each entry gains its template string (at most 8 × 256 characters). The kiosk does a lookup in a map of at most 8 entries per tile instead of an index. Quote `NFR_VariableResolutionLatencyTests`' median, run twice. |
| Overlay composite + render | ≤ 50 ms | **US1, inputs only.** Same node count (ADR-0164's 72 ceiling), same style functions. The stable-key computation now hashes geometry too. Quote `overlay_draw` p50/p95, run twice, beside spec 225's 21-run mean p50 of 56.63 ms. The leg is already over budget, so "should not move" has to be shown. |
| Headroom | ≤ 150 ms | Remainder |

A **republish** is not the event that §IV's SLO times (that is a value change reaching a
caption). US2's refetch-on-miss can add up to 7 s to a republish whose new template
SystemVariables has not yet indexed. During that time the wall shows the raw template,
never a wrong value.

§VII's dashboard obligation for composite-and-render is still outstanding (#1940). This
spec inherits that position from specs 146, 150 and 300 and does not discharge it.

---

## Independent end-to-end test procedure

Run against the run-mode Aspire stack, without reading any test code.

1. Boot the stack. Mint a token with `sse.overlays.write` and `sse.overlays.read` from
   Aspire's **proxied** Keycloak endpoint. Define the variables `t1` = `21` and `t2` =
   `35` in the wall's fab.
2. `POST /overlays` with `[Text "L1 {{t1}}" at (0.05, 0.1, 0.4, 0.1), Text "L2 {{t2}}" at
   (0.55, 0.1, 0.4, 0.1)]`. Publish it. Bind it to a tile of a published layout and open
   the wall. Expect "L1 21" on the left and "L2 35" on the right.
3. **Install a pairing recorder.** In the kiosk's devtools console, add a
   `MutationObserver` on the tile that logs every `(textContent, style.left)` pair of each
   `[data-testid=camera-viewer-overlay-label]`, on every mutation and on every
   `requestAnimationFrame`.
4. **(US1)** `POST /overlays/{id}/revisions` to branch, then `PATCH` the elements in
   reverse order **with each element keeping its own geometry**, then publish. Expect the
   wall to look the same, because each caption kept its place. **Expect the recorder to
   show no pair "L1 …" at `55%` and no pair "L2 …" at `5%`, at any frame.** Before this
   fix, those pairs appear for about the tile's frame age.
5. **(US2)** In the Aspire dashboard, **stop the `system-variables` resource.** Branch,
   reverse the order again, and publish. Expect correct pairing, as in step 4, **while
   SystemVariables is down**. Before this fix, the stale snapshot's positional texts land
   on the swapped captions and stay there.
6. **(US2)** With SystemVariables still stopped, branch and change "L1 {{t1}}" to "Line 1
   {{t1}}", then publish. Expect the caption to show **the raw template** "Line 1 {{t1}}"
   (never "L1 21"). Expect the kiosk console to log `resolved-text-template-miss` once
   after the retries are exhausted. Start `system-variables` again and change `t1` to
   `22`. Expect "Line 1 22".
7. **(US3)** `POST` an overlay `[Box, Text "Zone A", Ellipse]`, all overlapping, and
   publish it. Expect **201** with no overlap warning. Expect the wall's overlay nodes in
   that DOM order, and `getComputedStyle(node).zIndex === 'auto'` for each.
8. Read `overlay_draw` p50/p95 on the nine-tile fixture. Record them beside spec 225's
   baseline. Repeat once and record both runs.

---

## Dependency on spec 300

Everything here is written against spec 300's model: `OverlayElement`, `ElementKind`,
`OverlayRevisionPublishedV3`, and the `""`-padded `ResolvedOverlayTextChangedV2` that
FR-005 retires. **Spec 300 had not merged when this was written.**

- Do not start Phase 4 until #2349's PR-A is on `develop`.
- Cut this branch from `develop` after that. Cutting it from the #2349 branch makes an
  unannounced stacked PR (CLAUDE.md §"Stacked PRs").
- Before starting, re-check that `301` is still free. Two unmerged branches can claim
  the same number.

---

## Open question (for the human gate)

**Should the multi-element editor (#2343) warn when two `Text` elements overlap?**

This is a product judgement, not an architectural one, and **it does not block this
spec**. It is recorded here because #2348 asked it and this spec moves it.

- **Recommendation:** a **non-blocking** warning, **only for Text-over-Text
  intersection**. Never warn when a Box or Ellipse is involved, because a shape around or
  under text is the intended use (decision 1).
- **Alternatives:**
  - no warning at all (order is enough);
  - warn on every intersection. Not recommended: it fires on every box-and-caption pair,
    which would teach operators to ignore it.

Everything else in this spec (ordered not prevented, no domain reorder operation, the
editor half moving to #2343, template-keyed pairing) is resolved from the constitution,
ADRs and code above. It is open to review, but nothing in it waits on a product answer.

---

## Follow-on work (file before PR-A merges)

1. **Comment on #2343:**
   - the ordered-layer-list recommendation (decision 3);
   - the overlap-warning question above;
   - "a reorder is a client-side permutation and a wholesale `PATCH`; no endpoint is
     needed".
2. **`renderElementsKey` omits geometry**, so a move-only republish is not re-measured
   (`LayoutGrid.tsx:507`). A measurement gap, separate from pairing.
3. **Re-scope note on #2348.** The editor half moved to #2343. This spec delivers the
   renderer half.
