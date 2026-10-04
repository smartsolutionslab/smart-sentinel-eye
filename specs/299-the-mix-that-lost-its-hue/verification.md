# Phase 5 verification — spec 299 / issue #2695

Date: 2026-10-04. Branch `fix/2695-triad-contrast-and-oklch-drift`, worktree
`D:\Github\sse-2695`, commits `ede2ee8b` (US1) + `a832bb22` (US2).

## 1. `dotnet test tests/Architecture.Tests`

```
Passed!  - Failed: 0, Passed: 617, Skipped: 0, Total: 617
```
Independently re-run by the orchestrator, not merely relayed from the engineer. Also independently
confirmed `ede2ee8b` alone (US1 only, before US2 lands) is green on its own: 616/616.

## 2. Compiled-CSS diff against the `e892965b` baseline

Both apps built fresh (`management-web`, `kiosk-web`) and diffed against the pre-change baseline
captured on `e892965b` before any token/resolver change (saved outside the worktree so it survives
any checkout: `scratchpad/spec299-baseline/{management-web,kiosk-web}-baseline.css`). Filtered to
just the token-relevant fragments (`color-mix(...)`, the new primitives/roles, the `.text-accent-*`
rules) to cut through unrelated prettier/build-id noise:

- Every `color-mix(in oklch, ...)` → `color-mix(in oklab, ...)` — confirmed 20 real declarations
  changed (4 of them `--shadow-*`), 0 remaining in oklch.
- Three new primitives added: `--green-700`, `--amber-700`, `--red-700`.
- Three new semantic roles added: `--color-accent-{active,warning,fault}-text` (dark → the signal
  var; light → the new `-700` stops; high-contrast inherits, not redeclared).
- `.text-accent-{active,warning,fault}` now cite the `-text` var instead of the signal var directly.

No other difference found. Matches spec §7 step 2's permitted-difference list exactly.

## 3. Live, through the real UI (Chromium, Playwright) — both themes

Booted a fresh Aspire stack. Signed in as operator.

**Dark theme (characterisation — must be byte-identical to pre-change, plan R3):**

```
Badge (stream-health, active tone):
  pre-change: oklch(0.72656 0.20847 148.34) on oklch(0.25 0.04 148.34)
  post-change: oklch(0.72656 0.20847 148.34) on oklch(0.25 0.04 148.34)   <- identical

FaultNotice (fault tone, triggered via a 409 on rule-publish):
  pre-change: oklch(0.67864 0.20948 24.66) on oklch(0.25 0.04 24.66)
  post-change: oklch(0.67864 0.20948 24.66) on oklch(0.25 0.04 24.66)    <- identical
```

**Light theme (US2's actual fix, read as real rendered pixels via canvas `getImageData`, not the
raw `oklch()` string which this Chromium's `fillStyle` does not auto-convert):**

```
Badge text/background:      rgb(14, 113, 47) on rgb(226, 244, 228)   -> contrast 5.3464 : 1
FaultNotice text/background: rgb(174, 19, 33) on rgb(255, 231, 229)   -> contrast 6.1114 : 1
```

Both clear WCAG 1.4.3's 4.5:1, and both match plan.md §2.2's precomputed table almost exactly
(plan: active `rgb(14,113,47)` at 5.35:1 on its tint; fault `rgb(174,18,33)` at 6.13:1 on its
tint — the 1-unit RGB and 0.02 contrast differences are canvas/browser rounding, not a
discrepancy in the fix).

One test-infrastructure note for the record, not a product finding: the live script's first two
attempts had bugs in the test harness itself (a stale page-navigation reference after switching
sections, and a canvas-based oklch→rgb probe that silently failed to convert and read back
garbage numbers near 1.0 "contrast" — caught immediately because a contrast of ~1.0 for two
visibly different colours is not a plausible result, not because anything flagged it
automatically). Both fixed before trusting the final numbers above; the throwaway scripts were
deleted afterward and are not part of this branch's diff.

## 3a. A known limitation found at phase 6 — recorded, not fixed here

`frontend-reviewer` found that the new light-theme `-text` stops make one call site **worse**:
`apps/shared/src/ui/composites/CameraViewer.tsx`'s `ViewerOverlay` draws `text-accent-fault`/
`text-accent-warning` over `bg-scrim` on top of `bg-bg-video`, and `--color-bg-video` is pinned to
`--black` in every theme (never redeclared in `[data-theme='light']`). The new light stops
(`--red-700`, `--amber-700`) were chosen for contrast against *light* grounds — against this
video container's near-black background, contrast actually drops (fault 6.58:1 → 2.91:1, warning
11.15:1 → 3.14:1), failing WCAG 1.4.3 in exactly the theme this spec exists to fix.

**Latent only**: light theme cannot be reached at runtime today (spec §Non-goals; no theme
switcher exists), so this is not observable in production, and the live Playwright check above
only exercised Badge/FaultNotice — not `CameraViewer` — so it would not have caught this. It is a
real defect the moment light theme becomes reachable, in a component used by `CameraDetailPage`
and `OverlayEditorDialog`, not only the dark-pinned wall.

**Not fixed in this PR**: the correct fix needs a new design decision — an on-video text role that
stays the signal colour in every theme, distinct from the on-surface `-text` role this spec adds.
That is new scope beyond the two ADR amendments this PR implements. Recorded here and filed as
#2709 rather than expanding this PR's scope or leaving it undocumented.

## 4. What was NOT covered

- US1's hover/pressed/disabled/subtle hue-drift fix was verified by the Architecture.Tests facts
  (`A_mix_toward_black_or_white_keeps_its_base_hue`, now green) and the compiled-CSS diff, not by a
  separate live pixel-read of those specific states — they're interaction states (hover, pressed,
  high-contrast mode), not something a sign-in-and-read script naturally exercises without
  additional UI interaction. The Architecture.Tests facts are the authoritative check for these;
  the live read above focused on US2 (contrast), which only the browser can truly confirm (a C#
  resolver computing a contrast ratio from CSS source is not the same claim as "the browser
  renders it this way").
- Only Chromium was checked (Playwright's default project), not Firefox/Safari rendering of
  `oklch()`/`color-mix(in oklab, ...)`.

## Latency (constitution §IV)

**N/A** — a design-token/CSS change, not on the event-to-overlay path.

## Stack teardown

AppHost stopped; all 10 containers (`mosquitto`, `keycloak`, `mediamtx`, the Aspire tunnel proxy,
`rabbitmq`, `storage`, `postgres`, `camera-sim`, `pgadmin`, `fixture-video`) stopped explicitly —
`docker ps` confirmed empty afterward.
