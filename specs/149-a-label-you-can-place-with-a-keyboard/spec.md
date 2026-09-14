# Spec 149 — A label you can place with a keyboard

**Issue:** #2344 — *An overlay label can only be positioned by dragging, so
there is no keyboard path at all.*
**Branch:** `fix/2344-a-label-you-can-place-with-a-keyboard` (cut from
`origin/develop`, **not** stacked on #2360).
**Lane:** autonomous (ADR-0144). **Engineer:** `frontend-engineer`.
**Programme:** fourth delivery of the overlay-editor programme (#2339–#2353),
after #2339 (spec 146), #2340 (spec 147) and #2341 (spec 148, PR #2360 open).

**ADRs referenced:** ADR-0037 (the phased workflow), ADR-0144 (the lane and the
two colours of phase 4a), ADR-0036 (smallest change, no speculative
generality), ADR-0074 (two frontends with `apps/shared` between them),
ADR-0077/ADR-0078 (Radix + Tailwind — **not applied here**, see §Out of scope),
ADR-0139 (a new-behaviour test is observed red first), ADR-0146 §Discipline
item 5 (*"Real interaction states. Rest, hover, pressed, **focus-visible**,
disabled and loading, designed per variant"* — the standing decision this spec
discharges for one control), ADR-0148 (token architecture — **token conversion
is out of scope**, #2342), ADR-0109 (file-disjoint parallelism), ADR-0128 /
ADR-0129 (what the editor is *not* on).
Constitution: §IV (latency budget — see §Latency below), §VII (dashboard rule,
not engaged), §Testing.

---

## What this is

`apps/shared/src/ui/composites/OverlayEditor.tsx` renders the label as a
`react-rnd` `<Rnd>` with `cursor: 'move'`, no `tabIndex`, no `role`, and no key
handler. `normalizedX`, `normalizedY`, `normalizedWidth` and `normalizedHeight`
are reachable **only** by a mouse drag. The two keyboard-reachable controls
(`overlay-editor-text`, `overlay-editor-font-size`) cover text and font size and
nothing else.

That is **WCAG 2.2 SC 2.1.1 Keyboard (Level A)** — all functionality operable
through a keyboard interface — and under **EN 301 549** it is a procurement
question, not a preference. It is also the failure most likely to be met by an
operator who is not disabled at all: a trackball, a gloved hand, or a touch
panel where fine dragging is genuinely hard.

**This is a frontend-only change to one shared composite.** No backend, no
contract, no migration, no endpoint, no ADR, no new dependency.

## What this is not

- **Not numeric entry, snapping, guides or a drag readout** — that is #2346.
  This spec *defines the step grid* those must adopt (§The step grid, below),
  because #2346's own ordering note says whichever lands first decides it.
- **Not a token conversion** of the file's inline styles (#2342, gated on
  #2332). The focus ring introduced here is an inline style, like everything
  else in this file, and is explicitly listed as #2342's to convert.
- **Not a change to the label's own appearance.** `overlayLabelSurfaceStyle`
  (spec 146) is not touched. `OverlayLabelParity.test.tsx` must keep passing.
- **Not a change to the wall.** `CameraViewer`'s painted label stays
  non-interactive — the wall is read-only, and giving it focus would put a tab
  stop on each of up to 250 tiles.
- **Not multi-label selection** (#2345). One label, the one the editor already
  has.

---

## What was verified, not assumed

Six findings settle the issue's open questions. Each was read out of this
repository or its `node_modules` on this branch.

**1. `react-rnd` does not fight this — it forwards unknown props to the real
DOM node, and it is already fully controlled.**
`node_modules/.pnpm/react-rnd@10.5.3/…/lib/index.js` `Rnd.prototype.render`
destructures a fixed list of its own props (`disableDragging`, `style`,
`position`, `onMouseDown`, `onMouseUp`, `bounds`, the six drag/resize callbacks,
the handle props, `scale`, `allowAnyClick`, `dragPositionOffset`, …) and passes
**everything else** through as `resizableProps` to `re-resizable`'s
`Resizable`. `re-resizable@6.11.2`'s render then reduces its props against a
`definedProps` allow-list and spreads the remainder — `extendsProps` — straight
onto the rendered `div`.

`tabIndex`, `role`, `aria-*`, `onKeyDown`, `onFocus`, `onBlur` and
`data-testid` appear in **neither** list, so they land on the DOM element
verbatim. And `<Rnd>` here is already driven by `position={{x,y}}` +
`size={{width,height}}` derived from `value` — a *controlled* component. The
keyboard path therefore needs **no** bypass, no imperative handle, and no
second element: it calls `onChange` with new normalized values, exactly as the
drag path does, and the next render moves the box.

**2. The three guards render the component bare, and two of them pin it.**
`OverlayEditorCharacterisation.test.tsx` (spec 147 T001) and
`OverlayLabelParity.test.tsx` (spec 146) are the baselines proving 146/147 did
not move behaviour; `OverlayEditorBackdrop.test.tsx` is spec 147's own red.
None uses a Redux `<Provider>`, so nothing added here may require a store.

Two concrete constraints fall out:

- `OverlayEditorCharacterisation.test.tsx` mocks `react-rnd` with
  `Rnd: (props) => props.children` — **a stub that renders no element at all**.
  A keyboard test written against that mock would be testing the stub. The new
  tests must use the **real** `react-rnd`, which
  `OverlayLabelParity.test.tsx` already proves mounts cleanly in jsdom.
- `OverlayLabelParity.test.tsx` asserts `editorLabel.style.border` equals the
  wall label's (empty). The focus indicator must therefore **not** be a
  `border`, and must not be present at rest.

**3. jsdom cannot test a drag, but has no trouble with a key.**
jsdom hard-codes `getBoundingClientRect()` to an all-zero rect and
`offsetWidth`/`offsetHeight` to `0`, which is why spec 147 stubbed `Rnd` at all.
Keyboard handling reads **no geometry from the DOM** — it reads `value` from
props and arithmetic from constants — so it is fully testable. §Testing below
separates that from the three things that are not.

**4. The server refuses a zero-sized label, and the client can already produce
one.** `src/OverlayDesigner/Domain/Overlay/NormalizedSize.cs`:

```csharp
Ensure.That(normalizedWidth).Satisfies(value => value is > 0m and <= 1m, …);
```

`(0, 1]`, zero excluded — *"a label with no area is not a label."* But
`clamp01` in `OverlayEditor.tsx` clamps to `[0, 1]` **inclusive**, and
`OverlayEditorCharacterisation.test.tsx` pins exactly that: a resize to
`offsetHeight: -100` emits `normalizedHeight === 0`. A label in that state
cannot be saved — the `PATCH` earns a 400. In a real browser a mouse cannot
practically reach 0 (`re-resizable` handles plus the label's `padding: '0 4px'`
with `box-sizing: border-box`), but a keyboard *repeating a shrink key* reaches
it deterministically. See FR-008 and §Contradictions.

**5. The domain permits an off-edge label; the editor's drag does not.**
`NormalizedPosition.cs` states it outright: *"a label may describe a rectangle
running off the right edge, and the kiosk composite clips it. That is true today
and is not made an invariant here."* The **editor**, however, passes
`bounds="parent"` to `<Rnd>`, so a drag cannot put the label outside the canvas.
The keyboard must match the editor, not the domain — see FR-007 and
§Contradictions.

**6. `apps/shared` has no `@testing-library/user-event`.**
`apps/shared/package.json` devDependencies carry `@testing-library/react`,
`jsdom` and `vitest` only; `user-event@14.6.6` is a dependency of
`apps/kiosk-web` and `apps/management-web`. The new tests use
`fireEvent.keyDown`, as every existing test in this directory does. **Do not add
a dependency for this.**

---

## The step grid — decided here, for #2346 to adopt

Requirement 2 asks for a step defined against the **normalized** grid so
behaviour does not change with canvas size. The canvas is 800×450, so one pixel
is `0.00125` in x and `0.00222` in y.

| Gesture | Step | On the 800×450 canvas | On a 1920×1080 wall tile |
|---|---|---|---|
| Arrow | **0.005** | 4.0 px × 2.25 px | 9.6 px × 5.4 px |
| Shift + Arrow | **0.05** | 40 px × 22.5 px | 96 px × 54 px |

**Why these two numbers.**

- **One step value for both axes**, not one per axis. The authored space *is*
  normalized; a 0.005 move means the same fraction of the frame on either axis,
  and #2346's numeric inputs will be in the same units. The visual consequence —
  a diagonal nudge is 4 px across and 2.25 px down — is the honest shape of a
  16:9 frame in normalized coordinates, and pixel-squaring it would make the
  step depend on canvas size, which requirement 2 forbids.
- **Coarse is exactly 10× fine**, the arrow/Shift-arrow ratio every drawing tool
  uses (Figma, Illustrator, PowerPoint).
- **Traverse cost is right.** 20 coarse presses cross the canvas; 200 fine
  presses do. A 0.01/0.1 pair would be 8 px per press at authoring size — too
  blunt to sit a label on a machine panel's edge. A 0.0025/0.025 pair would need
  400 presses to cross.
- **Both are exact multiples of `0.0005`, and both divide `1.0` evenly.** A
  label nudged from an exact origin lands on exact values, and the clamp
  boundaries (`0`, `1`) are *reached*, not approached.

**The quantum: every keyboard-produced value is rounded to 4 decimal places**
(`Math.round(v * 10_000) / 10_000`). Two reasons, both load-bearing:

1. Repeated `+= 0.005` in IEEE-754 binary drifts — 0.005 is not exactly
   representable. Without a quantum, 200 presses produce
   `0.9999999999999xyz` and the clamp boundary is never hit exactly.
2. It gives #2346 a grid to agree with rather than relitigate. A drag lands on
   `0.248714`; the first nudge after it snaps to `0.2487` — a 0.08 px change on
   the editor canvas and ~0.2 px on a 1920 px tile, below the threshold of
   anything. **Numeric entry in #2346 should round to the same 1e-4 grid**, and
   any snap grid it adds should be a multiple of `0.0005`.

`decimal` on the server (`NormalizedPosition(decimal X, decimal Y)`) accepts
this exactly.

---

## The keyboard map — decided here

| Keys | Effect |
|---|---|
| `ArrowLeft` / `ArrowRight` | move x by ∓/± 0.005 |
| `ArrowUp` / `ArrowDown` | move y by ∓/± 0.005 |
| `Shift` + arrow | the same move, step 0.05 |
| **`Ctrl`** + `ArrowRight` / `ArrowLeft` | width ± 0.005, **top-left anchored** |
| **`Ctrl`** + `ArrowDown` / `ArrowUp` | height ± 0.005, **top-left anchored** |
| `Ctrl` + `Shift` + arrow | the same resize, step 0.05 |

**Why `Ctrl` for resize, and why not `Alt`.** Requirement 2 spends `Shift` on
the coarse step, so the resize modifier must be something else.
`Alt` + `ArrowLeft`/`ArrowRight` is **browser Back/Forward** on Windows and
Linux in Chrome, Edge and Firefox — precisely the deployment target — and `Alt`
alone opens Firefox's menu bar. `Ctrl` + arrow is unclaimed on a non-text
element in those browsers. The known cost is macOS, where
`Ctrl`+`ArrowLeft`/`Right` switches Spaces at the OS level and cannot be
`preventDefault`-ed; macOS is not a deployment target for either app, the
shortcut is user-configurable, and losing resize-in-x on a developer's Mac is a
smaller harm than losing browser history on every operator console.

Resize is **anchored at the top-left** — the corner `normalizedX`/`normalizedY`
already name — so `Ctrl`+`ArrowRight` moves the right edge right and leaves the
origin alone. That is the bottom-right-handle mental model, and it means resize
and move never interfere.

Every handled combination calls `preventDefault()`; an unhandled key (`Tab`,
`Escape`, `Enter`, `Alt`+anything, plain letters) is left entirely alone so the
dialog's own focus management and the browser's shortcuts keep working.
`Home`/`End`/`PageUp`/`PageDown` are **not** bound — nothing asks for them, and
ADR-0036 forbids inventing them.

---

## Latency budget (constitution §IV)

**N/A — no leg.** `OverlayEditor` is an authoring dialog in
`apps/management-web`, reached from `OverlaysPage`. It is not on
`event arrival → overlay rendered`; none of the six legs is touched, so §VII's
dashboard rule (ADR-0117) is not engaged.

**The one way this spec could reach the budget is named so it can be checked:**
`overlayLabelSurfaceStyle` (`apps/shared/src/ui/composites/overlayLabelStyle.ts`)
*is* on the composite-and-render leg — its own doc says *"called up to 250 times
per wall render (constitution §IV, ≤ 50 ms)"*. **This spec does not modify that
function, does not add a caller, and does not add any prop to
`CameraViewer`.** A reviewer should treat any diff to that file, or any focus /
key handling appearing on `CameraViewer`'s label, as out of scope for this
issue.

---

## User stories

### US1 — An operator places a label without a mouse (P1)

*As a control-room operator with a trackball, a gloved hand, or no pointing
device at all, I tab to the overlay label and move and size it with the arrow
keys, so that placing a label is not a mouse-only operation.*

**This is the whole shippable slice.** It is independently deliverable,
independently observable (tab to it, press a key, watch it move) and it
discharges SC 2.1.1 on its own. US2 is the same slice's assistive-technology
half and ships with it.

### US2 — A screen-reader operator knows what moved (P1)

*As an operator using NVDA or JAWS, the label announces itself when I reach it,
tells me how to move it, and tells me where it went — without reciting four
numbers on every keypress.*

Split out as its own story because its acceptance is a different kind of
evidence (attribute assertions in jsdom, a real screen reader in phase 5), not
because it can ship separately. It cannot: a focusable box that says nothing is
not "operable through a keyboard interface" in any useful sense.

---

## Functional requirements

**FR-001 — The label is a focus stop.** The `<Rnd>` element carries
`tabIndex={0}` and is reachable by `Tab` from the dialog, ahead of the text
input in DOM order. It gains `data-testid="overlay-editor-label"`.

**FR-002 — A focus indicator that survives an arbitrary backdrop.** While
focused, the label paints a **two-ring indicator**: a 2 px inner ring in
`#ffffff` and a 2 px outer ring in `#000000`, drawn with `outline` +
`outlineOffset` and `boxShadow` — **never `border`** (finding 2), and absent at
rest. The two rings contrast 21:1 with **each other**, which is what makes the
indicator perceivable over the checkerboard (`#1f2937`/`#111827`), over white,
over black and over an arbitrary camera frame — no single colour can clear 3:1
against unknown photography. This is the mechanism WCAG 2.2 SC 2.4.11 (Focus
Appearance) sanctions and discharges SC 2.4.7 (Focus Visible, Level A).

**FR-003 — The ring shows on focus, not only on `:focus-visible`.** The file is
inline-styled throughout and a pseudo-class cannot be expressed inline, so the
indicator is driven by `onFocus`/`onBlur` state. The consequence is deliberate:
a mouse click also raises the ring, which doubles as a selection cue for the
trackball operator this issue is partly about. Noted as a departure from
ADR-0146 discipline item 5's *focus-visible* wording, and as #2342's to
reconsider when this file moves to classes and tokens.

**FR-004 — Arrow keys move.** `ArrowLeft`/`ArrowRight` change `normalizedX` by
∓/±`0.005`; `ArrowUp`/`ArrowDown` change `normalizedY`. One keypress, one
`onChange`.

**FR-005 — `Shift` coarsens to `0.05`.** Same axes, same direction, step ×10.

**FR-006 — `Ctrl` resizes, top-left anchored.** `Ctrl`+`ArrowRight`/`ArrowLeft`
change `normalizedWidth` by ±/∓`0.005`; `Ctrl`+`ArrowDown`/`ArrowUp` change
`normalizedHeight`. `Ctrl`+`Shift` uses `0.05`. `normalizedX`/`normalizedY` do
not change.

**FR-007 — A keyboard cannot reach what a drag cannot reach.** Position is
clamped so the label stays wholly inside the canvas, exactly as `bounds="parent"`
already constrains the drag: `x ∈ [0, max(0, 1 − width)]`,
`y ∈ [0, max(0, 1 − height)]`. Size is clamped so `width ≤ 1 − x` and
`height ≤ 1 − y`. **`clamp01` is the outermost clamp and is the same function
the drag path uses** — the reachable-region bound composes with it rather than
replacing it, so requirement 4's letter *and* its stated intent ("a label cannot
be nudged off the canvas any more than it can be dragged off it") both hold. See
§Contradictions.

**FR-008 — Keyboard resize floors at one fine step, not at zero.**
`normalizedWidth` and `normalizedHeight` clamp to a minimum of `0.005`, so the
keyboard cannot produce a value the server refuses (finding 4). This bound
applies to the **keyboard path only**; the drag path's `clamp01` is unchanged
and the characterisation test that pins `normalizedHeight === 0` on a stubbed
resize keeps passing untouched.

**FR-009 — Every keyboard-produced value is quantized to 1e-4.** After the step
and before the clamps, `Math.round(v * 10_000) / 10_000`. Drift cannot
accumulate over repeated presses.

**FR-010 — Nothing but geometry changes.** `text` and `fontSizePx` are carried
through unmodified on every keyboard `onChange`, exactly as `emitGeometry` does
today.

**FR-011 — The drag path is unchanged.** `onDragStop` and `onResizeStop` keep
their present behaviour byte-for-byte, including `clamp01`'s inclusive `[0,1]`
and the `Math.max(…, 24)` / `Math.max(…, 16)` render floors.

**FR-012 — An accessible name.** The label carries
`aria-roledescription="Overlay label"` and an `aria-label` naming it by its
text, falling back to a fixed string when the text is empty.

**FR-013 — Arrow keys must actually reach the handler.** The label carries
`role="application"`, scoped to that one element. Without it, NVDA and JAWS in
browse mode consume arrow keys for the virtual cursor and the handler never
fires — a `div[tabindex="0"]` does not trigger focus mode, and being inside a
Radix `role="dialog"` does not either. The rejected alternative (`role="group"`
and expecting the operator to press NVDA+Space) is not "operable through a
keyboard interface"; it is a workaround the operator has to already know. The
cost — the element's inner text leaves the browse-mode reading order — is
mitigated because the same text is in `overlay-editor-text` and is spoken by
FR-012's name. **This is the one requirement jsdom cannot prove; see §Testing.**

**FR-014 — Instructions, described not announced.** A visually hidden node
(Tailwind `sr-only`, as `DataTable.tsx:47` already uses from `apps/shared`)
states the key map once, and the label references it with `aria-describedby`. It
is static text, not a live region, so it is read on arrival and never again.

**FR-015 — One debounced announcement, naming only what changed.** A
`aria-live="polite"` node — the shape `LayoutEditorDialog.tsx:312` already uses —
announces **500 ms after the last keypress**, not per keypress, and names only
the value that moved: `Left 25%`, `Top 40%`, `Width 30%`, `Height 20%`. A burst
of ten presses produces **one** utterance. Values are rendered as percentages
because that is what an operator can hold in their head; `0.25` and `25%` are
the same number and only one of them is speech.

**FR-016 — A clamp announces its edge.** When a press is refused by FR-007 or
FR-008, the announcement says so — `Left 0%, at the left edge`,
`Width 0.5%, at the minimum` — because otherwise a held key is silent and
indistinguishable from a dead one.

**FR-017 — The live region is empty at rest.** It renders empty until the first
keyboard geometry change, so mounting the dialog announces nothing.

**FR-018 — No store, no dependency.** Nothing added here requires a Redux
`<Provider>` (the three guards render bare), and no package is added to
`apps/shared/package.json` (finding 6).

---

## Acceptance scenarios (Gherkin)

### Happy path

```gherkin
Scenario: An operator nudges a label one fine step right
  Given the overlay editor is open with a label at normalizedX 0.25
  And the label has keyboard focus
  When the operator presses ArrowRight
  Then onChange receives normalizedX 0.255
  And normalizedY, normalizedWidth, normalizedHeight, text and fontSizePx are unchanged
  And after 500 ms the live region reads "Left 25.5%"

Scenario: Shift coarsens the step tenfold
  Given the label is at normalizedY 0.4 with keyboard focus
  When the operator presses Shift+ArrowDown
  Then onChange receives normalizedY 0.45

Scenario: Ctrl resizes from the top-left corner
  Given the label is at normalizedX 0.25, normalizedWidth 0.30, with keyboard focus
  When the operator presses Ctrl+ArrowRight
  Then onChange receives normalizedWidth 0.305
  And normalizedX is still 0.25

Scenario: A burst of presses announces once
  Given the label has keyboard focus
  When the operator presses ArrowRight ten times within 500 ms
  Then onChange has fired ten times
  And the live region has been written exactly once, 500 ms after the last press
```

### Conflict — the clamps

```gherkin
Scenario: A label cannot be nudged off the right edge
  Given the label is at normalizedX 0.75 with normalizedWidth 0.25
  And the label has keyboard focus
  When the operator presses Shift+ArrowRight
  Then onChange receives normalizedX 0.75, unchanged
  And the live region reads "Left 75%, at the right edge"

Scenario: A label cannot be shrunk to nothing
  Given the label is at normalizedWidth 0.005 with keyboard focus
  When the operator presses Ctrl+ArrowLeft
  Then onChange receives normalizedWidth 0.005, unchanged
  And the live region reads "Width 0.5%, at the minimum"
  # 0.005 is the floor because NormalizedSize.From refuses (0, 1]'s open bound

Scenario: Growing a label stops at the canvas edge, not at 1
  Given the label is at normalizedX 0.8 with normalizedWidth 0.19
  And the label has keyboard focus
  When the operator presses Shift+ArrowRight with Ctrl held
  Then onChange receives normalizedWidth 0.2
  And normalizedX is still 0.8

Scenario: A hundred and sixty fine presses reach the boundary exactly
  Given the label is at normalizedX 0 with normalizedWidth 0.2
  And the label has keyboard focus
  When the operator presses ArrowRight a hundred and sixty times
  Then normalizedX is exactly 0.8 — not 0.7999999999999 and not 0.8000000001
  And no intermediate value has more than four decimal places
  # FR-009's quantum is what makes "exactly" true; without it 0.005 drifts
```

### Bad request — keys that are not ours

```gherkin
Scenario: An unhandled key is left to the browser
  Given the label has keyboard focus
  When the operator presses Tab, Escape, Enter, "a", or Alt+ArrowLeft
  Then onChange does not fire
  And the event is not preventDefault-ed
  And the live region is unchanged

Scenario: A handled key is not also a browser shortcut
  Given the label has keyboard focus
  When the operator presses Ctrl+ArrowRight
  Then the event is preventDefault-ed
```

### Auth / scope

```gherkin
Scenario: The keyboard path changes no authorization
  Given an operator who can open the overlay editor
  When they move the label with the keyboard and save
  Then the same PATCH /overlays/{id}/revisions/{n} is issued with the same
       If-Match header and the same scope as a drag would have issued
```

**Auth is genuinely untouched.** `OverlayEditor` is presentational: it calls
`onChange` and nothing else. No endpoint, no scope, no token, no header changes.
That is why there is no `security-reviewer` pass in phase 6.

### Regression — the guards

```gherkin
Scenario: The three existing guards pass unmodified
  Given OverlayEditorCharacterisation.test.tsx, OverlayEditorBackdrop.test.tsx
        and OverlayLabelParity.test.tsx as they stand on develop
  When the keyboard path lands
  Then all three pass with no edit to any assertion
  And the editor label still paints no border at rest
```

---

## Independent end-to-end test procedure

Run by a human (or phase 5) in a real browser, against the Aspire stack. **No
step of this needs a screen reader except step 6, which is reported honestly as
unverified if none is available.**

1. `dotnet run --project src/AppHost`; open `management-web` → **Overlays** →
   open a draft revision's editor.
2. Press `Tab` until the label takes focus. **Expect**: a white-inside /
   black-outside double ring around the label plate, clearly visible against the
   checkerboard.
3. Press `ArrowRight` five times. **Expect**: the label steps right in five
   even, visible increments (≈4 px each), and stops nowhere.
4. Hold `ArrowRight`. **Expect**: the label travels to the right edge of the
   canvas and **stops there** — it never leaves the canvas, and it never
   disappears.
5. Press `Ctrl`+`ArrowRight`, then `Ctrl`+`ArrowLeft` held. **Expect**: the plate
   grows and shrinks from its right edge only; the top-left corner does not
   move; it stops shrinking while still visible, never collapsing.
6. Switch the backdrop to **Captured frame** (spec 147) against a bright scene —
   a ceiling light or a white machine housing. Re-focus the label. **Expect**:
   the ring is still unambiguously visible. Screenshot it; this and step 2's
   screenshot are the evidence for FR-002, and there is no substitute for them.
7. With NVDA running, `Tab` to the label. **Expect**: it announces as an
   *overlay label* by its text, then reads the key instructions. Press
   `ArrowRight` five times quickly. **Expect**: the label moves (proving focus
   mode — FR-013) and **one** announcement of the new left position follows.
8. Move the label to a deliberate position, click **Save**. **Expect**: a `200`,
   not a `400`. Reopen. **Expect**: the position persisted, with at most four
   decimal places in the payload.

---

## Locked tech choices

| Concern | Choice |
|---|---|
| Where | `apps/shared/src/ui/composites/OverlayEditor.tsx`, one file |
| Positioning widget | `react-rnd@10.5.3`, unchanged, driven as the controlled component it already is |
| Styling | inline `CSSProperties`, matching the file; one `sr-only` Tailwind class (`DataTable.tsx` precedent) |
| State | React `useState` in the component. **No Redux** (FR-018) |
| Tests | `vitest` + `@testing-library/react` + `fireEvent`, jsdom. **No `user-event`** (finding 6) |
| Announcement | `aria-live="polite"`, debounced 500 ms (`LayoutEditorDialog.tsx:312` precedent) |
| Dependencies added | **none** |

---

## Contradictions with the issue's own text

Both are reported rather than silently worked around.

**1. Requirement 4's mechanism does not deliver requirement 4's intent.** It
reads *"The existing `[0,1]` clamping (`clamp01`) applies identically to
keyboard movement — a label cannot be nudged off the canvas any more than it can
be dragged off it."* Those are two different rules. `clamp01` bounds each value
to `[0,1]` **independently**, which permits `x = 1` with `width = 0.25` — a
label three-quarters off the canvas. The drag path is bounded by
`<Rnd bounds="parent">`, which is a *different* constraint and stricter.
Applying `clamp01` alone would give the keyboard reach the mouse does not have —
the opposite of the equivalence SC 2.1.1 asks for. FR-007 keeps `clamp01` as the
outermost clamp (the letter) and composes the reachable-region bound inside it
(the intent). Note that the **domain** sides with the issue's literal wording:
`NormalizedPosition.cs` deliberately permits an off-edge rectangle and leaves the
kiosk to clip it. That is a fact #2346 needs, because *typed* numeric entry may
reasonably allow what dragging cannot.

**2. The issue says the `[0,1]` clamp is what should apply, but `[0,1]` produces
a label the server rejects.** `NormalizedSize.From` requires `(0, 1]`. `clamp01`
allows `0`, and the spec-147 characterisation pins that a stubbed resize emits
`normalizedHeight === 0`. Following requirement 4 literally on the resize path
would hand a keyboard operator a deterministic, repeatable way to reach an
unsaveable label — a worse bug than the one being fixed. FR-008 floors the
**keyboard** path at `0.005` and leaves the drag path alone. **The underlying
mismatch — a client clamp of `[0,1]` against a server bound of `(0,1]` — is a
pre-existing defect of the drag path and is not fixed here** (ADR-0036, smallest
change). Recommend filing it; it belongs with #2346, which is where numeric
entry will hit the same wall from the other side.

Two smaller notes, not contradictions:

- The issue cites **SC 2.1.1** for the focus indicator. 2.1.1 is about
  operability; the indicator is **SC 2.4.7 Focus Visible (Level A)** and
  **SC 2.4.11 Focus Appearance (Level AA)**. The work is the same; the citation
  in FR-002 is corrected.
- Requirement 1 says the frame backdrop arrives *"once #2340 lands"*. It has
  landed — `BackdropControls` and `useFrameCapture` are on `develop` — so step 6
  of the test procedure is executable today rather than deferred.

---

## Dependency on PR #2360 (spec 148, open)

**Functional dependency: none.** #2360 adds `resolvedPreview` / `isResolving` /
`resolveFailed` props, a `previewText` local, an `aria-describedby` on
`overlay-editor-text`, and a `<PlaceholderPreviewPanel>` sibling. None of it
touches position, size, focus or keys.

**Textual overlap: one hunk, adjacent lines.** #2360 rewrites
`<span data-testid="overlay-editor-preview">{value.text || ' '}</span>` to
`{previewText || ' '}` — the child of the `<Rnd>` whose opening props this spec
extends, two lines above. Git resolves that cleanly in the common case, and both
sides are small.

**Standing instruction (CLAUDE.md, §Stacked PRs).** **Do not stack.** This
branch is cut from `develop`. If #2360 merges first, `git fetch origin && git
rebase origin/develop` and re-run all four test files before pushing. If #2360
does not land at all, **nothing here changes** — every requirement is
independent of it.

One thing to keep apart on a rebase: this spec adds an `aria-describedby` to the
**label**, #2360 adds one to the **text input**. They are different elements and
neither replaces the other.
