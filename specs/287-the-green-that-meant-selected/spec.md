# Feature Specification: The green that meant "selected"

**Spec:** 287 (286 is claimed by open PR #2666's branch; 284 skipped deliberately, see the spec-number memory note)
**Feature Branch**: `enhancement/2623-raw-button-to-button-primitive` (cut from `origin/develop` at `aa08ce16`)
**Created**: 2026-09-28
**Status**: Draft — Phase 1, awaiting gate review
**Issue**: [#2623](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2623) — *Raw `<button>` elements and triad-as-selection-colour should move to `<Button>`*. Filed at #2336's Phase 3 gate (spec 268 plan §7). Labels `enhancement`, `agent:ready`. **Lane:** autonomous (ADR-0144).
**Input**: "32 raw `<button>` elements across the apps should move to the shared `<Button>` primitive … several places use the status triad (green/red/amber) as a selection or button colour rather than status — ADR-0146 item 5 bans the triad as affordance colour."

**ADRs and prior specs referenced:** ADR-0146 items 4 and 5 and *The ground and the accent* (the triad is status vocabulary, never brand; the accent's "job is interactive affordance and selection, nothing else"), ADR-0146 *What the wall subtracts*, ADR-0077 (custom design-system primitives), ADR-0078 / ADR-0148 (tokens), ADR-0151 (`unavailable` vs `disabled`), ADR-0139 + constitution §Testing (red for new behaviour, characterisation for preserved behaviour), ADR-0087 (every commit builds), ADR-0037 / ADR-0144 (phases, lane). Spec 268 (the Button state matrix this applies), spec 277 (`RetryBanner`), spec 228 (GridDesigner's native radios).

**No new ADR for this slice.** It applies ADR-0146 items 4–5 as written. The parts that *do* need a decision are carved out (§2) and not built here.

**Issue body re-read for a lane disclaimer:** none. The body names a dependency (#2336, closed) and asks for extra care on the kiosk; it does not declare itself not-ready. The single comment is the lane's pick-up note.

---

## 1. What was measured (2026-09-28, tree `aa08ce16`)

`grep -rn "<button" apps --include=*.tsx`, tests and `e2e/` excluded, one comment hit (`OverlayEditor.tsx:668`) discarded. **32 raw `<button>` elements — the count matches the issue, the list does not.** Spec 268's inventory was taken before specs 258, 266 and 277 landed: `CellPage.tsx`'s two buttons moved into `LayoutGrid.tsx`; `WallPage.tsx`, `WallForm.tsx` ×2, `WallsPage.tsx`, a third `PickerPage.tsx` button (walls section) and `StreamHealthBadge.tsx` (spec 266 made the pill a popover trigger) are new; spec 277 folded the "nine underline refetch links" into one `RetryBanner` plus four conflict-only *Reload* links, so seven inline text actions remain, not nine; `ShellLayout.tsx:109` is now `:194`.

| # | Site | Today | Class |
|---|---|---|---|
| 1 | console `App.tsx:40` *Sign in* (session expired) | solid `bg-accent-active`, `px-6 py-3` | **In — US1** |
| 2 | console `App.tsx:60` *Try again* (sign-in failed) | tinted `bg-accent-active/20 text-accent-active` | **In — US1** |
| 3 | console `App.tsx:75` *Sign in* (first visit) | solid `bg-accent-active` | **In — US1** |
| 4 | console `ShellLayout.tsx:194` *Try again* (`CrashPanel`) | solid `bg-accent-active` | **In — US1** |
| 5–7 | console `LayoutsPage.tsx:104`, `OverlaysPage.tsx:96`, `SystemVariablesPage.tsx:89` filter chips | selected = `border-accent-active bg-accent-active/10 text-accent-active` | **In — US2** (colour); stays a raw `<button>` (§2 D2) |
| — | console `GridDesigner.tsx:236` preset chip (`<label>` over a native radio, not a button) | same triad selection classes | **In — US2** (colour) |
| — | console `ShellLayout.tsx:173` `NavItem` active (a `NavLink`, not a button) | `bg-accent-active/10 text-accent-active` | **In — US2** (colour). Not in the issue's list; found by the triad grep. |
| 8–9 | kiosk `App.tsx:44,87` *Sign in again* / *Sign in* | solid `bg-accent-active`, `px-6 py-3` | Out — D1 |
| 10 | kiosk `ReconnectingScreen.tsx:33` *Try now* | tinted triad | Out — D1 |
| 11–12 | kiosk `LayoutGrid.tsx:208` *Back to picker*, `:508` *Back* (over live video) | tinted triad / `bg-bg-elevated/60` | Out — D1 |
| 13–15 | kiosk `PickerPage.tsx:72` *Retry*; `:98,:116` layout and wall cards | tinted triad; cards `hover:border-accent-active` | Out — D1 |
| 16 | kiosk `WallPage.tsx:164` *Retry* | tinted triad | Out — D1 |
| 17–23 | `RetryBanner.tsx:16`; *Reload* in `LayoutsPage.tsx:133`, `OverlaysPage.tsx:125`, `RulesPage.tsx:161`, `SystemVariablesPage.tsx:116`; `ChainRecoveryNotice.tsx:358,379` | `className="underline"` inside a fault alert | Out — D2 |
| 24–25 | `WallForm.tsx:143,152` scene *Up*/*Down* | compact `px-2 py-0.5 text-xs` | Out — D1 (size) |
| 26–29 | `BackdropControls.tsx:178,188`, `OverlayEditor.tsx:681,690` | inline styles | Out — owned by #2342 (open) |
| 30 | `DataTable.tsx:115` sort header | text-only header control | Not a Button shape; stays raw (spec 268 §4 already gave it the focus recipe) |
| 31 | `WallsPage.tsx:43` whole-row link-like button | list row | Not a Button shape; stays raw; no triad |
| 32 | `StreamHealthBadge.tsx:39` popover trigger pill | triad **as status** (Healthy/Degraded/Offline) | Correct use of the triad; stays raw |

Triad uses that are status, and stay: `StreamHealthBadge` tones, `DryRunPanel.tsx:75` (matched), `RulesPage.tsx:226` (rule state), `LayoutGrid.tsx:446` / `TileAlignmentBadge` / `LiveUpdatesBadge` (warnings), `CameraViewer.tsx:505`, `PlaceholderPreviewPanel.tsx:32`, every `text-accent-fault` alert.

**Kiosk CSS precondition, checked by a build rather than assumed:** a scratch `vite build` of `apps/kiosk-web` already emits `hover:bg-accent-hover`, `active:bg-bg-pressed`, `focus-visible:outline-focus-ring`, `disabled:text-fg-disabled` — the kiosk scans `apps/shared` sources, so a later kiosk slice needs no Tailwind wiring. Recorded for D1, not used here.

## 2. Carved out — decisions nobody has made (not built here)

Each would change visible behaviour in a way no spec or ADR specifies, which is the #2378 precedent for not guessing. None blocks the console slice: its files are disjoint.

- **D1 — the kiosk, plus any compact site.** (a) `Button` has one size (`px-4 py-2 text-sm` + 1 px border ≈ 38 px tall). The kiosk *Sign in* buttons are `px-6 py-3` text-base ≈ 48 px — the fleet's actual touch targets, which the issue asks to handle with care. Converting shrinks them ~20 %; keeping the size needs a `size` API on `Button` (a design-system decision) because `Button` merges with `clsx`, not `tailwind-merge`, so a call-site `px-6 py-3` races `px-4 py-2` on stylesheet order. `WallForm` *Up/Down* has the opposite problem. (b) `Button`'s base carries `transition-colors`, a paint-triggering property animation; ADR-0146 *What the wall subtracts* disqualifies exactly that in `apps/kiosk-web`, and its console clause allows motion on "transform and opacity only" — so the primitive itself arguably sits outside ADR-0146 on **both** surfaces. That is a finding against spec 268's Button, reported here, not fixed here (changing `Button` restyles every console button). (c) The picker's layout/wall cards are card-shaped (`h2` + metadata, `p-6`, `text-left`); `Button`'s `inline-flex justify-center text-sm font-medium` base would restyle them into something else. (d) `LayoutGrid.tsx` is the wall's own render tree, so a kiosk slice touches the event→overlay path's composite-and-render leg (§IV) and owes its render-leg citation.
- **D2 — inline text actions and toggle chips.** `Button` has no `link` variant (spec 268 §7 already called this "a #2335-shaped question") and no toggle/selected variant. The seven underlined actions sit inside fault alerts and inherit `text-accent-fault` — red text as affordance, which is its own ADR-0146 item-4 question. The three filter-chip groups have no hover/pressed state and no `aria-pressed`; this slice moves their *selected colour* off the triad (mandated) and leaves their state design and semantics to D2.

Both are recommended as follow-up issues **without** `agent:ready` — each needs a human design call, and D1(b) may need an ADR-0146 amendment, which the lane may not write.

## 3. User Scenarios & Testing *(mandatory)*

### User Story 1 — The console's sign-in and crash actions are real Buttons (Priority: P1)

An operator reaching the console signed out, after a session expiry, after a failed sign-in, or after a route crash sees one action. Today it is a hand-styled green block with no hover, pressed or focus state — green being the colour that tells an operator a camera is healthy. With this story each is the shared `<Button>`, so it has spec 268's full state matrix and the accent colour.

**Mapping rule, applied mechanically:** a solid triad fill → `variant="primary"`; a tinted triad fill (`/20`) → `variant="secondary"`. No size override (the console is attended at desk distance with a mouse; the standard Button size is the design system's size — assumption A1). Handlers, labels, `type` and roles unchanged.

**Why P1:** these are the first pixels anyone sees, and the only console actions still drawn in the status triad.

**Independent test:** open the console signed out; the *Sign in* button's computed background equals the `--color-accent` token and differs from `--color-accent-active`; clicking it still starts the redirect.

### User Story 2 — Selection reads in the accent, not in "healthy" green (Priority: P1)

An operator filtering layouts, overlays or system variables, choosing a grid preset, or looking at the nav sees which item is selected. Today selection is drawn in `--color-accent-active` — the same green as a Healthy stream badge on the same console. With this story selection is `border-accent bg-accent-subtle text-accent` (chips, preset) and `bg-accent-subtle text-accent` (nav), per ADR-0146's "the accent's job is … selection".

**Why P1:** ADR-0146 item 4 bans it outright; the green on a selected *Archived* filter chip reads as "archived is healthy".

**Independent test:** on the Layouts page the selected filter chip's computed `border-color` equals the `--color-accent` probe; the active nav link's `color` equals it too; a Healthy stream badge on the Cameras page is still green.

### Acceptance scenarios (Gherkin)

```gherkin
Feature: The console draws affordance and selection in the accent, and status in the triad

  # Happy path — US1
  Scenario: A signed-out operator sees an accent Sign in button
    Given the console is opened with no session
    When the sign-in screen renders
    Then the "Sign in" button's background equals --color-accent
    And it does not equal --color-accent-active
    And hovering it changes its background, and focusing it by keyboard draws the focus-ring outline

  Scenario: The crash panel's retry is a Button and still retries
    Given a console route throws while rendering
    When the "Something went wrong" panel shows
    Then "Try again" is rendered by the shared Button in its primary variant
    And activating it re-navigates to the same path

  # Happy path — US2
  Scenario: A selected filter chip is drawn in the accent
    Given an operator on the Layouts page
    When they select the "Archived" filter
    Then that chip's border colour equals --color-accent
    And no other chip carries it

  Scenario: The active nav link is drawn in the accent
    Given an operator on the Rules page
    Then the "Rules" nav link has aria-current="page"
    And its text colour equals --color-accent

  # Conflict — status must not follow the selection change
  Scenario: Status keeps the triad
    Given a camera whose stream is Healthy
    When the Cameras page renders its health badge
    Then the badge still carries the green status tone (bg-accent-active/20)

  # Bad request — N/A
  # No site in scope issues a request of its own; every handler (signinRedirect,
  # navigate, setFilter, selectPreset) is unchanged and is characterised, not re-specified.

  # Auth
  Scenario: Sign in still starts the OIDC redirect
    Given the console is opened with no session
    When the operator activates "Sign in"
    Then signinRedirect is called exactly as before
    And after an expired session the redirect still carries returnTo = the current path
```

### Independent end-to-end procedure

1. Boot the AppHost; open management-web in a fresh browser context (no session). Read the *Sign in* button's computed `background-color`; compare with a probe painted `var(--color-accent)` and one painted `var(--color-accent-active)`. Hover it; Tab to it.
2. Sign in as the seeded operator. Read the active nav link's `color`.
3. Layouts → select *Archived*; read the chip's `border-color`. Open *New layout* → pick a preset; read the preset label's `border-color`.
4. Cameras → with a Healthy stream, confirm the badge is still green.
5. Repeat 1–3 with `data-theme="light"` and `"high-contrast"` on `<html>`; the selected text stays legible.

### Edge cases

- **High-contrast / light theme:** `--color-accent-subtle` is re-derived per theme; `text-accent` on it must stay ≥ 4.5:1 (pinned, plan §4).
- **Touch on the console:** Button's `hover:` rules sit inside `@media (hover: hover)` (spec 268 pin), so a tap does not latch hover.
- **Expired-session *Sign in*:** passes `state: { returnTo }`; the conversion must keep the arrow function verbatim.

## 4. Requirements

- **FR-001** The four US1 sites render the shared `Button` from `@smart-sentinel-eye/shared/ui/primitives/Button`, with the §3 mapping, no size or colour override, and unchanged `onClick`, label and accessible name.
- **FR-002** The five US2 sites use `border-accent` / `bg-accent-subtle` / `text-accent` for the selected state; the unselected state is unchanged. No triad token appears in any of their literals.
- **FR-003** No file under `apps/kiosk-web/` and no file under `apps/shared/src/ui/` changes.
- **FR-004** The §1 status uses keep their triad classes byte for byte.
- **FR-005** Existing tests covering these sites pass **unmodified** (plan §5 lists them).

## 5. Success criteria

- **SC-001** In the running console, the four US1 buttons and five US2 selected states resolve to the accent token (e2e, computed style vs token probe).
- **SC-002** `grep -rnE "(bg|border|text)-accent-active" apps/management-web/src` returns only status sites (`StreamHealthBadge`, `DryRunPanel`, `RulesPage:226`).
- **SC-003** `git diff --name-only origin/develop` lists no path under `apps/kiosk-web/` or `apps/shared/src/ui/`.

## 6. Locked choices and latency

- React 19 + TypeScript + Tailwind 4 tokens (ADR-0074, ADR-0078, ADR-0148); the existing `Button` primitive (ADR-0077, spec 268) — no new variant, no new prop, no new token.
- **Latency budget (§IV): N/A.** Only `apps/management-web` changes; the kiosk bundle, which carries every leg of the event→overlay path, is untouched (SC-003).

## 7. Assumptions (marked, not buried)

- **A1** The console sign-in and crash buttons adopt the standard Button size, shrinking from `px-6 py-3` text-base to `px-4 py-2 text-sm`. The issue names "the console's sign-in buttons" as moving to `<Button>`, which accepts its look; the kiosk, where size is a touch-target question, is D1.
- **A2** Tinted-triad → `secondary`, not `ghost`: the tinted buttons were visibly a filled block, and `secondary` is the bordered variant closest to that weight.

## 8. Out of scope

D1 and D2 (§2); #2342's four buttons; the three not-a-Button-shape sites; a shared filter-chip composite (three copies exist, but extracting it is D2's decision about what a chip is); an architecture test banning triad-as-affordance app-wide (only statable once D1 and D2 have removed the remaining sites).
