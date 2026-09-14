# Spec 154 — An edit you can take back

**Issue:** #2347 — *The overlay editor has no undo.*
**Branch:** `feat/2347-undo-in-the-overlay-editor` (cut from `origin/develop`
at `313bb50c`, nothing stacked)
**Lane:** autonomous (ADR-0144) — `agent:ready`, Project #13.
**Phase 4a colour:** **red.** New behaviour: a new capability, new key
bindings, new controls. A test that arrives green is a phase-4 failure
(ADR-0139, constitution §Testing).
**ADRs:** ADR-0037 (phases), ADR-0144 (lane), ADR-0109 (`[P]` markers),
ADR-0074 (two React apps), ADR-0075 (RTK Query — and what this spec
deliberately does *not* use), ADR-0077 (the `Button` primitive),
ADR-0079 (RHF `Controller` owns the value), ADR-0113 (the If-Match version
read this must not disturb), ADR-0123 (the render leg is the operator's wait
— the latency argument), ADR-0139 + constitution §Testing (red for new
behaviour), ADR-0146 (management-web is the arm's-length surface).
**New ADR needed:** **no for what is built; one is worth filing separately.**
See *An ADR that does not exist yet* below — the rule "a composite in
`apps/shared` does not assume a Redux store" is load-bearing for this spec,
is pinned by a test, and lives only in spec 150's FR-018. That is an
observation to file, not a blocker, and the lane may not write ADRs anyway.

---

## What this is

Inside one editing session in the overlay editor, an operator can take back
what they just did — and put it back again. A drag that overshoots, a resize
that went the wrong way, a typed word, a nudge run that crossed the canvas:
`Ctrl+Z` returns the label to where it was before that action, `Ctrl+Shift+Z`
redoes it.

The floor of the stack is **the state the dialog opened with**. Undoing to
the bottom is therefore the same operation as "revert to opened", and there
is no way to undo past it.

## What this is not

- **Not a second "Revert" control, and not a new use of the word.** See
  *Decision 1* — the issue's ranked-first half already ships, and `revert`
  already means something else in this feature (`revertOverlayRevision`,
  `overlays.api.ts:129`, wired at `OverlaysPage.tsx:54` — publication-level,
  not editing-session).
- **Not a change to what `onChange` emits.** Every payload every existing
  emission site produces today is byte-for-byte unchanged. Undo *adds* a
  second reason for `onChange` to fire; it does not alter the first.
- **Not a backend change.** No file under `src/` is touched. Undo is
  session-local and never reaches the wire. If a task proposes a C# edit, it
  is wrong.
- **Not Redux.** `OverlayEditor` lives in `apps/shared`, is rendered bare by
  five suites with no `<Provider>`, and `OverlayEditorKeyboard.test.tsx:693`
  asserts exactly that. Respected — see *Decision 4*.
- **Not design tokens** (#2342, gated on #2332). The two new controls use the
  file's existing inline-style idiom, not Tailwind and not tokens.
- **Not a touch on `apps/management-web/src/features/layouts/**`** — PR #2373
  is open there. `LayoutEditorDialog` has the same hole; out of scope,
  recorded as an observation.
- **Not undo of the backdrop, the captured frame or the selected camera.**
  See *Decision 3*.

---

## What was verified, not assumed

Read on `origin/develop` @ `313bb50c`, after #2364 (PR #2369) and #2346
(spec 151) merged.

| Claim | Where | Verdict |
|---|---|---|
| A drag is already one `onChange` | `OverlayEditor.tsx:283-290` (`handleDragStop`), `:308-318` (`handleResizeStop`) — `onDrag`/`onResize` write local `preview` state only | **Confirmed.** The task's correction 1 is right and the issue body is wrong. |
| The text input fires per keystroke | `OverlayEditor.tsx:546` — `onChange={(e) => onChange({ ...value, text: e.target.value })}` | **Confirmed.** |
| Arrow nudge fires per keypress, and key repeat makes it a stream | `OverlayEditor.tsx:428` — `emitNormalized(...)` at the end of `handleLabelKeyDown`; `OverlayEditorKeyboard.test.tsx:409` holds a key for 160 presses | **Confirmed.** |
| #2346 added four geometry fields that fire per commit | `OverlayEditor.tsx:335-340` (`handleGeometryCommit`), `OverlayGeometryFields.tsx:105-138` (`commit`), `:153-179` (Enter / Escape), blur-commits at the input | **Confirmed — and already atomic.** One commit, one `onChange`. Escape discards without emitting. |
| **The font-size slider is a fifth emission site, and it streams** | `OverlayEditor.tsx:556-564` — `<input type="range" onChange={(e) => onChange({ ...value, fontSizePx: Number(e.target.value) })}` | **New.** Named nowhere in the issue, nowhere in the three corrections, and nowhere in the task brief. A dragged range input fires `change` per step; so does a held arrow key on it. See *Contradictions*, item 1. |
| The dialog now opens on a saved label | `OverlayEditorDialog.tsx:134-137` (`defaultValues` from `editTarget`), `:151-153` (`reset(defaultValues)`) | **Confirmed.** |
| **Revert-to-opened-state already ships, in its meaningful form** | `OverlayEditorDialog.tsx:233-238` — closing calls `reset(defaultValues)`, and since #2364 `defaultValues` *is* the saved label | **Contradicts the task's correction 3.** See *Contradictions*, item 2. |
| `OverlayEditor` is rendered only by `OverlayEditorDialog` | grep of `apps/` — the sole non-test import outside `apps/shared` is `OverlayEditorDialog.tsx:14`; `kiosk-web` never imports it | **Confirmed.** This is the latency argument. |
| No `max-lines` rule anywhere in TypeScript | `grep -rn "max-lines"` across every eslint config in `apps/` — zero hits | **Confirmed.** ADR-0084's 300-LOC cap is SonarAnalyzer and C#-only. `OverlayEditor.tsx` is 602 lines; judged on readability, not on a rule. |
| The dialog reads `currentData`, never `data`, at both query sites | `OverlayEditorDialog.tsx:88-92` (chain version) and `:165-178` (resolve preview), each with a paragraph of reasoning | **Confirmed.** This spec adds no query read and changes neither. It does have to survive their re-renders — see *Decision 4* and *The RTK Query interaction*. |

---

## The decisions

### Decision 1 — Scope: step-wise undo/redo only. One mechanism, which delivers both halves.

The issue ranks revert-to-opened above step-wise undo and says *"if only one
ships, it should probably be that one."* **That one shipped, and #2364 is
what finished it.**

`OverlayEditorDialog.tsx:233-238` resets the form on every close, and since
#2364 the value it resets to is `editTarget.label` — the saved label — not
the `DEFAULT_INPUT` constant. Cancel (`:299`) calls `onOpenChange(false)`,
which runs that reset. "Discard and close, returning to the state the dialog
opened with" is, verbatim, what the Cancel button does today, on a saved
draft.

So the remaining value in #2347 is step-wise undo — and undo with the floor
at the opened state **subsumes** the other half anyway: hold `Ctrl+Z` and you
arrive at the opened state without closing the dialog, which is strictly more
than Cancel offers. One mechanism, two behaviours, no second control and no
second word.

The word `Revert` is additionally unavailable: `revertOverlayRevision` is a
wired mutation meaning "restore a published revision" (`OverlaysPage.tsx:54`).
Reusing it for editing-session state would collide in the operator's
vocabulary and in grep.

### Decision 2 — The undo boundary, per emission site. Five sites, four rules.

| # | Site | Code | Emission rate | Boundary | Rule |
|---|---|---|---|---|---|
| 1 | Drag | `handleDragStop`, `:283` | one per completed gesture | the emission itself | **Atomic.** One step, no coalescing. |
| 2 | Resize | `handleResizeStop`, `:308` | one per completed gesture | the emission itself | **Atomic.** |
| 3 | Geometry field commit | `handleGeometryCommit`, `:335` | one per accepted commit | the commit | **Atomic.** A refused draft and an Escape emit nothing, so they are not steps. |
| 4 | Label text | `:546` | one per keystroke | **idle** | A run of typing is one step. The run closes after `TEXT_IDLE_MS` with no further keystroke, **or** on blur of the input, **or** on the first emission from any other site. |
| 5 | Font-size slider | `:556` | one per slider step, dragged or arrowed | **idle** | Same rule as 4, same constant, same three closers. |
| 6 | Arrow nudge | `handleLabelKeyDown` → `emitNormalized`, `:428` | one per keypress, **including OS key repeat** | **keydown → keyup, per axis and mode** | A held run is one step. The run closes on `keyup` of any arrow, on blur of the label, or on the first emission from any other site. **Not** on an idle timer. |

Two things this table is deliberately careful about.

**Sites 4/5 and site 6 cannot share a rule, and the reason is mechanical.**
OS key repeat fires `keydown` roughly every 30 ms with **no intervening
`keyup`** until release. An idle timer wide enough to hold a 160-press run
together is also wide enough to merge two *deliberate*, separate nudges made
a beat apart. `keyup` is the exact, unambiguous end of the gesture, and it is
available; using a timer where an event exists would be guessing at something
the browser already tells us.

Conversely, typing and dragging a slider have no "release" that means
anything — a range input's pointer-up does end a drag, but the same control
is also arrowed, and `change` on a text input has no terminator at all. Idle
is the only boundary those two have.

**The run key includes the axis and the mode.** `arrow:move:x`,
`arrow:move:y`, `arrow:resize:width`, `arrow:resize:height`. Holding
`ArrowRight` then, without releasing, also pressing `Ctrl` switches from
moving to resizing — a different intent, so a different run, so a new step.
`Shift` does **not** break a run: it coarsens the step within the same
gesture on the same axis, which is what spec 149 designed it to be.

### Decision 3 — Undo covers `OverlayLabel`'s six fields. Nothing else.

`text`, `normalizedX`, `normalizedY`, `normalizedWidth`, `normalizedHeight`,
`fontSizePx`. The authoring-session state #2340/#2347's sibling specs added —
`backdrop`, `capturedFrame`, `selectedCamera` (`OverlayEditor.tsx:444-446`)
— is out.

Three reasons, in order of weight:

1. **Those are not edits.** They are not persisted, they never reach
   `onChange`, and `OverlayEditorBackdrop.test.tsx:196` and `:207` pin
   "emits no `onChange`" as a requirement. Undo restores what would be saved;
   the backdrop is the operator's viewing context, not their work.
2. **Undo that changes the backdrop is the surprising one.** An operator who
   captured a frame to check alignment and then pressed `Ctrl+Z` to take back
   a drag would lose the frame — and the frame cost a WHEP session.
3. **It cannot be half-done.** Recording `backdrop` into the same stack means
   an undo can be a no-op on the label, which breaks the invariant that every
   undo step is visible on the thing being edited.

The counter-argument in the brief — "one that ignores it may also be
surprising" — is real but weaker: the backdrop's own controls are radio
buttons in plain view and are trivially reversible by clicking the other one.
`OverlayEditorBackdrop.test.tsx:218` already pins that round-trip
("Restores the checkerboard exactly after switching away and back").

### Decision 4 — The history lives in the editor, in React state, with no Redux and no new required prop.

`OverlayEditor` stays Redux-free. That is not a preference — five suites in
`apps/shared` render it bare, two of them are characterisation baselines, and
`OverlayEditorKeyboard.test.tsx:692-696` is an explicit test named *"Renders
with no Provider in the tree, exactly as the three existing guards do"*
(spec 150 FR-018). Departing would break a guard whose entire purpose is to
stop this.

The history is a hook — `useOverlayEditHistory` — in its own file, holding
two snapshot stacks and a run token in `useRef`/`useState`. `OverlayEditor`
calls it; nothing above it changes.

**The re-seed problem, and why it needs no prop.** `OverlayEditor` is
controlled: the parent owns `value`. The parent also *replaces* it from
outside, on open and on target change (`OverlayEditorDialog.tsx:151-153`,
`reset(defaultValues)`). The history must treat that as "a new session,
new floor", not as an undoable step.

The hook distinguishes the two by **object identity against what it last
emitted**. Every emission (including an undo's own) records the emitted
object in a ref. On render, `value === lastEmitted` means the echo of our own
change; anything else is an external re-seed, which clears both stacks,
closes any open run, and makes the incoming `value` the new floor. First
mount satisfies this trivially.

This needs no new prop, which matters twice: the five bare-render guards keep
calling `OverlayEditor` with exactly `value` and `onChange`, and
`OverlayEditorDialog.tsx` is not modified at all — so
`OverlayEditorDialogChainRetention.test.tsx`, the suite that exists to pin
`currentData` against `data`, is untouched by construction.

The alternative — an explicit `editSessionKey` prop passed down from the
dialog — was rejected for those two reasons. It is the fallback if identity
tracking proves unreliable in practice; the task list says how that would be
discovered.

### Decision 5 — Undoing past the opened state is impossible by construction.

The floor is not a rule the undo action checks; it is the absence of anything
below it. `reset(defaultValues)` replaces `value` from outside, the hook sees
a value it did not emit, and both stacks are cleared with that value as the
new base. There is nothing older to return to. `canUndo` is false at that
point and the Undo control is disabled.

This holds for all four transitions `OverlaysPage` can drive: create → open,
edit A → open, edit A → close → edit B, and edit A → save → close. The last
one matters most: after a successful edit, `onSubmit` calls
`reset(defaultValues)` (`OverlayEditorDialog.tsx:195`) before closing, so a
reopen cannot reach a pre-save state through undo.

---

## User stories

### US1 (P1) — Take back the last thing I did

**As** an operator positioning a label, **when** I drag, resize, type, nudge
or type an exact number and it comes out wrong, **I want** to press `Ctrl+Z`
and have the editor return to the state before that action, **so that** I do
not have to reconstruct a value I can no longer see.

This is the independently-shippable slice. It is observable end to end:
open the editor on a saved draft, drag the label, press `Ctrl+Z`, watch the
label return and the four geometry fields follow it.

#### Acceptance scenarios — US1

```gherkin
Scenario: A completed drag is one undo step
  Given the overlay editor is open on a label at Left 10%, Top 10%
  When the operator drags the label to Left 40%, Top 30%
  And the operator presses Ctrl+Z
  Then the label is back at Left 10%, Top 10%
  And onChange has been called twice — once for the drag, once for the undo
```

```gherkin
Scenario: A completed resize is one undo step
  Given the overlay editor is open on a label 30% wide and 8% tall
  When the operator resizes it to 50% wide and 12% tall
  And the operator presses Ctrl+Z
  Then the label is 30% wide and 8% tall again
```

```gherkin
Scenario: A run of typing is one undo step, not one per keystroke
  Given the overlay editor is open on a label reading "Furnace"
  When the operator types " Bay 3" into the label text field
  And no further keystroke arrives for the idle interval
  And the operator presses Ctrl+Z
  Then the label text reads "Furnace"
  And a single Ctrl+Z was enough
```

```gherkin
Scenario: A held arrow run is one undo step, however long the key was held
  Given the overlay editor is open on a label at Left 10%
  And the label has keyboard focus
  When the operator holds ArrowRight for one hundred and sixty key-repeat presses
  And releases ArrowRight
  And presses Ctrl+Z
  Then the label is back at Left 10%
```

```gherkin
Scenario: Releasing and pressing again is two steps, not one
  Given the overlay editor is open on a label at Left 10%
  When the operator presses ArrowRight ten times and releases
  And presses ArrowRight ten times again and releases
  And presses Ctrl+Z once
  Then the label is at the position it held after the first run
  And it is not back at Left 10%
```

```gherkin
Scenario: Switching axis mid-hold starts a new step
  Given the label has keyboard focus at Left 10%, Top 10%
  When the operator holds ArrowRight for five presses
  And, without releasing, presses ArrowDown five times
  And presses Ctrl+Z once
  Then Top is back at 10%
  And Left still holds the value the rightward run produced
```

```gherkin
Scenario: Ctrl mid-hold starts a new step, Shift does not
  Given the label has keyboard focus
  When the operator holds ArrowRight for three presses
  And, without releasing, holds Shift and presses ArrowRight three more times
  Then all six presses are one undo step
  But when the operator instead holds Ctrl and presses ArrowRight
  Then the resize presses form a second, separate undo step
```

```gherkin
Scenario: A committed geometry value is one undo step
  Given the overlay editor is open on a label at Left 10%
  When the operator types "33.33" into the Left field and presses Enter
  And presses Ctrl+Z
  Then the Left field reads 10%
  And the label has moved back with it
```

```gherkin
Scenario: A refused or escaped geometry draft is not an undo step
  Given the overlay editor is open on a label at Left 10%
  When the operator types "abc" into the Left field and presses Enter
  Then the field shows "Enter a number."
  And Ctrl+Z does nothing, because nothing was committed
  And the Undo control is still disabled
```

```gherkin
Scenario: Redo puts back exactly what undo took
  Given the operator has dragged the label and pressed Ctrl+Z
  When the operator presses Ctrl+Shift+Z
  Then the label is back at the dragged position
```

```gherkin
Scenario: A new edit after an undo discards the redo stack
  Given the operator has dragged, pressed Ctrl+Z, and can redo
  When the operator makes any new edit
  Then the Redo control is disabled
  And Ctrl+Shift+Z does nothing
```

```gherkin
Scenario: Undo cannot go past the state the dialog opened with — edit mode
  Given the dialog was opened on a saved draft reading "Furnace", Left 10%
  When the operator drags the label and types "Kiln"
  And presses Ctrl+Z repeatedly until the Undo control is disabled
  Then the label reads "Furnace" at Left 10%
  And further Ctrl+Z presses change nothing
```

```gherkin
Scenario: Undo cannot go past the state the dialog opened with — create mode
  Given the dialog was opened with New overlay
  When the operator edits and then undoes to the bottom of the stack
  Then the label is exactly DEFAULT_INPUT's label
```

```gherkin
Scenario: Closing and reopening on a different draft starts a fresh history
  Given the operator edited draft A without saving and closed the dialog
  When the operator opens the editor on draft B
  Then the Undo control is disabled
  And no state from draft A is reachable by undo
```

```gherkin
Scenario: A saved edit is not undoable after reopening
  Given the operator edited a draft and saved it
  When the operator reopens that draft
  Then the Undo control is disabled
  And the pre-save label is not reachable
```

```gherkin
Scenario: The backdrop is not part of the history
  Given the overlay editor is open with the checkerboard backdrop
  When the operator selects the White backdrop
  And drags the label
  And presses Ctrl+Z
  Then the label is back at its pre-drag position
  And the backdrop is still White
  And onChange fired for the drag and the undo only — never for the backdrop
```

```gherkin
Scenario: A query settling does not erase the history
  Given the operator has dragged the label so Undo is available
  When the placeholder-resolve query settles and re-renders the editor
  Then the Undo control is still enabled
  And Ctrl+Z still returns the label to its pre-drag position
```

```gherkin
Scenario: An uncommitted geometry draft is left alone by undo
  Given the operator has dragged the label
  And has typed "77" into the Width field without committing it
  When the operator presses Ctrl+Z
  Then the label returns to its pre-drag position
  And the Width field still shows the uncommitted "77"
```

```gherkin
Scenario: Bad request — undo with nothing to undo
  Given the dialog has just opened and nothing has been edited
  When the operator presses Ctrl+Z
  Then nothing changes
  And onChange is not called
  And the Undo control is disabled rather than present-and-inert
```

```gherkin
Scenario: Auth — undo reaches no endpoint at all
  Given the operator's session has expired
  When the operator presses Ctrl+Z in an open editor
  Then the undo succeeds locally
  And no HTTP request is issued
  And Save is where the expired session surfaces, exactly as today
```

```gherkin
Scenario: Conflict — undo does not disturb the If-Match version
  Given the dialog is open on a saved draft and has read the chain's version
  When the operator edits, undoes, and clicks Save
  Then the PATCH carries the version read from useGetOverlayQuery's currentData
  And it is unaffected by how many undo steps were taken
```

### US2 (P2) — Know that it happened, without looking

**As** a screen-reader user, **when** I press `Ctrl+Z`, **I want** the editor
to say what happened, **so that** I am not guessing whether the shortcut was
heard.

Separable from US1 and droppable to a follow-up if US1's review runs long.
It is P2 only because US1 without it is usable by sighted operators, not
because it is optional in the long run — the editor already carries three
live regions and a convention for them.

#### Acceptance scenarios — US2

```gherkin
Scenario: An undo is announced
  Given the operator has dragged the label
  When the operator presses Ctrl+Z
  Then a polite live region announces that the change was undone
```

```gherkin
Scenario: A refused undo is announced as refused, not silently
  Given nothing has been edited
  When the operator presses Ctrl+Z
  Then the live region says there is nothing to undo
```

```gherkin
Scenario: The undo announcement does not collide with the geometry announcer
  Given the editor's geometry live region exists
  Then the undo announcement is in its own region with its own data-testid
  And spec 149's geometry announcements are unchanged
```

---

## Functional requirements

- **FR-001** Two snapshot stacks, past and future, of `OverlayLabel` values,
  scoped to one editing session.
- **FR-002** Every `onChange` the editor emits from sites 1-6 records a step,
  subject to the coalescing rules in *Decision 2*.
- **FR-003** Sites 1, 2 and 3 are atomic: one emission, one step.
- **FR-004** Sites 4 and 5 coalesce by idle: the run closes after
  `TEXT_IDLE_MS` of no emission from that site, on blur of the control, or on
  the first emission from any other site.
- **FR-005** Site 6 coalesces by `keydown` → `keyup`, keyed on
  `arrow:{move|resize}:{x|y|width|height}`. The run closes on `keyup` of any
  arrow key, on blur of the label, or on the first emission from any other
  site. `Shift` does not break a run; `Ctrl` does, by changing the key.
- **FR-006** `Ctrl+Z` (and `Cmd+Z`) undoes. `Ctrl+Shift+Z`, `Cmd+Shift+Z` and
  `Ctrl+Y` redo.
- **FR-007** The bindings are handled on the editor's root element so they
  work wherever focus sits inside it, and are `preventDefault`-ed. They are
  **not** conditioned on the event target — see *The one-undo-model choice*.
- **FR-008** Visible `Undo` and `Redo` controls, disabled when the
  corresponding stack is empty, placed below the canvas and **before** the
  label text input in DOM order.
- **FR-009** Any new step clears the future stack.
- **FR-010** An undo's own emission is not itself a step, and does not clear
  the future stack.
- **FR-011** A `value` arriving from the parent that the editor did not emit
  clears both stacks, closes any open run, and becomes the new floor.
- **FR-012** Undo restores only the six `OverlayLabel` fields. `backdrop`,
  `capturedFrame` and `selectedCamera` are untouched by undo and never enter
  the stacks.
- **FR-013** No history state reaches Redux, and `OverlayEditor` continues to
  render with no `<Provider>` in the tree.
- **FR-014** (US2) A dedicated `aria-live="polite"` region, with its own
  `data-testid`, announcing undo, redo and the refused case.
- **FR-015** No emission payload from any existing site changes.
- **FR-016** `OverlayEditorDialog.tsx` is not modified.

### The one-undo-model choice

FR-007 intercepts `Ctrl+Z` even while a text or geometry field has focus,
rather than deferring to the browser's native input undo there.

The alternative — let the field keep its own undo, take over only elsewhere —
sounds more respectful and is worse in practice. React controlled inputs
already make native undo unreliable: the browser restores the DOM value, React
re-renders it back from props, and the two disagree in ways that differ per
browser. Offering an undo that works differently depending on which of two
adjacent controls has focus is a worse surprise than one that always means the
same thing.

The cost is real and is recorded: an operator who wanted to un-type one
character inside the field gets the whole typing run back instead. That is the
coalescing rule working as designed, and it is what every drawing editor does.

---

## An independent end-to-end test procedure

Run against the Aspire stack, in `management-web`, without reading any test
code.

1. Boot the stack. Sign in as an operator with overlay scopes.
2. Go to **Overlays**. Create a draft named `undo-probe-<timestamp>` with the
   text `Furnace`, save it as a draft.
3. Reopen it with **Edit**. Note Left / Top / Width / Height in the four
   numeric fields.
4. Drag the label to the far corner. The fields follow. Press `Ctrl+Z`. **The
   label returns and all four fields read their step-3 values.**
5. Press `Ctrl+Shift+Z`. **The label returns to the corner.**
6. Click into the text field, type ` Bay 3`, wait a beat, press `Ctrl+Z`
   **once**. **The text reads `Furnace` again** — not `Furnace Bay 3` minus
   one character.
7. Click the label to focus it. Hold `ArrowRight` for two full seconds, let
   the key repeat carry it across the canvas, release. Press `Ctrl+Z`
   **once**. **The label returns to where the hold began.**
8. Press `Ctrl+Z` repeatedly. **The Undo button goes disabled with the label
   reading `Furnace` at its saved geometry** — the state step 3 opened on.
   Press `Ctrl+Z` again: nothing happens.
9. Select the **White** backdrop. Drag the label. Press `Ctrl+Z`. **The label
   moves back; the backdrop stays white.**
10. Type `{{camera.name}}` into the text field and wait for the placeholder
    preview panel to resolve. **The Undo button is still enabled**, and
    `Ctrl+Z` still works — the query settling did not erase the history.
11. Click **Save draft**. Reopen with **Edit**. **The Undo button is
    disabled** and the pre-save label is unreachable.
12. Open **New overlay**. Edit anything, then undo to the bottom. **The label
    is `Overlay text` at 10/10/30/8, 32px** — `DEFAULT_INPUT`.

Steps 4, 6, 7 and 8 are the feature. Steps 9-12 are the boundaries that make
it safe.

---

## Locked tech choices

React 19 function component + hooks (ADR-0074). No new dependency — no
`use-undo`, no `immer` patch history, no Redux Toolkit undo middleware
(ADR-0075 governs *server* cache state; this is ephemeral component state and
the editor is deliberately store-free). The two controls use the existing
`Button` primitive if it can be adopted without a Tailwind/token change
(ADR-0077, #2342), and the file's own inline-style idiom otherwise. Tests:
Vitest + Testing Library, matching the six existing suites.

---

## Latency-budget impact

**N/A.** Constitution §IV's legs are the kiosk wall's `event arrival →
overlay rendered` path. `OverlayEditor` is rendered by exactly one component,
`OverlayEditorDialog` in `apps/management-web`; `kiosk-web` never imports it,
verified by grep across `apps/`. Nothing in this spec runs on the wall.

Stated explicitly because the **composite + render** leg is already over its
50 ms budget at p50 54.2 ms (ADR-0123) and nothing may be added to it
casually. This adds nothing to it — not a little, none. ADR-0146's argument
applies instead: `management-web` is the arm's-length surface, where an
affordance like an Undo button is exactly what belongs.

The one figure worth stating on the authoring side: the idle constant in
FR-004 is a felt-latency choice, not a budget one. `DEBOUNCE_MS = 250`
(`useDebouncedValue.ts:11`) is the repo's existing "a typed word has stopped"
constant and is the obvious candidate; `ANNOUNCE_DELAY_MS = 500`
(`OverlayEditor.tsx:67`) is the existing "a key-repeat burst has stopped"
constant. Plan.md picks one and says why.

---

## The RTK Query interaction

The design **reads nothing from a query hook.** It has no `data` /
`currentData` choice to make of its own.

It does, however, have to survive query-driven re-renders, and this is where
the codebase has been bitten three times (#2341, #2364, #2368). The relevant
fields, named:

- `useResolveOverlayTextQuery` → **`currentData`** (`OverlayEditorDialog.tsx:166`,
  used at `:178`). Reaches `OverlayEditor` as `resolvedPreview`, `isResolving`,
  `resolveFailed`. A **new object identity** every time the query settles.
- `useGetOverlayQuery` → **`currentData`** (`:89`). Does not reach
  `OverlayEditor` at all, but drives `disabled` on Save and re-renders the
  dialog, and therefore the `Controller`, and therefore the editor.

Both already use `currentData` and both are correct; this spec changes
neither and modifies neither line (FR-016).

**The consequence for FR-011.** The re-seed detector must compare `value`
against the object the editor last emitted — never "did the component
re-render", never a deep-equality check on props, never a dependency array
containing `resolvedPreview`. Both of those would clear the history every time
a placeholder resolved. That is scenario *"A query settling does not erase the
history"* and it is a required test, not a nice-to-have: it is the exact shape
of the three bugs above.

---

## Guards — which survive unmodified

All seven. This is the claim, and T001 verifies it by running them before any
source change and again after.

| Suite | Why it survives |
|---|---|
| `apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx` | Pins the canvas, the pixel mapping, and the three original emission payloads. Undo adds emissions; it changes none of theirs. |
| `apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx` | **The one to watch.** Its sixteen arrow-combo tests fire `keyDown` with no `keyUp` and assert `onChange` payloads — coalescing changes *history*, not emissions, so they are unaffected. Its DOM-order test (`:103`) requires the label to precede the text input; the new controls go between them, preserving that. Its no-`Provider` test (`:693`) is the constraint Decision 4 honours. |
| `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx` | Backdrop state is explicitly out of scope (Decision 3). Its "emits no onChange" assertions are a *requirement* this spec restates. |
| `apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx` | Kiosk-side label rendering. Not touched. |
| `apps/shared/src/ui/composites/OverlayLabelParity.test.tsx` | Reaches the label via `overlay-editor-preview`'s `parentElement`, and asserts styles. No style changes. |
| `apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx` | `OverlayGeometryFields.tsx` is not modified. Its commit/blur/Escape protocol is consumed as-is and is what makes site 3 atomic. |
| `apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx` | `OverlayEditorDialog.tsx` is not modified (FR-016). Untouched by construction. |

**If any of these has to be edited, stop.** A guard that needs adjusting is
evidence the behaviour moved, and the two characterisation baselines among
them exist precisely to make that loud.

---

## Contradictions with the issue and with the corrections

The brief asked for these explicitly. Mine can be wrong too; each carries
where to check it.

1. **There is a fifth emission site, and nobody has named it.** The font-size
   slider (`OverlayEditor.tsx:556-564`) is a `type="range"` whose `onChange`
   fires per step — a dragged slider and a held arrow on it both stream. The
   issue names drag; correction 2 names typing, arrows and geometry fields;
   the brief repeats those three. None names the slider. Without a rule it
   would be the worst-behaved site in the editor: sliding the font from 32 to
   200 is ~168 undo steps. Covered by FR-004. *Check by reading `:556-564`.*

2. **Correction 3 is right that revert-to-opened is now meaningful, and wrong
   that it is therefore something to build.** #2364 made `defaultValues` the
   saved label; `OverlayEditorDialog.tsx:233-238` already resets to
   `defaultValues` on every close, so Cancel already performs
   "discard and close, returning to the state the dialog opened with" — the
   issue's ranked-first half, in its meaningful form, for free, the moment
   #2369 merged. What does not exist is revert **in place**, without closing,
   and undo-to-floor is that. So the ranking the brief says "applies for the
   first time" resolves to: build the other one. *Check by reading
   `OverlayEditorDialog.tsx:129-137` and `:230-238` together.*

3. **Correction 1's line reference is stale, its claim is not.** It cites
   `OverlayEditor.tsx:420-422` for inline `onDragStop`/`onResizeStop` arrows.
   On current `develop` those are extracted `useCallback`s at `:283` and
   `:308`, and `:420` is inside `handleLabelKeyDown`. Spec 151 added
   `onDrag`/`onResize` preview handlers alongside them. The claim — one
   completed gesture, one `onChange` — is still exactly true. Worth saying
   only because the citation would send the next reader to the wrong lines.

4. **The issue's own motivating sentence is now false.** *"Since there is no
   numeric entry (#2346) and no readout, the operator cannot even see what
   the value was before they moved it."* #2346 merged (spec 151): four
   numeric fields with a live readout that tracks the drag. The *need* for
   undo survives — seeing a number is not the same as getting it back — but
   the argued severity is lower than the issue text implies.

5. **`OverlayEditorDialogChainRetention.test.tsx`'s own comment cites
   `OverlayEditorDialog.tsx:71-75` for the `data` destructure.** Those lines
   are now the `getToken` ref block; the hook is at `:88-92` and already
   reads `currentData`. Stale comment in a merged guard. Not this spec's to
   fix — recorded so a reader chasing the `data`/`currentData` history does
   not conclude the fix was lost.

6. **ADR-0146 says "a wall of up to 250 tiles"** in its opening argument.
   `GridDimensions.MaxTiles = 4` (#2363). Does not affect this spec — the
   quoted reasoning about compositing cost is directionally unchanged — and
   is already filed. Noted because the brief asked for it.

## An ADR that does not exist yet

**"A composite in `apps/shared` does not assume a Redux store."** It is
enforced by a test (`OverlayEditorKeyboard.test.tsx:693`), it decided where
spec 148 put its query, it decides where this spec puts its history, and its
only written form is FR-018 in spec 150. That is a rule of the same kind as
the boundary rules that do have ADRs, recorded only in a spec nobody will
grep.

**This spec does not write it.** The lane may not author ADRs (ADR-0144), and
nothing here is blocked: the rule is honoured either way. Filed as an
observation for a human to decide.

## Observations filed, not fixed

- `LayoutEditorDialog` has no undo either, and the same argument applies to
  it. Out of scope — `apps/management-web/src/features/layouts/**` is owned
  by open PR #2373.
- `OverlayGeometryFields`' Escape-discards-draft and this spec's `Ctrl+Z` are
  two different "take it back" gestures a keystroke apart. Both are correct
  in their own scope and the acceptance scenario pins the interaction, but a
  future pass may want one story for both.
