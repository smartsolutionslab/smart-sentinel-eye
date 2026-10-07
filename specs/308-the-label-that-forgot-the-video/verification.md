# Verification 308: The label that forgot the video

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2709

## Phase 5 — what was observed

**What**: the on-video triad text role (`text-accent-{fault,warning}-on-video`) resolves to the
signal colour in every theme, and clears WCAG 1.4.3's 4.5:1 against `--color-bg-video` with the
scrim composited on top — in every theme, not just the one spec 299 was fixing for. Also confirmed
live: the pre-fix `-text` role (what `ViewerOverlay` used before this PR) genuinely fails 4.5:1 in
light theme, reproducing #2709's reported defect rather than taking the issue's numbers on faith.

**How**: built the real `apps/management-web` bundle (`vite build`), extracted the compiled CSS, and
loaded it in a headless Chromium (Playwright) page rendering both the new on-video classes and the
old `-text` class over a `--color-bg-video` ground with `.bg-scrim` composited on top — for each of
`dark`, `light`, `high-contrast`. Read `getComputedStyle(...).color` / `.backgroundColor` and
normalized every value (including browser-native `oklab()`/`oklch()` output) to displayed sRGB via a
canvas round-trip, then computed the WCAG relative-luminance contrast ratio in-process. This is a
different measurement path than phase 6's own OKLCH→sRGB script — independent confirmation, not a
re-read of the same number.

**What the output was** (verbatim):

```
=== theme: dark ===
new on-video fault                   text=rgb(255,82,82) ground=rgb(0,0,0) contrast=6.58:1 PASS
new on-video warning                 text=rgb(255,171,64) ground=rgb(0,0,0) contrast=11.15:1 PASS
OLD -text fault (pre-fix, for comparison) text=rgb(255,82,82) ground=rgb(0,0,0) contrast=6.58:1 PASS

=== theme: light ===
new on-video fault                   text=rgb(255,82,82) ground=rgb(4,5,6) contrast=6.39:1 PASS
new on-video warning                 text=rgb(255,171,64) ground=rgb(4,5,6) contrast=10.83:1 PASS
OLD -text fault (pre-fix, for comparison) text=rgb(174,19,33) ground=rgb(4,5,6) contrast=2.83:1 FAIL

=== theme: high-contrast ===
new on-video fault                   text=rgb(255,82,82) ground=rgb(0,0,0) contrast=6.58:1 PASS
new on-video warning                 text=rgb(255,171,64) ground=rgb(0,0,0) contrast=11.15:1 PASS
OLD -text fault (pre-fix, for comparison) text=rgb(255,82,82) ground=rgb(0,0,0) contrast=6.58:1 PASS
```

The old `-text` fault role only renders differently from the new on-video role in **light** theme
(both are the same `-500` signal value in dark and high-contrast, since spec 299 never redeclared
those) — and in light, it measures 2.83:1, failing 4.5:1, confirming the regression #2709 reports is
real and reproducible, not just computed from token values on paper. The new on-video role clears
4.5:1 in all three themes, matching the architecture guard (`A_triad_label_is_legible_on_video`) and
the plan's predicted figures (6.58/11.15 before scrim, ~6.38/10.81 after — measured here at
6.39/10.83, within rounding of alpha compositing).

**Latency**: N/A — a static CSS custom-property / Tailwind-class swap, same element count, no
runtime work added. Not on the event-to-overlay path (constitution §IV); the ≤ 50 ms composite +
render leg is unaffected.

**What was not covered**: this verification renders the compiled CSS directly in a synthetic harness
page, not through a live `CameraDetailPage` or `OverlayDraftForm` navigation against a booted Aspire
stack (memory was tight enough this session that a full-stack boot wasn't attempted for a change this
small; the harness uses the exact same compiled bundle `CameraViewer.tsx` ships, so the gap is
navigation/routing, not token resolution). Also not covered: #2736's frozen-frame-ground question — this
verification assumes `--color-bg-video` (solid black) is the real ground, which is the same assumption
the architecture guards make and #2736 exists to check.
