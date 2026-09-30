# Spec 292 — The motion that has a reason

**Issue:** [#2334](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2334)
— "A motion language, with reduced-motion as a designed path rather than an escape hatch".
Phase 01 of the frontend redesign programme. Gate #2329 closed 2026-09-13 by PR #2338
(ADR-0146/0147/0148). Picked up by the autonomous lane (ADR-0144).
**Branch:** `feat/2334-motion-tokens` (cut from `origin/develop` @ `ca5d1c6c`)
**Created:** 2026-09-30
**Lane:** autonomous (ADR-0144)

**ADRs this spec is bound by (and implements; it decides nothing they left open):**

- **ADR-0146** — *One discipline, two surfaces*. The motion stance. Quoted verbatim where
  it binds, §2.
- **ADR-0148** — *Two-layer tokens in OKLCH*. The mechanism (CSS custom properties in one
  shared file, consumed by `tailwind.config.ts` through `tailwindTheme.ts`), the naming
  (`--duration-fast`, `--ease-out` are its own examples), and "prohibitions expressed as
  absence".
- **ADR-0078** — design tokens as CSS custom properties consumed by Tailwind.
- **ADR-0123** / constitution **§IV** — the 50 ms composite + render leg.
- **ADR-0144** — the lane, and the three things it may not do.
- **ADR-0036** — no speculative generality: every token this spec adds has a consumer in
  this spec.

**No new ADR is needed**, and no ADR or constitution section is amended. §4 records the one
interpretation this spec makes of ADR-0146's text, marked as such.

---

## 1. The issue's premise, re-checked on this tree (`ca5d1c6c`)

Four claims in the issue body are stale or wrong. The spec is built on what is true.

| Issue says | Tree says | Consequence |
|---|---|---|
| "There are no motion tokens." | **Spec 257 (#2332) shipped the category**: `--duration-fast: 120ms`, `--duration-moderate: 160ms`, `--duration-slow: 200ms`, `--ease-out: cubic-bezier(0.2, 0, 0, 1)`, `--ease-in: cubic-bezier(0.4, 0, 1, 1)`, `--ease-in-out: cubic-bezier(0.4, 0, 0.2, 1)` (`apps/shared/src/ui/tokens/tokens.css:210-217`), Tailwind keys `duration-{fast,moderate,slow}` / `ease-{out,in,in-out}` (`tailwindTheme.ts:110-119`), and two bridges binding Tailwind's `--default-transition-*` to `--duration-fast` / `--ease-out` (`tokens.css:225-231`). Spec 257 §3 hands "motion roles, springs, reduced-motion as a designed path, the wall's motion rule" to #2334 explicitly. | The scale exists. What is missing is **roles**, **consumers**, **a designed reduced-motion path**, and **the wall rule as a check**. This spec adds those and changes no existing value. |
| "Everything else is Tailwind's default `transition-colors` at its default duration, applied where someone remembered." | Exactly **three** transition sites exist, none at Tailwind's default: `Button.tsx:96` (`transition-colors`, resolving through the bridge to 120 ms / `--ease-out`) and `PickerPage.tsx:101,119` (bare `transition`, **on the kiosk**). No `animate-*`, `motion-*`, `@keyframes`, `.animate(` or View Transition exists anywhere else in `apps/*/src`. | The migration set is three sites, listed in plan §5. |
| The wall is "250 tiles". | `GridDimensions.MaxTiles = 4` (`src/LayoutComposition/Domain/Layout/GridDimensions.cs`). 250 is a *fab* figure. ADR-0146 carries the same error; it is filed as **#2363** (open). Spec 225 §0 corrected it first. | This spec does not inherit it, in any comment or commit message. The corrected case is stronger: the leg is already recorded over budget (ADR-0123, p50 54.2 ms vs 50 ms), so it has no headroom to spend on decoration at any tile count. |
| The highlight pulse is "already guarded by `prefers-reduced-motion`". | True, and it animates **`box-shadow`** — a paint property (`apps/kiosk-web/src/styles/index.css:17-28`). ADR-0146 names it "the exception that defines the rule" while also disqualifying "any property animation that triggers layout or paint" on the wall. | Not a contradiction to resolve here — ADR-0146 names the exception explicitly. It is **allowlisted by name** in the guard (§3 US1) and **left byte-identical**. A compositor-only rewrite of the pulse is a follow-up (§6), not this spec. |

## 2. What #2329 actually decided (ADR-0146, verbatim)

#2329 was closed by PR #2338, which added ADR-0146. The issue thread has no comments; the
ADR is the decision. On motion, ADR-0146 says, verbatim:

> **What both surfaces inherit — the discipline**
> 1. **One type scale**, one spacing rhythm (4-point), one radius scale, one set of
>    motion durations. A value is picked from a scale or it is wrong.

> **What the console adds** — `apps/management-web` is attended, at desk distance, doing
> configuration work. It gets depth and motion:
> - Motion on state change and surface entry, 120–200 ms, **transform and opacity only**.
> - View Transitions between routes.

> **What the wall subtracts** — `apps/kiosk-web` gets the discipline and none of the
> ornament. The following are **disqualified there**, and a reviewer should treat their
> appearance as a defect rather than a preference:
> - Any animation that is not itself a signal. The existing overlay-highlight pulse
>   (`apps/kiosk-web/src/styles/index.css`) is the exception that defines the rule: it is
>   motion _as_ the alert, and it already honours `prefers-reduced-motion`.
> - Any property animation that triggers layout or paint rather than compositing.

And ADR-0148:

> ADR-0146's wall subtractions are not tokens with different values — they are tokens the
> wall has no reason to cite. [...] A prohibition enforced by there being nothing to reach
> for is stronger than one enforced by review.

**On spring curves: ADR-0146 does not call for them.** The issue made springs conditional
("Spring-based curves for the console, if #2329 says so"). The word "spring" appears nowhere
in ADR-0146, 0147 or 0148; the rejected "Material depth" option is the one that carried the
Apple treatment. So **this spec ships no spring curve** — every easing is a `cubic-bezier`
from the existing scale. Adding a spring (CSS `linear()` approximation) later would need its
own decision, because it is exactly the "craft ceiling" ADR-0146 chose not to reach for.

## 3. User stories

### US1 (P1): Motion has four named roles, and the wall has none of them

**As** a contributor adding motion to either app, **I want** a small set of named roles —
state change, surface entering, surface leaving, route transition — each with a fixed
duration and curve, **so that** "how long, what curve" has one answer per role, and **so
that** the wall's prohibition is a failing build rather than a reviewer's memory.

Deliverables: eight role tokens in `tokens.css` citing the existing scale; eight Tailwind
utilities (`duration-{state,enter,exit,route}`, `ease-{state,enter,exit,route}`); the
kiosk's two bare `transition` sites removed; the one existing console transition (`Button`)
cites its role explicitly; a new architecture test `MotionLanguageTests` encoding the wall
rule, the cheap-by-default rule and the designed-reduced-motion rule.

**Independent test:** `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~MotionLanguage`
plus both apps' `tokens.build.test.ts`. On `develop` the wall-rule fact is red on
`PickerPage.tsx` (two sites) and the role-at-call-site fact is red on `Button.tsx`.

```gherkin
Feature: Motion roles and the wall rule

  Scenario: A role utility resolves to its role token (happy)
    Given either app's real index.css compiled by Tailwind 4.3.3
    When the candidates duration-state, duration-enter, duration-exit, duration-route,
         ease-state, ease-enter, ease-exit and ease-route are compiled
    Then each emits transition-duration or transition-timing-function citing
         --duration-<role> or --ease-<role> respectively

  Scenario: Role tokens cite the scale, and resolve inside ADR-0146's 120–200 ms
    Given apps/shared/src/ui/tokens/tokens.css
    Then --duration-state is var(--duration-fast)       # 120 ms
    And  --duration-enter is var(--duration-slow)        # 200 ms
    And  --duration-exit  is var(--duration-moderate)    # 160 ms
    And  --duration-route is var(--duration-slow)        # 200 ms
    And  --ease-state is var(--ease-out), --ease-enter is var(--ease-out),
         --ease-exit is var(--ease-in), --ease-route is var(--ease-in-out)

  Scenario: The existing console transition keeps its rendered timing (characterisation)
    Given Button's class string cites transition-colors duration-state ease-state
    Then its computed transition-duration is 120ms
    And  its computed transition-timing-function is cubic-bezier(0.2, 0, 0, 1)
    # identical to develop, where the same values arrive through the bridge

  Scenario: Ambient motion on the wall fails the build (conflict)
    Given a file in apps/kiosk-web/src, or an apps/shared module the kiosk imports
    When it contains a Tailwind transition/animate/duration/ease/delay utility,
         a motion-safe: or motion-reduce: variant, a @keyframes, an animation or
         transition declaration, an inline-style transition/animation value,
         or a Web Animations .animate( call
    Then MotionLanguageTests fails naming the file, the match, and ADR-0146
    Unless the match is the allowlisted ssE-overlay-highlight-pulse (motion as signal)

  Scenario: A paint-property transition is deliberate or it fails (conflict)
    Given any apps/*/src non-test TS/TSX file
    When a class string contains transition, transition-all, transition-shadow
         or an arbitrary transition-[...] utility
    Then the test fails — these animate paint properties by default
    When a class string contains transition-colors
    Then the test fails unless that file is on the paint-transition allowlist with a reason

  Scenario: A transition without a named role fails (bad request)
    Given a class string with a transition-* utility
    When it does not also cite duration-<role> (state|enter|exit|route)
    Or   it cites a scale step (duration-fast, duration-150, ease-out, duration-[…])
    Then the test fails and names the four roles

  Scenario: A keyframe that animates a paint property fails (conflict)
    Given any @keyframes in apps/*/src/**/*.css
    When a frame declares a property other than opacity, transform, translate, scale, rotate
    Then the test fails, unless it is the allowlisted wall pulse

  Scenario: An allowlist entry that no longer applies fails (honesty)
    Given an allowlist entry (file + match + reason)
    When the file no longer contains the match
    Then the test fails — the list is shrink-only

  # Auth: N/A — no endpoint, token or scope is touched.
```

### US2 (P2): A dialog enters and leaves, and reduced motion keeps the relationship

**As** a console operator, **I want** a dialog to rise into place when it opens and settle
away when it closes, **so that** I can see where it came from and that it has gone; and **as**
an operator with `prefers-reduced-motion: reduce`, **I want** the same open/close relationship
told by fading alone, **so that** I am not given a jump cut instead.

Deliverables: `apps/shared/src/ui/motion/motion.css` holding four keyframes
(`sse-surface-enter`, `sse-surface-exit`, `sse-scrim-enter`, `sse-scrim-exit`) and their
reduced-motion redefinitions (opacity only, **same duration, same curve**); four Tailwind
`animate-*` utilities; `Dialog` and `ConfirmDialog` (content + overlay) wired through
Radix's `data-state`; the stock `animate-{spin,ping,pulse,bounce}` utilities no longer
compile in either app; **the kiosk does not import `motion.css`** (prohibition by absence).

**Independent test:** `e2e/motion.spec.ts` (Chromium, seeded console sign-in, the "Register"
dialog `interaction-states.spec.ts` already opens) reads `document.getAnimations()` on the
dialog content under both `reducedMotion` settings.

```gherkin
Feature: Surfaces enter and leave

  Scenario: A dialog enters with travel (happy)
    Given a signed-in console on /cameras with reducedMotion "no-preference"
    When the operator opens the Register dialog
    Then the dialog content runs animation sse-surface-enter for 200 ms
         with timing cubic-bezier(0.2, 0, 0, 1)
    And  its keyframes animate opacity and transform, nothing else
    And  the overlay runs sse-scrim-enter (opacity only)

  Scenario: A dialog leaves (happy)
    When the operator presses Cancel
    Then the content runs sse-surface-exit for 160 ms with cubic-bezier(0.4, 0, 1, 1)
    And  the dialog is removed from the DOM once the animation ends

  Scenario: Reduced motion is a designed path, not a deletion
    Given reducedMotion "reduce"
    When the operator opens the Register dialog
    Then the content still runs sse-surface-enter for 200 ms          # not "none", not 0 ms
    And  its keyframes animate opacity only — no transform, no travel

  Scenario: The wall cannot reach the surface animations (conflict)
    Given the kiosk's compiled index.css
    Then it contains no sse-surface-* or sse-scrim-* keyframes
    And  no animate-spin, animate-ping, animate-pulse or animate-bounce rule

  # Bad request / auth: N/A — no input, no endpoint.
```

### US3 (P3): A route change in the console cross-fades

**As** a console operator, **I want** moving between top-level sections to cross-fade over the
route duration, **so that** a section change reads as one place replacing another rather than
a flash. Reduced motion keeps the cross-fade: it is opacity, not travel.

Deliverables: the shell's `NavItem` passes React Router 7's `viewTransition`; `motion.css`
sets `::view-transition-old(root)` / `::view-transition-new(root)` to `--duration-route` /
`--ease-route`. The user agent's own cross-fade (opacity only) is kept — no slide.

```gherkin
Feature: Route transitions

  Scenario: A nav click cross-fades (happy)
    Given a signed-in console on /cameras
    When the operator clicks the "Layouts" nav link
    Then document.getAnimations() includes animations on ::view-transition-new(root)
         and ::view-transition-old(root) with duration 200 ms and cubic-bezier(0.4, 0, 0.2, 1)
    And  the URL is /layouts

  Scenario: Reduced motion keeps the cross-fade
    Given reducedMotion "reduce"
    Then the same two pseudo-element animations run, opacity only, 200 ms

  Scenario: A browser without View Transitions navigates normally (degraded)
    Given document.startViewTransition is undefined (jsdom)
    When a NavItem is clicked
    Then navigation completes with no error   # React Router's own fallback

  # Auth: unchanged — the nav lives behind the existing sign-in.
```

## 4. Decisions: settled by ADRs, and the one interpretation

**Settled (applied mechanically):**

- **Mechanism**: CSS custom properties in the one shared token file, Tailwind keys in
  `tailwindTheme.ts` (ADR-0078, ADR-0148). Keyframes cannot be custom properties, so they live
  in `apps/shared/src/ui/motion/motion.css`, citing role tokens only.
- **Names**: `--duration-<role>`, `--ease-<role>` (ADR-0148's `--<category>-<role>` pattern;
  both categories already exist, so no category is added and `DesignTokenLayerTests` needs no
  edit).
- **Durations stay in 120–200 ms** (ADR-0146). No value is added to the scale.
- **Console motion is transform and opacity** (ADR-0146). Every keyframe this spec adds
  animates only those.
- **The wall gets no role utility, no keyframe and no transition** (ADR-0146), enforced by
  absence (`motion.css` not imported by the kiosk; stock `animate-*` removed from the shared
  theme) and by `MotionLanguageTests`.
- **No springs** (§2).

**Chosen within the ADRs' range (no ADR needed; recorded so it is not re-derived):**

- **Role → scale mapping.** Enter 200 ms / ease-out (decelerate into place: the surface's
  origin is the information ADR-0146 wants carried). Exit 160 ms / ease-in (accelerate away:
  the operator already chose to dismiss it, so it gets out of the way faster). State 120 ms /
  ease-out (unchanged from today's bridge). Route 200 ms / ease-in-out (both views are on
  screen at once). **This differs from spec 257 plan §2.7**, which had enter at 160 and leave
  at 200; spec 257 marked that section "the category only; the language is #2334", and
  exit-shorter-than-enter is the convention of every major motion system.
- **The pulse's `animation: none` under reduced motion stays.** For a looping *signal* the
  static ring (outline + 3 px box-shadow, both declared outside the animation) *is* the
  designed alternative: the tile is still visibly marked; only the oscillation goes. It is the
  one allowlisted exception to the "no `animation: none`" rule, with that reason.

**The one interpretation — marked, not buried.** ADR-0146's console bullet reads "Motion on
state change and surface entry, 120–200 ms, transform and opacity only". `Button`'s
`transition-colors` (spec 268 kept it deliberately, deferring to #2334) animates
`background-color`/`border-color`/`color` — paint properties. This spec reads the bullet as
governing **motion** (travel, scale, entry) and treats a colour cross-fade on one control, on
interaction, as a **paint-property transition that must be visibly deliberate** — the issue's
own words: "the expensive thing visibly deliberate", not "forbidden". It is therefore allowed
only via the paint-transition allowlist, one entry (`Button.tsx`), with a reason, and never on
the wall. **If the reviewer reads ADR-0146 literally instead**, the fallback is one line:
delete `transition-colors duration-state ease-state` from `Button.tsx` and the allowlist entry
(hover becomes instant); nothing else in this spec changes. This does not block the run.

## 5. Latency budget (§IV)

- **Composite + render (≤ 50 ms): no new cost on the wall.** Nothing animates on the wall
  that did not before; the pulse is byte-identical. The two sites removed (`PickerPage`) are
  on the layout picker, before any video is shown — not on the render path, but removing them
  is still a (tiny) reduction of kiosk paint work.
- **The render-budget gate (#2337, spec 225, `render-leg-gate` job) needs no change.** It
  measures `overlay_draw` on the four-tile fixture wall; this spec adds no animated property
  there, so there is nothing new for it to measure. It remains the backstop that would catch a
  future violation that got past `MotionLanguageTests` (e.g. a paint-triggering *static*
  style, which this spec's guard does not cover).
- **Console**: not on the event-to-overlay path. N/A.

## 6. Scope

### In this spec
US1–US3 above. Files: plan §1.

### Deferred, and to where

| What | Owner | Why |
|---|---|---|
| Enter/exit on Popover, DropdownMenu, Select, Tooltip, CommandPalette | follow-up issue (to be filed at phase 7) | Same two utilities, one className each; kept out so this slice is reviewable and the e2e covers one surface properly. Plan §7 lists the sites. |
| A compositor-only highlight pulse (opacity on a pseudo-element instead of animating `box-shadow`) | follow-up issue | A rendering change on the render leg; needs spec 225's gate to judge it, and is not what #2334 asks for. |
| Spring curves | a decision nobody has made | §2. |
| Busy/loading motion (spinner, skeleton) | follow-up | Spec 268 US2 and spec 266 deferred it here; ADR-0146 disqualifies it on the wall and it needs its own design on the console. Recorded so the next reader does not assume #2334 covered it. |
| ADR-0146's 250-tile arithmetic | #2363 | Not this spec's to amend. |

## 7. Phase-4a colour: **red**, with two characterisation tasks

Behaviour-changing: new tokens, utilities, keyframes, dialog animation, route transition,
kiosk picker loses its hover fade, stock `animate-*` utilities stop compiling. Red evidence:
`MotionLanguageTests` facts 1–3 (PickerPage, Button), both `tokens.build.test.ts` additions,
`e2e/motion.spec.ts`.

**Declared green in advance, not 4a evidence**: `MotionLanguageTests` facts 4 and 5 (no
keyframe on develop animates a paint property except the allowlisted pulse; no reduced-motion
block deletes motion except the pulse) and fact 6 (allowlist honesty). Each needs a quoted
**counterfactual** (plant a violation, observe red, remove it) in the PR body.

**Characterisation (observed green before and after, assertions unmodified):**
1. `Button`'s rendered timing — `e2e/interaction-states.spec.ts` and `Button.test.tsx`, plus
   a new computed-style assertion captured green on develop *before* the class change.
2. The wall pulse — a new assertion in `apps/kiosk-web/src/styles/tokens.build.test.ts`
   pinning the compiled `.ssE-overlay-highlight` rule and its reduced-motion block, captured
   green on develop first.

## 8. Independent end-to-end test procedure (Phase 5)

1. Boot the stack (`dotnet run --project src/AppHost`), sign in to the console.
2. Open **Cameras → Register**; in DevTools run
   `document.getAnimations().map(a => [a.animationName, a.effect.getTiming().duration])` during
   the open: expect `sse-surface-enter, 200` and `sse-scrim-enter, 200`. Close: `sse-surface-exit, 160`.
3. Rendering → *Emulate CSS prefers-reduced-motion: reduce*; repeat: same names and durations,
   `a.effect.getKeyframes()` contains no `transform`.
4. Click nav links: a 200 ms cross-fade; same under reduced motion.
5. **Ask the running wall, once** (the guard reads source; this reads the product): open a
   four-tile wall in the kiosk and run `document.getAnimations().length` → **0** with no
   highlight active; trigger a highlight → exactly the `ssE-overlay-highlight-pulse`
   animations, one per highlighted tile.
6. Read the `render-leg-gate` job's verdict on the PR's CI run and quote it.

## 9. Success criteria

- SC-1: eight role tokens and eight role utilities exist; every value resolves inside 120–200 ms.
- SC-2: zero motion on the kiosk outside the pulse — by source guard and by step 5's live read.
- SC-3: every `@keyframes` in the tree animates compositor properties only, except the pulse.
- SC-4: under `reduce`, dialogs still animate (opacity, same duration); nothing is `none` or 0 ms
  except the pulse.
- SC-5: `Button`'s computed transition timing is unchanged from develop.
- SC-6: `render-leg-gate` passes on the PR.
