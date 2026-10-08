# ADR-0146: One discipline, two surfaces

**Status:** **Accepted** (amended 2026-10-04 — item 4: the triad's *text* role may adapt
per theme; see [the amendment](#amendment-2026-10-04-the-triads-text-role-adapts-per-theme);
note 2026-10-07; corrected 2026-10-08 — see
[the correction](#correction-2026-10-08-the-wall-caps-at-four-tiles-not-250))
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
matches that description. `apps/kiosk-web` does not: it is a wall of **at most
four tiles** (ADR-0112, `GridDimensions.MaxTiles = 4` — "a real NFR ceiling, not
config"), drawn from a fab of up to 250 concurrent cameras (constitution
§Non-Functional Requirements → Scale), read across a control room, running
unattended for years. There, three of Apple's signature moves are actively
harmful:

- **Translucency.** `backdrop-filter` forces a compositing layer and a blur pass
  per element. Constitution §IV gives composite-and-render **50 ms**, shared
  across every tile on the wall — and ADR-0123 measures that leg at **p50
  54.2 ms on a real wall, already over budget with no translucency or ambient
  motion at all** (p95 79.2, max 164.6; #1891). There is no headroom for a
  compositing layer and a blur pass per tile, whatever the tile count.
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
beautiful on a developer's Mac while spending a composite-and-render budget
that is already exceeded (ADR-0123's p50 54.2 ms against §IV's 50 ms). Issue
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

> **2026-10-07 (issue #2709, spec 308).** "Wherever the triad colours text" means
> on a theme surface — the tint and the three grounds this section names. Text on
> `--color-bg-video`, which is black in every theme, uses
> `--color-accent-<role>-on-video` instead: declared once in `:root`, equal to the
> signal, never redeclared by a theme, because the signal already clears 4.5:1
> there in every theme. Decided on the issue by a human before the spec; recorded
> here so this sentence is not contradicted by the token file.

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

## Correction (2026-10-08): the wall caps at four tiles, not 250

Issue #2363.

### What was wrong

This ADR stated in three places that the wall carries up to 250 tiles. It does
not. A wall caps at **four tiles** — `GridDimensions.MaxTiles = 4`, `MaxCells =
4` (2×2), enforced at `Layout.cs:78` and fixed by **ADR-0112**, which calls it
"a real NFR ceiling, not config". The 250 figure is the constitution's **250
concurrent cameras per fab** (§Non-Functional Requirements → Scale) — a fab has
up to 250 cameras; a wall shows at most four of them at once. The original text
conflated the two.

The error also reached the translucency argument's arithmetic (0.2 ms per tile
times 250 is the whole 50 ms budget; times four it is 0.8 ms, not a binding
constraint) and the closing rationale ("costs a fab frames across 250 tiles").

### Why the prohibitions hold anyway, on better evidence

The cheap fix would be to replace "250" with "4" and leave the reasoning
standing — but that would leave the translucency prohibition justified by
arithmetic that no longer binds at the real tile count. The prohibitions hold
for a reason this ADR did not originally use: **the composite-and-render leg is
already over budget.** ADR-0123 measures a real wall at p50 **54.2 ms** against
§IV's 50 ms budget (p95 79.2, max 164.6; #1891) — breaching today, with no
translucency and no ambient motion at all. There is no headroom to spend,
whatever the per-tile multiplier.

The other two prohibitions were never tile-count arguments. Ambient motion
competes with the operator's eye scanning for a fault, true at four tiles or at
four hundred. The unattended-for-years framing is about burn-in and drift, not
throughput. Neither needed correcting.

### What changed

The premise, the translucency arithmetic, and the closing rationale above are
corrected in place to cite ADR-0112's four-tile ceiling and ADR-0123's measured
breach, rather than the 250-camera figure. No prohibition changes; only the
reasoning that supports them.

Corrected already, before this ADR, on issues #2337 and #2353, and in spec
150's working notes.
