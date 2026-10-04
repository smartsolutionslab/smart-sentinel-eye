# ADR-0146: One discipline, two surfaces

**Status:** **Accepted** (amended 2026-10-04 — item 4: the triad's *text* role may adapt
per theme; see [the amendment](#amendment-2026-10-04-the-triads-text-role-adapts-per-theme))
**Date:** 2026-09-13
**Amends:** —

**Supersedes:** —
**Superseded by:** —

## Context

The brief was "Apple-class design" for both frontends. Taken literally that is a
mistake, and the reason is worth writing down before anyone implements against
it.

Apple's design language was built for a consumer device held at arm's length, in
a lit room, by a person giving it their full attention. `apps/management-web`
matches that description. `apps/kiosk-web` does not: it is a wall of up to 250
tiles (constitution §Non-Functional Requirements → Scale, "250 concurrent
cameras per fab"), read across a control room, running unattended for years.
There, three of Apple's signature moves are actively harmful:

- **Translucency.** `backdrop-filter` forces a compositing layer and a blur pass
  per element. Constitution §IV gives composite-and-render **50 ms**, shared
  across every tile on the wall. A blur that costs 0.2 ms per tile costs the
  whole budget at 250.
- **Ambient motion.** An operator scans the wall for a fault. Anything that moves
  and is not a fault competes for the eye that is looking for one.
- **Large static bright chrome.** These panels never sleep. Chrome that holds the
  same pixels for months burns in.

Meanwhile the actual state of the design system is not a matter of taste at all.
`apps/shared/src/ui/tokens/colors.css` is **seven custom properties, all
colour**: no type scale, no spacing scale, no radii, no elevation, no motion
tokens, no z-layers. No typeface is chosen anywhere, so the apps inherit
Tailwind's default stack and a Windows kiosk renders Segoe UI while a developer's
Mac renders SF — the fleet does not agree on what the product looks like.
`Button.tsx` expresses hover as `hover:opacity-90` on two of four variants and
has no pressed state at all.

So the gap is not that the product looks un-Apple. It is that almost nothing has
been decided, and the parts that have were decided per call site.

## Decision

**The two surfaces share one discipline and diverge on ornament. The direction is
_Instrument_: dark-first, near-black ground, one cool accent, and type and
spacing doing the work that decoration usually does.**

### What both surfaces inherit — the discipline

1. **One type scale**, one spacing rhythm (4-point), one radius scale, one set of
   motion durations. A value is picked from a scale or it is wrong.
2. **Optical alignment over mathematical alignment** where they disagree.
3. **Tabular figures everywhere digits align.** This product is columns of
   timestamps, latencies and camera counts; a face without `tnum` makes every
   table wobble.
4. **The semantic triad survives unchanged.** `--color-accent-active` (`#00c853`),
   `-fault` (`#ff5252`) and `-warning` (`#ffab40`) are domain vocabulary — an
   operator reads green, red and amber as go, fault and caution. They are **not**
   available as brand colour, and the accent introduced below is deliberately a
   hue none of them occupy, so an accent can never be misread as a status.

   > **Amended 2026-10-04 (issue #2695, spec 299).** "Unchanged" is narrowed:
   > the three roles, their names, their hues and the three signal values above
   > survive in every theme, but the colour the triad uses **as text** may take
   > a per-theme lightness and chroma at the same hue, so it stays legible on a
   > light ground. See the amendment below. The original sentence is kept as
   > written: what was decided is a different record from what it became.
5. **Real interaction states.** Rest, hover, pressed, focus-visible, disabled and
   loading, designed per variant. A uniform opacity fade is not a state.
6. **Restraint.** Border, fill, radius and shadow each assert "separate object".
   They are spent where hierarchy needs them, not stamped on every block.

### What the console adds

`apps/management-web` is attended, at desk distance, doing configuration work.
It gets depth and motion:

- Elevation expressed through tonal steps and 1px borders, with shadow reserved
  for surfaces that genuinely float (sheets, popovers, menus).
- Motion on state change and surface entry, 120–200 ms, **transform and opacity
  only**.
- View Transitions between routes.
- A light theme designed as a peer, not an inversion.

### What the wall subtracts

`apps/kiosk-web` gets the discipline and none of the ornament. The following are
**disqualified there**, and a reviewer should treat their appearance as a defect
rather than a preference:

- `backdrop-filter` and any translucency over live video.
- `box-shadow` on a per-tile element.
- Any animation that is not itself a signal. The existing overlay-highlight pulse
  (`apps/kiosk-web/src/styles/index.css`) is the exception that defines the rule:
  it is motion _as_ the alert, and it already honours
  `prefers-reduced-motion`.
- Static bright regions larger than a status chip.
- Any property animation that triggers layout or paint rather than compositing.

### The ground and the accent

The existing base (`#0b0d10`) and elevated (`#14171c`) surfaces are kept — they
are already the right ground for a control room, and keeping them means the wall
does not change colour. The system gains one accent that is **not** a status
hue: a cool cyan in the region of `oklch(78% 0.09 210)`, whose exact ramp
ADR-0148 defines. Its job is interactive affordance and selection, nothing else.

## Consequences

**The wall's section of this ADR is mostly prohibitions, and that is deliberate.**
The honest risk in this programme is not that it looks bad; it is that it looks
beautiful on a developer's Mac and costs a fab frames across 250 tiles. Issue
#2337 files the CI render-budget gate that makes that failure visible, and it is
scheduled _before_ the redesign rather than after.

**The console and the wall will look related, not identical.** Same ground, same
type, same signal colours, different density and different depth. A person who
uses both should recognise them as one product without mistaking one for the
other.

**This does not settle the typeface or the token file layout.** Those are
ADR-0147 and ADR-0148, kept separate because the licensing question and the
CSS-architecture question have different failure modes and different reviewers.

**Rejected: "Material depth"** — the literal Apple treatment, with vibrancy and
layered translucency on the console. It has the highest craft ceiling and it is
what a consumer product should do. It was rejected for this one because the
firewall keeping it off the wall would have to hold forever, enforced by review
alone, on a codebase where both apps import the same `apps/shared` components.
The first shared component that picks up a blur would carry it onto the wall, and
§IV would be spent before anyone measured it. A direction that cannot leak is
worth more here than a direction that is prettier.

**Rejected: "Clinical light"** — a light-first console against the dark wall. It
is defensible (configuration happens at a desk in a lit office; the wall lives in
a dark room) and it was close. It loses on the same coupling: two grounds means
every shared component carries two palettes, and `apps/shared` is where the
component library lives. The light theme still ships — as a designed peer, per
the console section above — it simply is not the default.

**Rejected: "High-contrast industrial"** — maximum legibility, no ornament
anywhere, closer to a cockpit than to software. It is the most defensible choice
for a safety-adjacent product and the least responsive to what was actually
asked for. The discipline section above takes what it is right about.

## Amendment (2026-10-04): the triad's text role adapts per theme

Issue #2695; implemented by spec 299.

### Why item 4 could not stand as written

Item 4 said the triad "survives unchanged" and named three values. Those values
are signal colours tuned for a near-black ground, and in the dark theme they are
legible as text everywhere this product sets them as text (WCAG 1.4.3 contrast,
computed from `tokens.css` with the CSS Color 4 OKLab→sRGB matrices):

| Theme | Triad as text on | active | warning | fault |
|---|---|---|---|---|
| dark | its tint / base / elevated / raised | 7.06 / 8.70 / 8.03 / 7.23 | 8.53 / 10.33 / 9.54 / 8.59 | 5.08 / 6.10 / 5.63 / 5.07 |
| light | its tint / base / elevated / raised | **1.95 / 2.08 / 2.24 / 2.24** | **1.62 / 1.75 / 1.88 / 1.88** | **2.71 / 2.97 / 3.19 / 3.19** |

The console section above requires "a light theme designed as a peer, not an
inversion". With item 4 read literally, that peer cannot render a status label —
"Offline", "Fault", a form's field error — at 4.5:1. The failure is latent only
because both apps' `index.html` pin `data-theme="dark"`; it is real the moment
anything sets `light`.

The only fix is a per-theme colour for triad text, and "unchanged" forbade it.
That made it a decision rather than a detail.

### Decision

**What survives unchanged, in every theme:**

1. The three roles and their names (`--color-accent-active`, `-warning`,
   `-fault`) — still domain vocabulary, still never brand colour, still a hue the
   accent does not occupy.
2. Their **hue identity**. Green means go, amber caution, red fault, in every
   theme; no theme moves a triad colour to another hue.
3. The **signal values themselves** — `#00c853`, `#ffab40`, `#ff5252` — declared
   once, in `:root`, and never redeclared by a theme. Fills, borders, status dots
   and the wall's highlight glow keep citing them.

**What may now differ per theme:** one **text role** per triad hue,
`--color-accent-<role>-text`, used wherever the triad colours text. Its hue is the
triad's own hue (to the same precision the triad is declared); a theme may move
only its **lightness and chroma**, and only as far as that theme's label-text
contexts need to clear WCAG 1.4.3's 4.5:1 — the role's own tint
(`--color-accent-<role>-subtle`) and the three grounds (`--color-bg-base`,
`-elevated`, `-raised`). This is the same latitude every other semantic colour
already has under ADR-0148 decision 1: a theme remaps a role; the primitive scale
stays fixed.

Where the signal already clears 4.5:1 — dark and high-contrast — the text role
**is** the signal. Only a theme that needs it gets a different stop.

**Still not permitted:** a text role at a different hue; a theme dropping or
renaming a triad role; a triad colour (signal or text) used for affordance or
brand. Every other sentence of item 4 stands.

### Consequences

- **Call sites do not change.** Tailwind's `text-*` utilities read the
  `--text-color-*` theme namespace before `--color-*` (verified against the pinned
  Tailwind 4.3.3, 2026-10-04), so the shared theme maps `text-accent-<role>` to
  the text role while `bg-`/`border-accent-<role>` keep the signal. A text use of
  the triad cannot pick the signal by accident, and a new one gets the legible
  colour without anyone remembering to.
- **Amber text in the light theme is dark ochre, not amber.** Any amber that
  clears 4.5:1 on white is; that is the arithmetic of the hue, not a choice.
  Hue identity is what an operator reads, and it is preserved.
- **The guard is mechanical.** Spec 299's `StatusTintTests` assert, per theme,
  that each text role's hue equals its triad's and that it clears 4.5:1 on its
  tint and on every ground — light included. Spec 297's shrink-only light-theme
  exclusion, which cited #2695, is removed with it.
- **High-contrast and dark render exactly as before.** The text role resolves to
  the same signal value there; the only rendered change is in `light`, which no
  shipped page sets today.
