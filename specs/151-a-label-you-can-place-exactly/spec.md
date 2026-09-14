# Spec 151 — A label you can place exactly

**Issue:** #2346 — "There is no way to place an overlay label precisely: no
numeric entry, no snapping, no guides."
**Branch:** `feat/2346-a-label-you-can-place-exactly`
**Lane:** autonomous (ADR-0144). **Engineer:** `frontend-engineer`.
**Reviewer (phase 6):** `frontend-reviewer`.
**Phase 4a colour:** **RED** — every requirement below is new behaviour.

**ADRs referenced:** ADR-0037 (the phased workflow), ADR-0144 (the lane and the
two colours of phase 4a), ADR-0139 (a new-behaviour test is observed red first),
ADR-0036 (smallest change; no speculative generality), ADR-0074 (two frontends
with `apps/shared` between them), ADR-0077/ADR-0078 (Radix + Tailwind —
**deliberately not applied here**, see §Out of scope), ADR-0146 §Discipline (one
discipline, two surfaces), ADR-0148 (token architecture — **token conversion is
out of scope**, #2342 gated on #2332), ADR-0109 (file-disjoint parallelism),
ADR-0123 (the composite-and-render leg is already over budget), ADR-0128 /
ADR-0129 (what the editor is *not* on).

**No new ADR is needed.** This spec makes one product decision (the unit an
operator sees) and two scope decisions, all of which sit inside decisions
already made by spec 149 / #2344. If the follow-up issue in §Out of scope is
picked up, snapping-to-thirds is likewise a product choice, not an architectural
one.

---

## Scope, and the split

The issue asks for four things. **This spec ships two of them and proposes the
other two as a separate issue.** That split is not a trim for convenience — it
is the issue's own recommendation, quoted verbatim:

> Points 1 and 4 are worth doing even if 2 and 3 are deferred: they turn an
> unreadable operation into a legible one for very little work.

(The issue numbers them 1 = numeric inputs, 2 = snapping, 3 = guides,
4 = readout.)

**In this spec — the legible pair.** Numeric entry and the live drag readout are
*the same four fields*: a controlled input shows the value and accepts a new
one. There is no separate readout widget to build. One vertical slice, one
story, observable end to end.

**Deferred to a new issue — the assisted pair.** Snapping and alignment guides
are also one thing, and a different one. Both need the same machinery that
neither of the above needs:

- interception of the *continuous* drag position (not just its readout),
- a candidate-target set (canvas edges, thirds, centre — and, once #2345 lands,
  every other label's edges and centres),
- a proximity test with a threshold in **pixels**, not normalized units, because
  "close enough to snap" is a property of the operator's hand and the screen,
  not of the coordinate space,
- a **window-level** modifier listener, because the key that suppresses snapping
  is pressed *during* a mouse drag, when focus is on neither the label nor any
  field.

Snapping without guides is invisible magic; guides without snapping are decoration.
They ship together or not at all. §Out of scope states the issue to file and how
this spec's design leaves room for it.

---

## User story

### US1 (P1) — Read back and type the four values

**As** an operator authoring an overlay label,
**I want** the label's position and size shown as numbers I can read and edit,
**so that** I can place a label on an exact value instead of wherever my hand
landed, and two labels meant to line up across two cameras actually do.

This is the whole slice. There is no US2.

**Why it is independently shippable:** the editor gains a panel; nothing else in
the system changes. No contract, no endpoint, no migration, no message. An
operator can open the dialog, read four numbers that were invisible yesterday,
type one, watch the label move, and save — all in one PR, observable by a person
in a browser.

---

## The unit an operator sees: **percent, at most two decimal places**

The stored truth is normalized (`0.2487`). The three candidates were normalized,
percent and pixels. **Percent wins**, and the reasons are in priority order:

1. **It is already this editor's spoken vocabulary.** Spec 149 shipped
   `formatPercent` and every keyboard announcement speaks it — "Left 30%", "Width
   0.5%, at the minimum". Showing normalized in the fields would give the sighted
   operator and the screen-reader operator *two different units for the same
   value in the same dialog*. One unit, one conversion, one mental model.

2. **Two decimal places of percent is exactly the grid, losslessly.** Spec 149
   quantizes to `1e-4` normalized. `1e-4` normalized **is** `0.01%`. So 2 dp
   percent and the programme's quantum are a bijection: every grid value is
   typeable and every typeable value is on the grid. Nothing is unreachable and
   nothing is silently rounded past the grid the programme already chose.

3. **The grid reads cleanly in it.** Fine step `0.005` → **0.5%**; coarse step
   `0.05` → **5%**. Compare "0.005" and "0.05", which differ by one character and
   are the exact pair an operator must not confuse.

4. **Thirds are legible only in percent.** The deferred snapping issue's targets
   are the edges, the centre and the thirds — `33.33%` / `66.67%`. In normalized
   they are `0.3333` / `0.6667`; in pixels they are canvas-dependent. Choosing
   percent now means the follow-up issue's readout needs no unit change.

**Pixels are rejected**, despite being the only unit an operator can measure on
screen: they are a function of `canvasWidthPx`/`canvasHeightPx`, which are
**props** with defaults, so the same stored label would read `200` on one host
and something else on another. They are also anisotropic against the grid —
1 px is `0.125%` across an 800 px canvas and `0.222%` down a 450 px one — so a
pixel field could not express the grid at all on the vertical axis.

**Normalized is rejected** for the same reason the announcements did not use it:
`0.2487` is a number an operator reads but does not reason with, and the two
step sizes are one character apart.

**The cost, stated.** Display and storage now differ by a factor of 100, so there
is a conversion at a boundary. FR-012 confines it to one module with two
functions, and the float trap in it is pinned by a test (see §Precision, below).

---

## Precision: where the float goes wrong, and where it is caught

The naive round trip is wrong, and the test suite must prove the implementation
is not naive.

```
0.2487 * 100            === 24.869999999999997      (display side)
Number((0.2487*100).toFixed(2))  === 24.87          ← correct display
24.87 / 100             === 0.24870000000000003     ← WRONG, off-grid, 17 dp
Math.round(24.87 * 100) / 10000  === 0.2487         ← correct parse
```

So the parse must quantize **on the normalized side** using the same
`QUANTUM = 10_000` spec 149 already defines, not divide by 100. A `0.24870000000000003`
reaching `onChange` is precisely the defect spec 149's phase-6 review found in
`quantizeBoundFloor` — a value latched at 17 decimals — arriving through a new
door.

---

## Functional requirements

### The panel

- **FR-001** — Four numeric fields are rendered below the canvas, labelled
  **Left**, **Top**, **Width**, **Height**, showing the label's current geometry.
  The names match spec 149's `AXIS_ANNOUNCE_LABEL` exactly, so a field and its
  spoken announcement name the same thing.
- **FR-002** — Values are shown in percent at grid resolution: the normalized
  value rounded to two decimal places of percent, with trailing zeros trimmed
  (`0.25` → `25`, `0.2487` → `24.87`, `0.005` → `0.5`). The `%` sign is part of
  the field's label and suffix, not of the editable text.
- **FR-003** — The field is `type="text"` with `inputMode="decimal"`, **not**
  `type="number"`. See §The input type, below; this is a correctness requirement,
  not a style one.

### Committing a value

- **FR-004** — While the operator is typing, the field holds an uncommitted draft
  string. A draft commits on **blur** and on **Enter**. **Escape** discards the
  draft and restores the field to the current value without emitting anything.
- **FR-005** — A committed value is parsed, quantized to the grid
  (`Math.round(percent * 100) / 10_000`, per §Precision), and emitted through the
  **same `onChange` payload builder the drag and keyboard paths already use** —
  not a second emission path.
- **FR-006** — Committing changes exactly one of the four normalized values.
  `text` and `fontSizePx` and the other three geometry values travel forward
  untouched, as a drag already does.

### Refusing a value

- **FR-007** — A draft that does not parse as a finite decimal number is refused
  with an inline message on that field (`role="alert"`): *"Enter a number."*
- **FR-008** — A **position** (Left / Top) outside `[0%, 100%]` is refused:
  *"Left must be between 0% and 100%."* The bound is `NormalizedPosition.cs`'s
  `InRange(0m, 1m)` and the Zod schema's `min(0).max(1)`, expressed in percent.
- **FR-009** — A **size** (Width / Height) outside `(0%, 100%]` is refused:
  *"Width must be greater than 0% and at most 100%."* The bound is
  `NormalizedSize.cs`'s `value is > 0m and <= 1m` and the Zod schema's
  `gt(0).max(1)`, expressed in percent. **A typed `0` is refused, not floored** —
  see §The zero-size question.
- **FR-010** — A refused draft **stays in the field** so the operator can correct
  it rather than retype it, and the label does not move. Nothing is emitted. The
  last committed value remains the truth.
- **FR-011** — A refused field clears its message as soon as a subsequent commit
  succeeds, or on Escape.

### Accepting a value that leaves the canvas

- **FR-012** — A committed value that places the label partly outside the canvas
  (`x + width > 1` or `y + height > 1`) is **accepted**. A non-blocking advisory
  (`role="status"`, not `alert`) is shown while that condition holds:
  *"This label extends past the right edge and will be clipped on the wall."*
  (…*bottom edge*… on the vertical axis; both, if both.) See §The off-edge
  question.

### The live readout

- **FR-013** — While a drag or a resize is **in progress**, the four fields show
  the in-flight geometry and update continuously.
- **FR-014** — `onChange` does **not** fire during a drag or resize. It fires at
  `onDragStop` / `onResizeStop` only, exactly as it does today. The live readout
  is local component state and is discarded when the gesture ends.
- **FR-015** — Starting a drag while a field holds an uncommitted draft commits
  the draft first — mouse-down on the label blurs the field, and FR-004's blur
  commit is what runs. No requirement makes the two states coexist.

### Conversion and accessibility

- **FR-016** — The percent ⇄ normalized conversion exists in **one module with
  two functions**, and spec 149's `formatPercent` moves into it. Two copies of
  the `× 100` is the defect this requirement exists to prevent.
- **FR-017** — Each field has a visible `<label>` bound by `htmlFor`, and
  `aria-describedby` pointing at its error message when one is present.
- **FR-018** — The panel is keyboard-reachable in DOM order and needs no key
  handling of its own. Arrow keys inside a field are the browser's; arrow keys on
  the **label** remain spec 149's nudge, unchanged.

---

## The input type: why not `type="number"`

`OverlayEditor` is rendered inside `OverlayEditorDialog`'s `<form onSubmit>` with
a real `type="submit"` button. A `type="number"` input participates in **native
constraint validation**, and any of `badInput`, `rangeUnderflow`, `rangeOverflow`
or `stepMismatch` **blocks form submission** before `onSubmit` ever runs — the
operator gets a browser tooltip on a field, not this editor's message, and the
save button appears to do nothing.

- `min`/`max` would fire `rangeUnderflow`/`rangeOverflow` — and FR-010 wants the
  bad value *kept in the field*, which is exactly the state that blocks submit.
- `step` would fire `stepMismatch` on every off-step value, which is most of
  them.
- Even with none of those set, `badInput` is reachable: typing `-` or `1e` into a
  number input leaves it in `badInput`, and its `.value` reads `''`, so the
  component cannot even show the operator what they typed.

jsdom's constraint validation is partial, so **no unit test would catch this**;
it would arrive as a bug report about a dead Save button. `type="text"` with
`inputMode="decimal"` has no native constraint surface at all, gives mobile and
touch keyboards the numeric pad, and makes FR-004's draft-string model the
natural one rather than a workaround.

The lost affordance is the spinner and its arrow-key stepping. That is acceptable
because **the stepping affordance already exists and is better**: spec 149 put
arrow-nudge on the label itself, on the same 0.5% / 5% grid, with announcements.
The field is for typing a number; the label is for stepping one.

---

## The zero-size question (#2361), and how this spec resolves it

**#2361** records that `clamp01` permits a zero-sized label while both
`NormalizedSize.cs` (`> 0m and <= 1m`) and `overlays.schema.ts` (`gt(0).max(1)`)
refuse one, and predicts that *"#2346 will hit this from the other side"* —
typed entry, with no pixel minimum and no key-repeat floor in the way.

**This spec does not fix #2361, and typed entry never reaches it.** FR-009
refuses a typed `0` with a message. The prediction was that numeric entry would
arrive at `clamp01` unguarded; it does not arrive at all.

**Why a message beats a floor here, and why this is the smaller change.** #2361
proposes splitting `clamp01` into a position clamp and a size clamp. That fix is
correct, and it is still the right fix for the **drag** path — but for a *form
field* it would be the wrong behaviour even if it were free: a clamp silently
substitutes a value the operator did not type. A field has a validation surface
that a drag and a keypress do not, and FR-007–FR-010 must exist regardless, for
`"abc"`, for `-5`, for `150`. The size bound is **one more comparison inside a
validator this spec writes anyway** — not a third copy of a floor, and not a new
constant.

**Why not fix #2361 as well.** Fixing it changes the drag path's output, and the
drag path's output is pinned by a **characterisation** assertion:

`apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx:156`
```
expect(next.normalizedHeight).toBe(0);
```

Editing that assertion is the exact signal CLAUDE.md §House rules says to treat
as a block rather than an adjustment. Doing it inside a PR whose subject is
something else buries the one piece of evidence a later reader has. #2361 is a
tracked defect with its own analysis, its own proposed fix and its own
characterisation artefact to change; it deserves its own red test and its own PR.

**What this spec owes #2361:** a comment recording that the typed-entry exposure
it predicted is closed by validation rather than by a clamp, so its remaining
scope is the **drag path only**, and that line 156 is the assertion to move when
it is fixed. (T007.)

---

## The off-edge question, and how this spec resolves it

`clamp01` bounds each of the four values independently, so it permits `x = 1`
with `width = 0.25` — a label three-quarters off the canvas — while dragging is
bounded by `<Rnd bounds="parent">` and cannot reach it. #2361 records this as
possibly intentional and leaves it to this spec.

**Decision: typed entry permits the off-canvas rectangle, and says so (FR-012).**

1. **The domain explicitly permits it.** `NormalizedPosition.cs`: *"a position
   carries no relationship to the `NormalizedSize` it travels with: a label may
   describe a rectangle running off the right edge, and the kiosk composite clips
   it. That is true today and is not made an invariant here."* The editor must
   not be stricter than the domain in a way that makes a **valid stored overlay
   un-editable**: an overlay authored through the API at `x = 0.9, width = 0.25`
   would, under a clamping field, silently move the moment an operator touched
   any of the four numbers.

2. **It is the same answer spec 149 already gave.** Spec 149's phase-6 finding 2
   is *clamp the motion, not the value*, for exactly this reason — an already
   off-region label may move toward the region but is never snapped onto the
   bound. Refusing typed off-edge values would be a third, contradictory answer
   to a question this programme has answered once.

3. **The three paths are then coherent, each as strict as its input is coarse:**

   | path | reachable region | why |
   |---|---|---|
   | drag | cannot leave — `bounds="parent"` | a hand is imprecise; the guard is free |
   | keyboard | may not move *further* out; may move in | a keypress is a step, not a destination |
   | typed | permitted, advised | a number typed on purpose is the one input where "I meant it" is credible |

4. **Silence is the part that would be wrong.** Hence FR-012's advisory: accepted,
   visible, non-blocking, and phrased as what the wall will do — not as an error,
   because neither the schema nor the domain calls it one.

---

## Acceptance scenarios (Gherkin)

### Happy path

```gherkin
Scenario: The four values are visible without touching anything
  Given an overlay label at x 0.25, y 0.05, width 0.5, height 0.1
  When the operator opens the overlay editor
  Then a field labelled "Left" reads "25"
  And a field labelled "Top" reads "5"
  And a field labelled "Width" reads "50"
  And a field labelled "Height" reads "10"

Scenario: Typing an exact value moves the label
  Given the editor is open with the label's Left at "25"
  When the operator replaces Left with "33.33" and presses Enter
  Then onChange is called exactly once
  And the emitted normalizedX is exactly 0.3333
  And normalizedY, normalizedWidth, normalizedHeight, text and fontSizePx are unchanged
  And the field reads "33.33"

Scenario: An off-grid stored value displays at grid resolution
  Given a label whose normalizedX is 0.2487142
  When the editor renders
  Then the Left field reads "24.87"

Scenario: Blur commits as Enter does
  Given the operator has typed "40" into Top without pressing Enter
  When the field loses focus
  Then onChange is called once with normalizedY 0.4
```

### Live readout

```gherkin
Scenario: The fields track a drag in progress
  Given the editor is open with the label's Left at "25"
  When the operator drags the label to pixel x 400 on the 800px canvas
  And has not yet released the mouse
  Then the Left field reads "50"
  And onChange has not been called

Scenario: Releasing the drag emits once, as it does today
  Given the operator is mid-drag with the label at pixel x 400
  When the operator releases the mouse
  Then onChange is called exactly once with normalizedX 0.5
  And the Left field reads "50"
```

### Conflict — a value that leaves the canvas

```gherkin
Scenario: An off-edge rectangle is accepted and announced, not refused
  Given a label whose Width is "25"
  When the operator commits "90" into Left
  Then onChange is called with normalizedX 0.9
  And a status message says the label extends past the right edge and will be clipped
  And no alert is shown

Scenario: The advisory clears when the label comes back inside
  Given the label is at Left "90" with Width "25" and the advisory is shown
  When the operator commits "50" into Left
  Then no status message about clipping is shown
```

### Bad request

```gherkin
Scenario: A non-numeric draft is refused and kept
  Given the editor is open with Width at "50"
  When the operator commits "abc" into Width
  Then an alert on the Width field says "Enter a number."
  And the field still reads "abc"
  And onChange has not been called

Scenario: A zero size is refused, not floored  (#2361, from the guarded side)
  When the operator commits "0" into Height
  Then an alert says "Height must be greater than 0% and at most 100%."
  And onChange has not been called
  And the label's height has not changed

Scenario: A position above the canvas is refused
  When the operator commits "150" into Top
  Then an alert says "Top must be between 0% and 100%."
  And onChange has not been called

Scenario: A negative position is refused
  When the operator commits "-5" into Left
  Then an alert says "Left must be between 0% and 100%."
  And onChange has not been called

Scenario: Escape abandons a bad draft
  Given the operator has typed "-5" into Left and it was refused
  When the operator presses Escape in the Left field
  Then the field reads the last committed value
  And no alert is shown
  And onChange has not been called

Scenario: A successful commit clears a standing error
  Given the Width field shows "Enter a number."
  When the operator commits "60" into Width
  Then no alert is shown on the Width field
  And onChange is called with normalizedWidth 0.6
```

### Auth / scope

```gherkin
Scenario: The panel carries no authorization of its own
  Given an operator who can open the overlay editor at all
  Then the four geometry fields are shown and editable
  And no request is made by the panel
```

**Why that is the whole auth story, stated rather than omitted:** this panel
issues no request. Reaching the editor at all already required
`sse.overlays.write` (or `sse.management`, which grandfathers it) at
`POST /overlay-designer/overlays`; the save button's authorization is unchanged
and untouched. A reviewer should confirm the *absence* of a new trust boundary
rather than wonder whether one was forgotten.

---

## Independent end-to-end test procedure

Run by a person, against the real Aspire stack. Not a substitute for the
automated tests; the things below are the ones jsdom cannot show — real layout,
a real mouse drag, real form submission, and the value surviving to Postgres as
a `decimal`.

1. **Boot.** `dotnet run --project src/AppHost` (one stack per machine). Wait for
   `management-web` and `overlay-designer` to report healthy.
2. **Sign in** to `management-web` as an operator. Navigate to **Overlays** →
   **New overlay**.
3. **Read back.** Confirm four labelled fields below the canvas — Left, Top,
   Width, Height — each showing a percentage, and that those percentages match
   where the default label actually sits on the canvas. *(Before this spec, the
   four values appear nowhere in the UI; this step is the issue's headline
   complaint, resolved.)*
4. **Type.** Put `33.33` in Left, press **Tab**. The label jumps left-to-right to
   a third across. The field reads `33.33`. Nothing else moves.
5. **Drag.** Drag the label with the mouse from left to right. **While the button
   is still down**, the Left field counts up continuously. Release: it settles,
   and the label does not jump.
6. **Exactness.** Put `24.87` in Left, press **Enter**. Field reads `24.87`
   (not `24.869999…`, not `24.9`).
7. **Refuse a zero.** Put `0` in Width, press Enter. An inline message appears,
   the label does not change size, and `0` is still in the field. Press
   **Escape** — the field returns to its previous value and the message clears.
8. **Refuse a range.** Put `150` in Top, Enter. Inline message; label unmoved.
9. **Off-edge.** Set Width to `25` and Left to `90`. Both commit. A **status**
   advisory (not an alert) says the label will be clipped on the wall, and the
   preview shows it running off the right edge.
10. **Save with an off-edge label.** Name the overlay `E2E-151-<timestamp>` and
    press **Save as draft**. It saves — the advisory is not a validation error.
    *(If the Save button appears to do nothing, §The input type's failure mode
    has occurred and the field is a `type="number"` after all.)*
11. **Round trip to the server.** Mint an operator token from Aspire's
    **proxied** gateway endpoint (not the container's mapped port) and
    `GET /overlay-designer/overlays`. The saved overlay's label carries
    `normalizedX: 0.9` and `normalizedWidth: 0.25` — **exactly**, not
    `0.9000000000000001`. This is the step that proves percent entry survives
    the `double` → `decimal` boundary.
12. **Keyboard still works.** Focus the label itself (Tab to it), press
    **ArrowRight** ten times. The Left field counts up in `0.5` increments and
    the spoken announcement says the same percentage the field shows. *(One unit,
    two surfaces — the whole reason for the percent decision.)*

**Record in the verification note** whether step 11 was actually performed
against a real server, or only the UI steps 3–10. A note that claims the round
trip without step 11 claims a discharge nobody earned.

---

## Latency budget impact: **N/A**, and why saying so is not a dodge

The event → overlay path is `camera → SFU → kiosk decode → presentation buffer →
event → overlay state → composite + render` (constitution §IV). **This change is
on none of it.** `OverlayEditor` runs in `management-web`, an authoring tool; it
is never mounted on the wall, never rendered per tile, and never reached by an
event.

**The one place a careless change here would reach the budget** is
`apps/shared/src/ui/composites/overlayLabelStyle.ts`, which **is** on the
composite-and-render leg. That leg is **already over budget** — ADR-0123 records
p50 54.2 ms against a 50 ms allowance — so it may not absorb anything, however
small. `overlayLabelStyle.ts` is therefore **read, not modified, and gains no new
caller** (plan.md §Boundary rules, checked at phase 6).

§VII's dashboard rule (ADR-0117) binds implemented legs; this change implements
no leg and is not subject to it.

**On the tile count:** ADR-0146 §Context and several issues describe the wall as
"up to 250 tiles". `GridDimensions.MaxTiles` is **4**. The correction is tracked
as **#2363** and is not this spec's to make — but no reasoning here depends on
either figure, because the editor renders exactly one label.

---

## Out of scope, and the issue to file

### Deferred to a new issue: snapping and alignment guides

**File as:** *"An overlay label snaps to the canvas's edges, thirds and centre,
and says what it snapped to"* — `enhancement`, blocked by nothing, related to
#2346 (this spec), #2345 (multi-label), #2344 (the grid).

It covers the issue's points 2 and 3 together:

- snap to the `0.005` grid, and to the canvas's edges / thirds / centre;
- a modifier held during the drag suppresses snapping;
- alignment guides drawn on the canvas while dragging, for whichever targets are
  in range.

**How this spec leaves room for it without rework.** Three things, deliberately:

1. **The unit is already right.** Thirds are `33.33%` / `66.67%`; the readout
   speaks percent; nothing about the panel changes when snapping lands.
2. **The continuous-drag hook already exists.** FR-013 puts `onDrag` / `onResize`
   handlers on the `<Rnd>` and holds the in-flight geometry in one piece of
   state. Snapping transforms that same value before it is displayed and before
   `onDragStop` emits it; guides render from it. The follow-up adds a transform
   and a rendering layer to a pipeline this spec builds, rather than building the
   pipeline.
3. **Label-to-label guides (#2345) need only a second source of targets.** The
   design that matters is that the target set is *a list*, not "the canvas". The
   follow-up should take canvas-derived targets from a function and, once #2345
   lands, concatenate label-derived ones. This spec does not build that function
   — building it now would be speculative generality (ADR-0036) — but it does not
   foreclose it, because nothing here decides where targets come from.

**Snapping thresholds are a pixel concern, not a normalized one**, and that is
the single design note worth carrying forward: "within 6 px of a target" is what
an operator's hand experiences; "within 0.0075 normalized" is the same thing on
one canvas size and the wrong thing on another.

### Also out of scope

- **Design tokens / Tailwind conversion** — #2342, gated on #2332. The new
  fields are inline-styled like every other control in this file (the text input
  and the font-size slider both are). `Input.tsx` and `FormField.tsx` are
  Tailwind-classed primitives; pulling them into an inline-styled file would
  create a hybrid for #2342 to untangle. **This is the same call spec 149 made**
  for its focus ring.
- **Fixing #2361's `clamp01`** — see §The zero-size question. Its remaining scope
  is the drag path.
- **Editing an existing overlay.** There is no edit dialog; `OverlayEditorDialog`
  is create-only. Step 11 of the test procedure reads back through the API
  precisely because the UI offers no re-open.
- **`CameraViewer.tsx` and the wall label** — untouched. A geometry field per
  tile on a wall is a defect, not a feature.
- **Anything under `src/**`.** No contract, endpoint, message, entity or
  migration changes. The two `Normalized*` value objects are **read for their
  bounds** and not modified.

---

## What was verified rather than assumed

1. **The spec number.** `ls specs/` ends at 149; `git log --all --name-only --
   specs/` shows **150** exists on the unmerged `feat/2345-…` branch. This is
   **151**.
2. **`feat/2345` carries no code** — `git diff --name-only develop...feat/2345-…`
   is three files, all under `specs/150-…`. **Nothing is stacked**; this branch is
   cut from `develop` and no rebase conflict is pending from that quarter.
3. **The characterisation assertion that pins the zero size** is real, at
   `OverlayEditorCharacterisation.test.tsx:155-156`, and reads
   `expect(next.normalizedWidth).toBe(1); expect(next.normalizedHeight).toBe(0);`.
   That is what makes fixing #2361 a characterisation edit.
4. **Only one guard file makes a structural DOM query** —
   `OverlayLabelParity.test.tsx:56` reaches the label via
   `getByTestId('overlay-editor-preview').parentElement`. Nothing queries by
   sibling order or child index, so adding a panel below the canvas disturbs no
   guard. (Checked with a grep for `querySelector`, `parentElement`, `container.`,
   `getAllBy`, `childNodes`, `nextSibling` across all five.)
5. **`apps/shared` has no React Hook Form and no `@testing-library/user-event`.**
   Its dependency list is React, SignalR, RTK, seven Radix packages, `clsx`,
   `react-rnd`, `tailwind-merge`, `zod`. So validation is hand-written (FR-007–
   FR-011) and the tests use `fireEvent`, as spec 149's do. **No new package.**
6. **There is no `max-lines` ESLint rule** in `apps/shared/eslint.config.js` or
   `eslint.config.mjs`, and ADR-0084's 300-LOC limit is a **SonarAnalyzer** rule,
   which runs on C#. So `OverlayEditor.tsx` at 517 lines is not failing anything —
   the extraction in plan.md is a readability argument, made on its merits, not a
   compliance one. Saying so prevents a reviewer being told a rule exists that
   does not.
7. **Internal composites need no `package.json` export entry.**
   `BackdropControls.tsx`, `FrameGrabber.tsx` and `PlaceholderPreviewPanel.tsx`
   are all imported relatively by `OverlayEditor.tsx` and none appears in the
   export map. The new component follows them.
8. **`OverlayEditorDialog` surfaces only `errors.label.text`.** A geometry value
   the Zod schema refuses today produces a form that simply does not submit, with
   no message anywhere. FR-007–FR-011 close that hole for the four geometry
   fields as a side effect of doing their own job; the `name` and `text` fields
   already had messages. Worth a reviewer's eye, not a separate requirement.

---

## Risks

| Risk | Handling |
|---|---|
| The percent parse divides by 100 and latches a 17-decimal value | §Precision names the exact wrong expression; T001 pins `24.87` → `0.2487` with `toBe`, not `toBeCloseTo`. `toBeCloseTo` would pass on the defect and must not be used for this assertion |
| `type="number"` is chosen anyway and kills the Save button | §The input type; the failure is invisible to jsdom, so test-procedure step 10 is the only place it can be caught |
| The live readout is wired through `onChange` and the dialog's RHF form re-validates at mouse-move rate | FR-014 is the requirement; T001 asserts `onChange` is **not** called during `onDrag` |
| A field re-renders from `value` mid-typing and eats the operator's keystrokes | FR-004's draft-string model is the fix; the draft, not `value`, is the input's `value` while focused |
| The panel's new DOM breaks a guard | §What was verified item 4 — only `OverlayLabelParity` queries structurally, and via a `data-testid`'s parent inside the canvas |
| `formatPercent` moving out of `OverlayEditor.tsx` changes an announcement string | It is a pure move. `OverlayEditorKeyboard.test.tsx` pins the strings verbatim (e.g. `'Width 0.5%, at the minimum'`) and must pass **unmodified** — that file is the characterisation for this one behaviour-preserving move inside an otherwise red spec |
| #2345 lands first and conflicts | It carries no code today. If it merges before this does, rebase and re-run all five guards |
