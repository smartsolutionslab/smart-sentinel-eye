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

**ADRs referenced:**

- **ADR-0077** (custom design system on Radix + Tailwind): the decision this implements.
  It already names Select, Tabs, Popover and DropdownMenu as primitives; no new ADR is
  needed to build them (§4).
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
| **Q1** | **Command palette (⌘K): build, adopt, or defer?** The issue asks that this be flagged, not assumed. **This spec takes no position** (see §3.3). | *None.* Out of this spec whatever the answer. | — |
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
   new — so #2336 restyles, it does not rebuild.

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

## 3. Scope

### 3.1 In this spec

- Four primitives in `apps/shared/src/ui/primitives/`: `Select`, `DropdownMenu`,
  `Popover`, `Tabs` — each on the Radix package already installed, citing semantic tokens
  only, and passing `SharedUiTokenUsageTests` **with no new carve-out**.
- Three first consumers (Q2): `RuleDialog` Action field, `LayoutsPage` row actions,
  `StreamHealthBadge` detail.
- The US5 guard.
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

Also out of reach for Select, and therefore not decided here: **a searchable picker
(combobox)** for cameras and overlays (§1 finding 2). Radix has none; it is
build-or-adopt. It is the same decision family as Q1 — `cmdk`, for example, serves both a
palette and a combobox — so they should be decided together.

### 3.3 The command palette (Q1) — flagged, not decided

The issue: *"The console has six surfaces today and will have more; a palette is the
cheapest way to keep navigation flat as it grows … Radix has no primitive for it, so it is
a build-or-adopt call — flag it in the spec rather than assuming."*

**This spec builds nothing for it and assumes nothing about it.** The facts a decision
needs, without a recommendation:

- **Build** on `@radix-ui/react-dialog` (already used) plus a hand-written filtered
  listbox (`role="combobox"` + `aria-activedescendant`). Stays inside ADR-0077 as written.
  The listbox and filtering are the parts Radix would otherwise give for free; the
  accessibility work is ours (ADR-0077's own stated negative: "bugs in primitives are
  ours").
- **Adopt** a headless palette library (e.g. `cmdk`, which composes Radix Dialog). One
  dependency, unstyled, so ADR-0077's "all visual code lives in the repo" still holds —
  but ADR-0077 names **Radix** as the headless layer, so adopting a second headless
  library **amends ADR-0077** and needs an ADR.
- **Defer** until navigation actually strains. Six surfaces fit a sidebar today.
- Either build path also yields the **combobox** §3.2 needs for the camera picker; that
  coupling is the strongest reason to decide the two together.

## 4. Why no new ADR

ADR-0077 already decides "Radix headless primitives + Tailwind tokens" and lists Select,
Tabs, Popover and DropdownMenu by name. What this spec decides — prop shapes, manual tab
activation, which tokens a highlighted item cites, which row actions stay inline — is
implementation detail inside that decision, written down in plan.md so it is not silent.

**Three things this spec touches would need an ADR, and none is decided here:** the
command palette's adopt path (amends ADR-0077), a searchable picker's adopt path (same),
and a console-wide notification convention (Toast).

## 5. Latency budget (§IV): N/A

No leg is touched. `apps/kiosk-web` imports no primitive from
`apps/shared/src/ui/primitives` (verified by grep on this tree), all three consumers are
in `apps/management-web`, and nothing here changes a token the wall reads. The
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

**Declared green in advance (pins, not 4a evidence):** US1 "choosing an option switches
the fields" and "the label names the control" (true of the native select today);
US3 "the tone is the triad, unchanged"; US2 "only applicable actions appear" (true of the
button row today). Their green result is not evidence the file passed.

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
  `e2e/rules.spec.ts`, `e2e/layouts.spec.ts`, `e2e/kiosk-reconciliation.spec.ts` and the
  layouts teardown green in CI.
- SC-5: no `backdrop-blur`, no animation, no z-index or shadow outside the
  `z-popover`/`shadow-popover` tokens in any new primitive.
