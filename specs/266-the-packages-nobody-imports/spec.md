# Spec 266 — The packages nobody imports

**Issue:** [#2335](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2335)
— *Four Radix packages are installed and imported by nothing*. Label `tech-debt`. Phase 02
of the frontend redesign programme. On Project #13, status **Todo** — verified 2026-09-26
via `gh issue view 2335 --json projectItems`. No `item-add` needed. **Supervised lane**
(ADR-0037): no `agent:ready` label; every gate stops for a human.

**Gate cleared.** The issue was gated on #2332 (tokens). #2332 is closed; its delivery
(spec 257) and the font pipeline (#2333, spec 261) are on `develop`. This spec is written
against the token file that actually landed — `apps/shared/src/ui/tokens/tokens.css` and
`tailwindTheme.ts` at `7aab60de` — not against the issue's description of it.

**Spec number.** Every remote branch, every local branch and every worktree was listed on
2026-09-26. The highest claimed number is **265** (an untracked directory in a worktree);
259 and 260 appear nowhere and are left alone in case an unpushed branch holds them.
**266** is free. Re-check immediately before opening the PR (spec 257 collided three ways
on exactly this).

*Re-checked 2026-09-27, after rebasing onto `origin/develop` (`2f477474`):* #2336's
worktree had also claimed 266 (`266-the-fade-that-stands-for-every-state`); that branch
renumbered its own spec to **268** (commit `6e2712aa`, not yet pushed — `origin` still
shows the old directory until it is). No other branch or worktree claims 266. **266 stays
with this feature.**

**Scope amended 2026-09-27 (product owner):** the command palette is **built** on
`@radix-ui/react-dialog`, not adopted, and is **in** this spec as US6 (§0 Q1, §3.3).

**ADRs referenced:**

- **ADR-0077** (custom design system on Radix + Tailwind): the decision this implements.
  It already names Select, Tabs, Popover and DropdownMenu as primitives; no new ADR is
  needed to build them (§4). The command palette (US6) is built on the Radix Dialog the
  design system already uses, so it too stays inside ADR-0077 as written.
- **ADR-0146** (one discipline, two surfaces): shadow only for what floats, no blur, the
  triad is status vocabulary not decoration, and the wall's prohibitions.
- **ADR-0148** (two-layer OKLCH tokens): primitives cite the semantic layer only.
- **ADR-0078** (Tailwind + CSS custom properties): the mechanism.
- **ADR-0151** (a disable that keeps the operator's place): governs which of `disabled` /
  `unavailable` a menu item or trigger uses.
- **ADR-0139 / ADR-0144** (red first; phase-4a colours). §6 declares **red**.
- **ADR-0150** (waiting is a condition): every new test waits on a condition.
- **ADR-0036** (smallest change, no speculative generality): the basis for every
  "defer" in §3.
- **ADR-0037**, **ADR-0109** (`[P]` rule), **ADR-0053** (sentence-style test names).

**Latency budget (§IV): N/A.** No leg is touched. See §5.

---

## 0. Open questions — the Phase 1 gate does not pass until these are answered

Each is marked `[NEEDS CLARIFICATION]` where it bites below. The spec is written against
the **default** named in each, so a "yes, the default" costs nothing; any other answer
changes the story it names and nothing else.

| # | Question | Default this spec is written against | Bites |
|---|---|---|---|
| **Q1** | ~~Command palette (⌘K): build, adopt, or defer?~~ **Answered 2026-09-27 by the product owner: build**, on `@radix-ui/react-dialog`; no third-party palette library (e.g. `cmdk`). In scope. | **Build.** | US6 (§3.3) |
| Q2 | Does each new primitive ship with **one real first consumer**, or as library code only? | One first consumer each (Select, DropdownMenu, Popover); Tabs library-only (Q3). | US1–US4 |
| Q3 | **Tabs has no consumer today** (§1). Build it library-only, or defer it and **remove** `@radix-ui/react-tabs` until a surface needs it? | Build library-only, as the issue and brief ask. | US4 |
| Q4 | On the layouts list, which row actions stay inline and which move into the menu? | Publish and Edit (new draft) inline; Discard draft, Revert, Archive in a "More actions" menu. | US2 |
| Q5 | Popover's issue-named consumer (data-table filter/column controls) does not exist. Is `StreamHealthBadge`'s details (§1, finding 4) the right first consumer? | Yes. | US3 |

Two further decisions are **architecture**, deliberately **not** made here, and not needed
for anything this spec builds (§3.2): a **notification surface** (Toast) and a **searchable
picker** (combobox) for choosing among up to 250 cameras.

## 1. The issue's premise, re-checked on this tree (`7aab60de`, 2026-09-26)

| Claim in #2335 | On this tree |
|---|---|
| 8 Radix packages in `apps/shared/package.json` | **True.** `react-{alert-dialog,dialog,dropdown-menu,popover,select,slot,tabs,tooltip}`. |
| Only 4 are imported | **True.** `Button.tsx` (slot), `Dialog.tsx`, `ConfirmDialog.tsx` (alert-dialog), `Tooltip.tsx`. `select`, `tabs`, `popover`, `dropdown-menu` are imported by nothing under `apps/`. |
| The console builds selects from native elements | **True.** Nine `<select>` elements in source: `RegisterCameraDialog` (1), `RuleDialog` (2), `SystemVariableDialog` (3), `GridDesigner` (2, rendered per tile), and `BackdropControls` (1, #2342's). |
| No menu primitive; row actions are buttons in a row | **True.** `LayoutsPage` shows up to five row buttons, `OverlaysPage` up to six. `CameraDetailPage.tsx`'s own comment: *"A fourth control here wants a menu rather than a fourth button."* |
| The overlay editor and camera detail "both want" tabs | **Not today.** `CameraDetailPage` is one section (a viewer and four fields). The overlay editor is `OverlayEditor.tsx`, owned wholesale by #2342 and a carve-out in `SharedUiTokenUsageTests`. **No surface has two peer panels to switch between.** → Q3. |
| Popover is wanted for data-table filters/columns | **Those controls do not exist.** `DataTable.tsx` has neither. → Q5. |
| No notification surface | **True.** Outcomes are reported inline (`role="alert"` / `role="status"`) — deliberately so in several specs. |

Findings the issue does not mention, each material to scope:

1. **A native `<select>` and Radix Select are driven differently by every test.** 13 e2e
   call sites use Playwright `selectOption` and 45 unit sites use `user.selectOptions`
   — most of them on `GridDesigner`'s camera/overlay pickers and inside the e2e **seed
   setups** every wall spec depends on. Radix Select is a `button[role=combobox]` plus a
   portalled `listbox`; neither API drives it. Migrating all nine selects is therefore a
   test-infrastructure change as much as a UI one, and is **not** this spec (§3.1).
2. **Radix Select cannot serve the camera picker.** A tile picks one of up to 250 cameras.
   Select offers first-letter typeahead, not search. That picker needs a **combobox**,
   which Radix does not provide — a build-or-adopt call of the same family as Q1 (§3.2).
3. **Radix Select treats `""` as "no value".** `shouldShowPlaceholder` is
   `value === "" || value === undefined` (`@radix-ui/react-select` 2.3.7, `dist/index.mjs`
   line 1140). Every existing "Choose a fab…" option uses `value=""`. The primitive must
   express "nothing chosen" as a placeholder, never as an option (US1, "bad request").
4. **`StreamHealthBadge`'s detail is unreachable from the keyboard.** Its tooltip trigger
   is a `<span>` with no `tabindex`, so the last-frame time and the error string — the
   only place the console states *why* a stream is Offline — can be read by hover alone.
   A Popover on a focusable trigger fixes that, and it is a real consumer that exists
   today (Q5). Its one test of the detail
   (`StreamHealthBadge.test.tsx`, *"Surfaces the error string in the tooltip content"*)
   cannot fail: it asserts the body text matches `/source unreachable|Degraded/`, and the
   pill itself reads "Degraded". It also waits a fixed 250 ms (ADR-0150). US3 replaces it.
5. **`GridDesigner` chose native radios on purpose** (spec 228 FR-001: "a native
   `<input type="radio">` group gives roving tab stop and arrow-key selection from the
   platform, for free"). That is evidence against building a Radio primitive now (§3.2).
6. **`kiosk-web` imports no shared primitive.** Nothing here reaches the wall (§5).
7. **`#2336` (interaction-state matrix) is open and names this issue** as needing "the
   same matrix". The new primitives are built with the states the token file already
   supports (plan.md §2), and #2336 then owns the matrix across every primitive, old and
   new — so #2336 restyles, it does not rebuild. **This includes the command palette
   (US6).** #2336 is being delivered in parallel (sibling worktree, spec 268) and may layer
   hover / pressed / focus / disabled / loading polish onto every primitive here as a
   follow-up. This spec does **not** build that matrix; it only guarantees the hooks the
   matrix will style: the Radix state attributes (`data-state`, `data-highlighted`,
   `data-disabled`, `data-placeholder`) on every new primitive, and — for the palette,
   whose listbox is hand-written because Radix has none — the **same** `data-highlighted`
   attribute on the active option and `data-state="open"` on the dialog content, so one
   selector family styles every floating list in the console.

## 2. User stories

Each story is independently shippable: it adds one primitive in its own file, its own
tests, and (per Q2) one consumer, and it can be observed in a running console without the
others.

### US1 (P1): An operator picks from a fixed list with the design system's Select

The **Action** field of the rule dialog (`RuleDialog.tsx`, *Set a system variable* /
*Highlight an overlay*) becomes the first consumer of a shared `Select` primitive. It
looks like the rest of the design system in all three themes, and behaves as a listbox:
keyboard-openable, typeahead, arrow navigation, Escape to close, focus returned to the
trigger.

**Why P1:** Select is the primitive with the most future consumers (nine native selects).
The rule dialog's Action field is the smallest adoption that proves it end to end: a fixed
two-item enumeration, driven through React Hook Form, which switches the fields below it,
with exactly one e2e call site (`e2e/rules.spec.ts:157-158`).

**Acceptance scenarios**

```gherkin
Scenario: the Action field is a design-system listbox (happy path)
  Given the "New rule" dialog is open
  When the operator focuses the "Action" control and presses ArrowDown
  Then a listbox opens with the options "Set a system variable" and "Highlight an overlay"
  And "Set a system variable" is marked as the selected option

Scenario: choosing an option switches the fields, as the native select did
  Given the "New rule" dialog is open with "Set a system variable" chosen
  When the operator chooses "Highlight an overlay"
  Then the "Overlay" and "Duration (ms)" fields are shown
  And the "Variable name" and "Value expression" fields are gone
  And submitting sends actionType "HighlightOverlay"

Scenario: keyboard-only round trip
  Given the Action listbox is open
  When the operator presses Escape
  Then the listbox closes
  And focus is on the "Action" control
  And the chosen value is unchanged

Scenario: the label names the control
  Then the control is reachable by its label "Action" (getByLabelText / getByRole combobox name)

Scenario: bad request, a caller passes "" as an option value
  Given a Select whose options include one with value ""
  Then rendering throws with a message naming the option label
  And "nothing chosen" is expressed through the placeholder instead

Scenario: disabled
  Given a Select rendered disabled
  Then the trigger cannot be opened by pointer or keyboard
  And its text uses the disabled foreground token, not reduced opacity

Scenario: invalid (form-level error)
  Given the FormField around a Select carries an error
  Then the trigger has aria-invalid="true" and aria-describedby naming the error text
```

*Conflict / auth: N/A — no request semantics change. The dialog's existing submit and
server-error handling is untouched and still covered by its existing tests.*

### US2 (P2): Row actions collapse into a menu `[NEEDS CLARIFICATION: Q4]`

The layouts list (`LayoutsPage.tsx`) keeps its primary actions inline and moves the rest
into a shared `DropdownMenu` behind one "More actions" trigger per row.

**Why P2:** it is the pattern the issue names ("row actions, currently buttons in a row")
and the one `CameraDetailPage` already asks for. It follows US1 because its adoption
touches e2e teardown code (`archive-e2e-layouts.teardown.ts`) that every layout spec
relies on.

```gherkin
Scenario: secondary actions are in the menu (happy path)
  Given a layout chain with a live revision and an open draft
  Then its row shows "Publish" and "Edit (new draft)" as buttons
  When the operator activates "More actions" for that row
  Then a menu opens with "Discard draft", "Revert" and "Archive" as menu items

Scenario: an item that opens a confirmation returns focus sensibly
  Given the menu is open
  When the operator chooses "Archive"
  Then the menu closes and the archive ConfirmDialog opens with focus on "Cancel"
  When the operator presses Escape
  Then the ConfirmDialog closes
  And focus is on that row's "More actions" trigger
  And the page accepts pointer input (no residual pointer-events:none on <body>)

Scenario: only applicable actions appear
  Given a chain with no open draft
  Then the menu contains no "Discard draft" item

Scenario: an in-flight action disables the items
  Given a publish is in flight for the row
  Then every item in that row's menu is disabled (aria-disabled / data-disabled)

Scenario: keyboard navigation
  Given focus is on "More actions"
  When the operator presses Enter
  Then the first enabled item is focused
  And ArrowDown/ArrowUp move between items and Escape closes the menu, returning focus to the trigger
```

*Conflict: the page's existing `isConflict` / `isStaleConflict` banner (a concurrent
publish/archive) is unchanged — the menu item calls the same handler the button did.
Auth: N/A — no new endpoint.*

### US3 (P3): A stream's health detail is reachable from the keyboard `[NEEDS CLARIFICATION: Q5]`

`StreamHealthBadge` becomes a focusable trigger that opens a shared `Popover` stating the
state, the last-frame time and, when not Healthy, the error — replacing the hover-only
tooltip.

```gherkin
Scenario: the detail opens from the keyboard (happy path)
  Given the cameras list shows a camera whose stream is Offline with error "connection refused"
  When the operator tabs to its "Offline" badge and presses Enter
  Then a dialog-role popover opens containing "State: Offline", the last-frame time and "Error: connection refused"
  When the operator presses Escape
  Then it closes and focus returns to the badge

Scenario: Healthy states no error
  Given a Healthy stream whose last poll carried an error string
  Then the popover shows the state and the last-frame time and no "Error:" line

Scenario: unknown stays inert
  Given no stream record for the camera
  Then the badge reads "Unknown" and is not a button (nothing to disclose)

Scenario: the tone is the triad, unchanged
  Then Healthy/Degraded/Offline badges keep their active/warning/fault colours
```

*Conflict / auth: N/A.*

### US4 (P4): Tabs exist as a primitive `[NEEDS CLARIFICATION: Q3]`

A shared `Tabs` primitive exists, tested, with **no consumer** until a surface with two
peer panels appears. If Q3 is answered "defer", this story is replaced by removing
`@radix-ui/react-tabs` from `apps/shared/package.json`, and US5's guard still holds.

```gherkin
Scenario: tabs switch panels (happy path)
  Given Tabs with "Details" and "History"
  When the operator focuses the tab list and presses ArrowRight, then Enter
  Then "History" is selected (aria-selected) and its panel is shown

Scenario: manual activation by default
  Given Tabs rendered with default props
  When the operator presses ArrowRight on "Details"
  Then focus moves to "History" but the "Details" panel stays shown until Enter or Space

Scenario: a disabled tab is skipped
  Given "History" is disabled
  Then ArrowRight from "Details" does not select it

Scenario: bad request, a duplicate tab value
  Given two tabs with the same value
  Then rendering throws naming the duplicate
```

Manual activation is the default because the console's heavy panels open live media: a
panel holding a `CameraViewer` would start a WHEP session on every arrow key under
automatic activation.

### US5 (P1, foundational guard): No installed Radix package is imported by nothing

The issue's finding becomes a build failure rather than an audit discovery.

```gherkin
Scenario: every declared Radix package is used
  Given apps/shared/package.json's dependencies
  Then every "@radix-ui/*" entry is imported by at least one non-test .ts/.tsx file under apps/shared/src

Scenario: bad request, a package is added and never imported (counterfactual)
  Given "@radix-ui/react-switch" is added to dependencies and imported by nothing
  Then the guard fails naming "@radix-ui/react-switch"
```

Red on `develop` today, naming exactly the four packages. It turns green only when US1–US4
(or Q3's removal) land, so it is written first and closed last.

### US6 (P5): An operator jumps to any console surface from the keyboard

A shared `CommandPalette` primitive, built on `@radix-ui/react-dialog` with a
hand-written filtered listbox (`role="combobox"` input + `aria-activedescendant`), and its
one consumer: the management-web shell (`ShellLayout.tsx`), which opens it with ⌘K /
Ctrl+K or a visible **"Go to…"** button in the nav.

**What it lists — navigation destinations only, exactly the nav's.** The seven entries
`ShellLayout` renders today (Cameras, Layouts, Walls, Overlays, Rules, System variables,
Audit — Walls arrived on `develop` after the issue said "six"), read from **one** constant
that both the nav bar and the palette render, so the two cannot drift. No actions ("New
rule", "Register camera"), no entity search (cameras by name), no recents, no fuzzy
ranking: each needs plumbing no consumer has asked for (ADR-0036), and each is a follow-up
that adds items to the same primitive. Filtering is a case-insensitive substring match on
the label.

**Why P5:** it is new behaviour rather than a finding of the issue, it depends on nothing
US1–US4 build, and it ships and is observable on its own.

**Assumption (marked, not asked):** the chord does nothing while any dialog is already
open (a rule being edited, a confirmation). A stray ⌘K must not navigate an operator out
of an unsaved form; the palette is for moving between surfaces, not out of a task.

```gherkin
Scenario: open with the chord, filter, activate (happy path)
  Given the operator is on the Cameras surface
  When the operator presses Control+K (or Meta+K)
  Then a dialog named "Go to" opens with focus in its search field
  And a listbox lists "Cameras", "Layouts", "Walls", "Overlays", "Rules", "System variables" and "Audit"
  And "Cameras" is the highlighted option (aria-activedescendant / data-highlighted)
  When the operator types "rul"
  Then the listbox lists only "Rules"
  When the operator presses Enter
  Then the dialog closes
  And the Rules surface is shown (URL /rules)
  And focus is on the nav's "Rules" link (aria-current="page")

Scenario: arrow keys move the highlight
  Given the palette is open with no query
  When the operator presses ArrowDown twice
  Then "Walls" is highlighted
  When the operator presses ArrowUp
  Then "Layouts" is highlighted
  And ArrowUp on the first option and ArrowDown on the last leave the highlight where it is

Scenario: typing resets the highlight to the first match
  Given the palette is open with "Overlays" highlighted
  When the operator types "a"
  Then the first option whose label contains "a" is highlighted

Scenario: Escape closes and returns focus to where the operator was
  Given focus is on the "Go to…" button
  When the operator presses Enter, then Escape
  Then the dialog closes, the surface is unchanged
  And focus is on the "Go to…" button
  And reopening shows an empty query with the first option highlighted

Scenario: pointer activation
  Given the palette is open
  When the operator clicks "Audit"
  Then the Audit surface is shown and the dialog is closed

Scenario: nothing matches
  Given the palette is open
  When the operator types "zzz"
  Then the listbox has no options and the text "No matching surfaces" is shown
  And Enter does nothing and the dialog stays open

Scenario: conflict, another dialog is already open
  Given the "New rule" dialog is open
  When the operator presses Control+K
  Then no palette opens and the "New rule" dialog keeps focus and its input

Scenario: bad request, a duplicate item value
  Given a CommandPalette whose items contain the value "/rules" twice
  Then rendering throws naming "/rules"

Scenario: the browser's own Control+K is not triggered
  When the operator presses Control+K on any surface
  Then the keydown's default action is prevented
```

*Auth: N/A — the palette lists exactly what the nav already shows to a signed-in
operator, and route access is unchanged (each surface still enforces its own scopes
through the gateway). Conflict: covered by the open-dialog scenario above; no request
semantics change.*

## 3. Scope

### 3.1 In this spec

- Four primitives in `apps/shared/src/ui/primitives/`: `Select`, `DropdownMenu`,
  `Popover`, `Tabs` — each on the Radix package already installed, citing semantic tokens
  only, and passing `SharedUiTokenUsageTests` **with no new carve-out**.
- Three first consumers (Q2): `RuleDialog` Action field, `LayoutsPage` row actions,
  `StreamHealthBadge` detail.
- The US5 guard.
- US6: `apps/shared/src/ui/primitives/CommandPalette.tsx` (on `@radix-ui/react-dialog`,
  already a dependency — no new package), and its consumer in
  `apps/management-web/src/app/ShellLayout.tsx` (one destinations constant shared by nav
  and palette, the "Go to…" trigger, the ⌘K / Ctrl+K listener mounted once), with a new
  `ShellLayout.test.tsx` and a new `e2e/command-palette.spec.ts`.
- Test enablement: `@testing-library/user-event` (14.6.6, the version both apps already
  pin) as a `devDependency` of `apps/shared`, and a jsdom shim for the pointer-capture,
  `scrollIntoView` and `ResizeObserver` APIs Radix's popper and Select call.

**Not in this spec:** migrating the other eight `<select>` elements, `OverlaysPage` /
`CameraDetailPage` / `RulesPage` row actions, or any `BackdropControls` /
`OverlayEditor` file (#2342). Each migration is a small follow-up that copies the first
consumer; the camera/overlay pickers additionally wait on §3.2's combobox decision.

### 3.2 The seven primitives with no package — evaluated

| Primitive | Decision | Reasoning | What would trigger building it |
|---|---|---|---|
| **Badge / status pill** | **Build next, as its own spec** — not here. | The most-duplicated pattern in the product: `StreamHealthBadge`, `RulesPage`'s `StateBadge`, `LayoutsPage`'s `badge()`, and on the wall `LiveUpdatesBadge` and `TileAlignmentBadge`. But consolidating it **touches the wall** (render leg, ADR-0146's "no static bright region larger than a status chip") and needs **three new semantic roles** — a subtle tint per triad hue, mirroring `--color-accent-subtle` — because the current call-site alpha (`bg-accent-active/20`) is exactly what `SharedUiTokenUsageTests` forbids inside `apps/shared/src/ui`. A token change plus a render-leg change is a spec of its own; folding it in would make this one latency-relevant. | Now — file the follow-up issue. |
| **Toast** | **Defer; needs a decision first (architecture).** | There is no notification surface, and the absence is partly deliberate: several specs chose inline `role="alert"`/`role="status"` so an outcome appears *where the operator acted*. A toast moves it elsewhere. On an attended 24/7 console the questions are product ones — which outcomes toast, whether they persist, how they stack, what `aria-live` politeness they carry, whether they ever reach the wall. `@radix-ui/react-toast` is not installed. **This is an ADR-sized decision (a console-wide feedback convention), not a primitive.** | An ADR on how the console reports asynchronous outcomes. |
| **Switch** | **Defer.** | Zero consumers: no `type="checkbox"` or `role="switch"` anywhere under `apps/`. ADR-0036. Needs `@radix-ui/react-switch`. | The first boolean setting in the console. |
| **Checkbox** | **Defer.** | Zero consumers (as above). `DataTable` has no row selection. Needs `@radix-ui/react-checkbox`. | Bulk actions / row selection in `DataTable`. |
| **Radio** | **Defer.** | Two radio groups exist; `GridDesigner`'s chose native radios deliberately (spec 228 FR-001) for platform roving tabindex, and `BackdropControls` is #2342's. A Radix radio group would add a package to replace something that works. | #2336 finding native radios cannot carry the state matrix. |
| **Segmented control** | **Defer.** | The only candidate is `GridDesigner`'s grid-size radios (above). Would need `@radix-ui/react-toggle-group`. | Same as Radio. |
| **Skeleton** | **Defer.** | A skeleton is a loading **state** (#2336's matrix names "loading") with a motion treatment (#2334 owns the motion language; ADR-0146 disqualifies ambient motion on the wall). Building it before either decides would build it twice. | #2334 and #2336 both landed. |

Also out of reach for Select, and **not built here**: **a searchable picker (combobox)**
for cameras and overlays (§1 finding 2). Radix has none. Q1's answer (build, §3.3) makes
building the natural path for it too — the palette's hand-written filtered listbox is the
same ARIA pattern — but the picker's needs (250 items, an inline rather than modal
surface, form-field integration) are not the palette's, so it stays a follow-up that may
extract a shared listbox from US6 once it has a second consumer, not before (ADR-0036).

### 3.3 The command palette (Q1) — decided: build (US6)

The issue: *"The console has six surfaces today and will have more; a palette is the
cheapest way to keep navigation flat as it grows … Radix has no primitive for it, so it is
a build-or-adopt call — flag it in the spec rather than assuming."*

**Decision (product owner, 2026-09-27): build on `@radix-ui/react-dialog`.** The options
as they stood, for the record:

- **Build** (chosen) — the Radix Dialog the design system already uses supplies the modal
  layer (focus trap, Escape, outside-click, focus restoration, portal); the filtered
  listbox (`role="combobox"` + `aria-activedescendant`) is hand-written. Stays inside
  ADR-0077 as written. The cost, accepted: the listbox's accessibility is ours
  (ADR-0077's own stated negative, "bugs in primitives are ours"), which is why US6's
  scenarios pin the keyboard contract case by case.
- **Adopt** (not chosen) a headless palette library such as `cmdk` — would have been one
  dependency but a **second headless library**, amending ADR-0077 and needing an ADR.
- **Defer** (not chosen).

Scope is deliberately the smallest palette that earns the name: the nav's destinations
only (US6). Actions and entity search are follow-ups that add items, not a different
primitive.

## 4. Why no new ADR

ADR-0077 already decides "Radix headless primitives + Tailwind tokens" and lists Select,
Tabs, Popover and DropdownMenu by name. What this spec decides — prop shapes, manual tab
activation, which tokens a highlighted item cites, which row actions stay inline — is
implementation detail inside that decision, written down in plan.md so it is not silent.

**The command palette needs no ADR because it is built, not adopted.** It wraps
`@radix-ui/react-dialog` — a package ADR-0077's Radix layer already covers and
`Dialog.tsx` already imports — and hand-writes only the listbox inside it, with visual
code in the repo on semantic tokens. No second headless library enters the stack, so
ADR-0077 is implemented, not amended. Had the answer been "adopt `cmdk`", that would have
needed an ADR.

**Two things this spec touches would still need an ADR, and neither is decided here:** a
searchable picker's *adopt* path (amends ADR-0077; building it would not), and a
console-wide notification convention (Toast).

## 5. Latency budget (§IV): N/A

No leg is touched. `apps/kiosk-web` imports no primitive from
`apps/shared/src/ui/primitives` (verified by grep on this tree), all four consumers
(including US6's shell) are in `apps/management-web`, and nothing here changes a token the wall reads. The
composite-and-render gate from #2337 needs no new case. If Badge (§3.2) is built, **that**
spec must cite the composite-and-render leg.

## 6. Phase-4a colour: **red** (behaviour-changing)

Every story adds behaviour. Two kinds of red, stated honestly:

- **Primitive tests** (US1–US4) run against a **signature-only stub** committed with them
  — the exact exported props interface from plan.md §3, whose component renders `null`.
  So they are red **on content** ("unable to find role combobox"), not on a missing
  module, and every commit still type-checks (ADR-0087: each commit builds on its own).
- **Consumer tests** are red on content against today's native `<select>`, button row and
  tooltip. A native `<select>` also has role `combobox`, so US1's discriminating assertion
  is the **portalled `listbox`** that ArrowDown opens — a native select never exposes one
  in jsdom.
- **US5's guard** is red on content, naming the four packages.
- **US6** is red on content in both halves: `CommandPalette.test.tsx` against the same
  kind of signature-only stub (no `dialog`, no `combobox`), and the new
  `ShellLayout.test.tsx` against today's shell, which has no "Go to…" button and ignores
  Control+K (so `findByRole('dialog', { name: 'Go to' })` fails). US6 does not affect
  US5's guard: `@radix-ui/react-dialog` is already imported by `Dialog.tsx`.

**Declared green in advance (pins, not 4a evidence):** US1 "choosing an option switches
the fields" and "the label names the control" (true of the native select today);
US3 "the tone is the triad, unchanged"; US2 "only applicable actions appear" (true of the
button row today); US6's shell pin "the nav still renders its seven links" (true today,
and must stay true once the constant is extracted). Their green result is not evidence
the file passed.

**Tests edited, and why that is not a moved characterisation:** the existing
`RuleDialog.test.tsx` `user.selectOptions` calls (lines 98, 246, 247, 264),
`StreamHealthBadge.test.tsx`'s tooltip fallback, and the e2e call sites in §7 change how
they **drive** the control. Their assertions on outcomes (payload `actionType`, which
fields show) stay byte-identical. This is behaviour-changing work, so these edits are
expected, not a characterisation failure.

## 7. Independent end-to-end test procedure

Per story, against the running Aspire stack (`dotnet run --project src/AppHost`), in
Chromium, in each of `data-theme` dark (default), `light` and `high-contrast`:

1. **US1.** Sign in to management-web → Rules → *New rule*. Tab to *Action*, press
   ArrowDown, choose *Highlight an overlay* by keyboard. Overlay/Duration fields appear.
   Create the draft; the row appears. `e2e/rules.spec.ts` covers the same path, with
   lines 157-158 driving the Radix listbox instead of `selectOption`.
2. **US2.** Layouts → a chain with a live revision → *More actions* → *Archive* →
   Escape → focus is on *More actions*; click anywhere — the page responds. Repeat and
   confirm the archive; the row leaves the list. `e2e/kiosk-reconciliation.spec.ts:90-93`
   and `e2e/support/archive-e2e-layouts.teardown.ts:166-178` open the menu before
   *Archive* (Q4 default); every other e2e layout path is unchanged (Publish stays inline).
3. **US3.** Cameras → stop a camera's stream (patch its MediaMTX path, per the repo's
   stream-outage procedure) → Tab to its *Offline* badge → Enter → the popover states the
   error → Escape → focus is on the badge.
4. **US4.** No surface consumes Tabs; verified by its unit tests only (Q3).
5. **US5.** `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~SharedUiDependencyUsage`
   green; the counterfactual in plan.md §5 red.
6. **US6.** Sign in → Cameras. Press Control+K: the "Go to" dialog opens with the caret
   in its field and Chromium's own omnibox search does **not** take focus. Type `rul`,
   Enter → Rules is shown, URL `/rules`, focus on the nav's *Rules* link. Tab to *Go to…*,
   Enter, Escape → focus back on *Go to…*. Open *New rule*, press Control+K → nothing
   opens. Repeat with Meta+K. The new `e2e/command-palette.spec.ts` covers the chord,
   filter, Enter-navigation and Escape-focus paths in Chromium.

Screenshots of each floating surface in all three themes go in the Phase 5 note; the
high-contrast one proves the border carries separation where the shadow is `0 0 #0000`.

## 8. Success criteria

- SC-1: `@radix-ui/react-{select,dropdown-menu,popover,tabs}` each imported by a
  non-test file (or `react-tabs` removed, per Q3); US5's guard green and its
  counterfactual red.
- SC-2: every scenario in §2 has a test, observed red first (except the declared pins),
  quoted verbatim in the PR body.
- SC-3: `SharedUiTokenUsageTests` and `DesignTokenLayerTests` green with **no new
  carve-out and no new token**.
- SC-4: `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test` clean;
  `e2e/rules.spec.ts`, `e2e/layouts.spec.ts`, `e2e/kiosk-reconciliation.spec.ts`,
  `e2e/command-palette.spec.ts` and the layouts teardown green in CI.
- SC-5: no `backdrop-blur`, no animation, no z-index or shadow outside the
  `z-popover`/`shadow-popover` tokens in any new primitive — except `CommandPalette`,
  which is modal and uses `z-overlay`/`shadow-overlay`/`bg-scrim` exactly as `Dialog`
  does, but **without** `Dialog`'s `backdrop-blur-sm` (ADR-0146).
- SC-6: the palette lists exactly the nav's destinations, from one shared constant; no
  new runtime dependency in any `package.json` (the build decision, §3.3).
