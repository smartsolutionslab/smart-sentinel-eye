# Spec 319 — The gray that forgot the video

**Issue:** [#2734](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2734)
— *CameraViewer's neutral on-video label text also fails contrast in light theme*.
Label `agent:ready`; Project #13, status **In Progress** (verified 2026-10-08,
`gh issue view 2734 --json projectItems`).
**Branch:** `fix/2734-on-video-neutral-contrast-token` (worktree `D:\Github\sse-2734`, cut from
`origin/develop` @ `ba37ca5a`)
**Created:** 2026-10-08
**Lane:** autonomous (ADR-0144).

**Spec number.** On 2026-10-08 `origin/develop` tops out at **315**. Unpushed worktrees already hold
316 (`feat/316-operator-mfe-shell-and-first-remote`), **317 three times** (`sse-2077`, `sse-2170`,
`sse-2325` — a collision those branches must settle among themselves) and 318 (`sse-2628`). No
branch or worktree holds `specs/319-*`. **319** is free. Re-check after every parked-PR merge before
opening the PR (memory: *spec number: origin/develop isn't enough*).

**The design decision was taken by a human before this spec**, recorded on the issue (comment of
2026-10-08):

> Decision (user, 2026-10-08): add a new neutral on-video design token distinct from the normal UI
> neutral token, rather than documenting this as an accepted exception.

This spec decides nothing beyond that: it names the token, picks its value, places it, and wires the
two call sites. The value choice is the one judgement left open by the decision; §5 records it and
why.

**ADRs this spec is bound by:**

- **ADR-0148** decision 1 (a theme remaps semantic roles; primitives fixed; components cite the
  semantic layer only) and its Naming rule (`--<category>-<role>-<variant>`). This is the rule that
  governs a **neutral** role; ADR-0146 item 4 governs only the triad.
- **ADR-0078** — tokens are CSS custom properties consumed through the shared `tailwindTheme.ts`.
- **ADR-0146** item 4 and its 2026-10-07 note (spec 308) — the `-on-video` variant's meaning ("text
  on the black `--color-bg-video` ground") is borrowed from there. Item 4 does **not** bind this
  role (it is not a triad colour), so no ADR sentence is contradicted and no note is needed (§6).
- **ADR-0165** §3 — precedent for a role justified by "an overlay sits on video, not on a theme
  surface".
- **ADR-0036** (smallest change — one role, two call sites), **ADR-0139/ADR-0144** (red first;
  colour in §7), **ADR-0037**, **ADR-0109** (`[P]`), **ADR-0053** (sentence-style test names).

**Precedent followed:** spec 308 (#2709) in full — the `-on-video` variant name, its placement next
to the other on-video roles in `tokens.css`, a `textColor`-only Tailwind key, the two architecture
facts in `StatusTintTests` (structure + legibility via the class's own `TryContrastRatio`), the
`tailwindTheme.test.ts` cast-and-assert `it`, and `CameraViewerOverlayTone.test.tsx`. **One clause
of 308 is deliberately not copied** — "declared in no theme block" — and §5 says why: copying it
would regress the high-contrast theme, breaking 308's *other* invariant ("dark and high-contrast
render exactly as before").

---

## 1. The issue's premise, re-checked on this tree (`ba37ca5a`)

Computed with the CSS Color 4 OKLab→sRGB matrices and 8-bit rounding of
`tests/Architecture.Tests/OklchColorResolver.cs` (the same path `StatusTintTests.TryContrastRatio`
takes, so the guard will print these figures), WCAG 1.4.3 relative luminance. The light scrim is
`--gray-950` (`rgb(11,13,16)`) at 40 % over black = `rgb(4,5,6)` — the value spec 308's phase 5 read
back live in Chromium.

| Issue says | Tree says | Consequence |
|---|---|---|
| `ViewerOverlay`'s neutral label and its hint line use `text-fg-muted`. | **Confirmed** — `CameraViewer.tsx:553` (neutral arm of `tone`), `:566` (hint `<span>`). No other element paints on `bg-bg-video` (`:429`) / the overlay `bg-scrim` (`:563`); kiosk has no on-video `text-fg-muted`. | The two call sites to change. |
| Light `--color-fg-muted` is `--gray-600`: 4.15 : 1 bare, 4.02 : 1 with the scrim. | **Reproduced**: `--gray-600` = `rgb(103,111,124)` → **4.14 : 1** on `--color-bg-video`, **4.02 : 1** on the light scrim. (Spec 308 §1 printed 4.15; the 0.01 is rounding on a different path.) | Below 4.5 : 1 — the defect. |
| (not named) | Dark `--color-fg-muted` is `--gray-500` = `rgb(129,138,152)`: **6.02 / 5.85 : 1**. High-contrast is `--gray-200` = `rgb(211,216,224)`: **14.67 : 1** (its scrim is black-over-black = black). | Dark and high-contrast already pass; only light fails. |
| `--color-bg-video` is black in every theme. | **Confirmed** — `tokens.css:102` in `:root` only; pinned by `DesignTokenLayerTests.Content_roles_are_pinned_across_themes`. #2736 confirmed live (Chromium) that a torn-down stream blanks to black rather than freezing a frame. | The ground is theme-invariant; the guard's ground is real. |
| Light theme is unreachable at runtime. | **Confirmed** — both apps pin `data-theme="dark"`; no switcher. | Latent fix; phase 5 sets the attribute by hand, as spec 308 did. |

## 2. User stories

### US1 (P1): A camera's neutral status and hint read over its video in every theme

As an operator viewing a camera in the console, when the stream is connecting (or in any state whose
label is neutral), and whenever the overlay shows a hint line, I can read that text over the video at
≥ 4.5 : 1 in every theme, because on-video neutral text uses a role chosen for the black video ground,
not the one chosen for the theme's own surfaces.

**Acceptance (Gherkin):**

```gherkin
Scenario: Neutral label on video is legible in light theme (happy)
  Given data-theme="light" on <html>
  And a CameraViewer whose stream is "connecting"
  When the viewer overlay renders its status label
  Then the label carries class "text-fg-muted-on-video" and not "text-fg-muted"
  And its computed colour is --gray-500 (rgb(129,138,152))
  And its contrast against the composited video ground is >= 4.5:1

Scenario: Hint line on video is legible in light theme (happy)
  Given data-theme="light" on <html>
  And a CameraViewer whose stream health is "Offline" with an error message
  When the viewer overlay renders the hint line
  Then the hint carries class "text-fg-muted-on-video" and not "text-fg-muted"
  And its contrast against the composited video ground is >= 4.5:1

Scenario: Dark and high-contrast render exactly as before (conflict — no regression)
  Given data-theme="dark" (or "high-contrast")
  When the neutral label and the hint render
  Then their computed colour equals --color-fg-muted's value in that theme on ba37ca5a
    (dark --gray-500, high-contrast --gray-200)

Scenario: The surface neutral role is untouched (conflict — boundary)
  Given data-theme="light"
  When a Badge with tone "neutral" renders on bg-bg-raised
  Then it still carries text-fg-muted and resolves to --gray-600

Scenario: Forgetting the light override fails the build (bad request — guard)
  Given tokens.css with the light block's --color-fg-muted-on-video declaration removed
  Then the legibility guard fails, naming light at 4.14:1

Scenario: A theme other than light cannot move the role (bad request — guard)
  Given a [data-theme='high-contrast'] block that declares --color-fg-muted-on-video
  Then the structure guard fails the build

Scenario: Triad tones keep their on-video roles (bad request — not a neutral state)
  Given a CameraViewer whose status is "offline" or "reconnecting"
  Then the label carries text-accent-fault-on-video / text-accent-warning-on-video
  And not text-fg-muted-on-video
```

**Auth:** N/A — a presentation token; no endpoint, no scope, no data reached.

**Independent test:** §8.

## 3. Scope

### 3.1 In

- One semantic colour role, `--color-fg-muted-on-video`, in `apps/shared/src/ui/tokens/tokens.css`:
  declared in `:root` as `var(--color-fg-muted)`, redeclared in `[data-theme='light']` only, as
  `var(--gray-500)`.
- One `extend.textColor.fg` key in `tailwindTheme.ts` → `text-fg-muted-on-video`.
- `ViewerOverlay`'s neutral tone arm and its hint line switch to it (`CameraViewer.tsx:553,566`).
- Guards: two architecture facts, one new Vitest `it`, one declared assertion edit + one new `it` in
  `CameraViewerOverlayTone.test.tsx` (§7).

### 3.2 Out, and why

- **Any other `text-fg-muted` call site.** All others paint on a theme surface, where `--gray-600`
  is the right light value (`The_neutral_label_is_legible_on_its_fill` guards it).
- **`--color-fg-primary-on-video` / `-disabled-on-video`.** No on-video call site paints them
  (ADR-0036: add a role with its first consumer).
- **The scrim in the guard.** The resolver cannot composite a `transparent` mix (it reports "no
  single rendered colour"); spec 308 guarded the bare ground and recorded the scrim figures (its
  A2). Same here — §1 records them; margin with the scrim is 1.35 : 1.
- **A theme switcher.** Light stays unreachable at runtime; latent fix.
- **An ADR-0146 note.** Item 4 is about the triad; a neutral role is ADR-0148 decision 1 latitude
  (§6).

## 4. Functional requirements

- **FR-001** `tokens.css` `:root` declares `--color-fg-muted-on-video: var(--color-fg-muted)` —
  citing the neutral **role**, not a primitive, so dark and high-contrast keep exactly the neutral
  their theme already chose (and follow it if a theme ever re-picks it).
- **FR-002** `[data-theme='light']` declares `--color-fg-muted-on-video: var(--gray-500)`. No other
  theme block declares it.
- **FR-003** In every theme `--color-fg-muted-on-video` clears 4.5 : 1 on `--color-bg-video`:
  dark 6.02, light 6.02, high-contrast 14.67 (with the scrim: 5.85 / 5.85 / 14.67).
- **FR-004** `tailwindTheme.extend.textColor` gains `fg: { 'muted-on-video':
  'var(--color-fg-muted-on-video)' }`. `extend.textColor.accent` and `extend.colors` are unchanged —
  no `bg-`/`border-` use (ADR-0036), as with spec 308's keys.
- **FR-005** `ViewerOverlay` uses `text-fg-muted-on-video` in the neutral arm of `tone` and on the
  hint `<span>`; the tone *selection* is unchanged.
- **FR-006** No other call site changes; `text-fg-muted` keeps meaning "neutral on a theme surface".
- **FR-007** One `tokens.css` comment above the `:root` declaration states why it exists (the ground
  is `--color-bg-video`, pinned black; light's surface neutral is chosen for light surfaces and
  fails on it) and cites #2734 — one comment, not one per line. The light-block declaration carries
  no comment of its own beyond a pointer if the engineer judges one needed.

## 5. The value, and the one clause of spec 308 not copied

**Value: `--gray-500` in light.** The candidates are the gray stops, because ADR-0148 makes the
primitive scale fixed and every neutral cites it:

| Stop | sRGB | on `--color-bg-video` | with light scrim |
|---|---|---|---|
| `--gray-600` (light's surface muted today) | `rgb(103,111,124)` | 4.14 | 4.02 — **fails** |
| **`--gray-500`** | `rgb(129,138,152)` | **6.02** | **5.85** |
| `--gray-400` | `rgb(157,165,177)` | 8.45 | 8.21 |
| `--gray-300` | `rgb(187,193,204)` | 11.61 | 11.28 |

`--gray-500` is the first stop that clears 4.5 : 1, with 1.35 : 1 margin after the scrim, and it is
**exactly the dark theme's muted**. Since the ground under this text is the same black in every theme,
the light overlay then renders identically to the dark one — the one already reviewed and the only
one any operator has seen. A brighter stop would make light's overlay *more* emphatic than dark's
over the same pixels, with no reason to. A custom value would add a primitive for one call site,
against ADR-0148's fixed scale.

**Why light redeclares it, unlike spec 308's roles.** Spec 308's roles are "declared once in `:root`,
never redeclared by a theme". That clause rests on ADR-0146 item 4 (the triad signal is pinned) and it
was free: the signal equals the high-contrast `-text` role, so pinning changed nothing in high-contrast.
For the neutral it is not free. High-contrast deliberately raises its muted to `--gray-200` (14.67 : 1).
A role pinned in `:root` to `--gray-500` would drop the high-contrast overlay to 6.02 : 1 — a visible
regression in the theme whose whole purpose is contrast, and a breach of spec 308's own "dark and
high-contrast render exactly as before" scenario.

`:root: var(--color-fg-muted)` plus a light-only override keeps both invariants: dark and
high-contrast are unchanged by construction, and only the theme that was wrong moves. The rule that
replaces "never redeclared" is "redeclared by light only", and the structure guard enforces it
(plan §6.1 fact 1) the same way 308's guard enforces its rule. ADR-0148 decision 1 permits a theme to
remap a semantic role, and `A_theme_redeclares_only_semantic_names_root_already_has` stays satisfied
because `:root` declares the name.

## 6. No new ADR, no ADR note

- ADR-0148 decision 1 already permits a theme to remap a semantic role; a new role that light remaps
  is that latitude used, not extended. The name follows its Naming rule
  (`--color` · `fg-muted` · `on-video`).
- ADR-0146 item 4 — including the sentence spec 308 had to narrow — speaks only of the triad. Nothing
  in it mentions or constrains a neutral role, so nothing needs narrowing and no note is written.
- The human decision is recorded on #2734; this spec records the value. Neither is an architectural
  decision ADR-0144 reserves.

## 7. Phase-4a colour

**Red.** Behaviour-changing: a new token, a new Tailwind mapping, a different class emitted by
`CameraViewer` on two elements, and a different rendered colour in light. That light is unreachable
today does not make it a refactor — the emitted class moves in every theme. Ambiguity would resolve to
red regardless (CLAUDE.md, phase 4a).

Red facts, observed failing on `ba37ca5a` before any production edit, output quoted verbatim in the PR
(ADR-0139):

| Fact | File | Why red on develop |
|---|---|---|
| `The_neutral_on_video_role_is_the_muted_role_except_in_light` (new) | `tests/Architecture.Tests/StatusTintTests.cs` | Not declared. |
| `The_neutral_label_is_legible_on_video` (new) | same | Not declared → unresolved, in all three themes. |
| `maps the neutral on-video role under extend.textColor.fg to its var` (new `it`) | `apps/shared/src/ui/tokens/tailwindTheme.test.ts` | No `textColor.fg`. |
| connecting case, rewritten (declared edit, below) | `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx` | Overlay emits `text-fg-muted`. |
| hint-line case (new `it`) | same | Hint emits `text-fg-muted`. |

**One declared assertion edit** (the behaviour moves, so the assertion must): spec 308's
`Keeps text-fg-muted, with no -on-video class, while connecting` asserts the old class. It becomes
`Carries text-fg-muted-on-video, not text-fg-muted, while connecting` — asserting
`text-fg-muted-on-video` present, `text-fg-muted` absent, and both triad `-on-video` classes still
absent (the last two assertions stay byte-identical). Its describe-level comment ("the neutral tone is
untouched … T030 follow-up") is updated to cite #2734 instead. No other existing assertion changes.

**Characterisation — green on `ba37ca5a`, passing unmodified after:** every other `StatusTintTests`
fact (notably `Each_on_video_text_role_is_its_signal_in_every_theme`, `A_triad_label_is_legible_on_video`,
`The_neutral_label_is_legible_on_its_fill`); every `DesignTokenLayerTests` fact (notably
`A_theme_redeclares_only_semantic_names_root_already_has`, `Semantic_colours_cite_the_scale_never_a_literal`,
`Every_name_is_a_token_a_primitive_or_a_named_bridge`, `Content_roles_are_pinned_across_themes`,
`Every_token_the_theme_cites_is_declared_and_semantic`); `InteractionStateTests`; both existing
`tailwindTheme.test.ts` `it`s (the `textColor.accent` `toEqual` is untouched — the new key is under
`fg`); the three triad cases in `CameraViewerOverlayTone.test.tsx`; every `CameraViewer*.test.tsx`;
`Badge.test.tsx`.

## 8. Independent end-to-end test procedure (phase 5)

Spec 308's verification method, reused so the two figures are comparable:

1. `dotnet test tests/Architecture.Tests` and `npm test` in `apps/shared` — quote counts.
2. Build `management-web` on `ba37ca5a` and on the branch; diff the compiled CSS. Permitted differences
   only: `--color-fg-muted-on-video` in the `:root` block and in the `[data-theme=light]` block, and one
   new rule `.text-fg-muted-on-video { color: var(--color-fg-muted-on-video) }`. `.text-fg-muted` stays
   (dozens of call sites), and every other `text-fg-*` / `text-accent` rule is byte-identical — this
   is what proves A1.
3. In headless Chromium (Playwright), load the compiled CSS and render the new class and the old
   `text-fg-muted` over a `bg-bg-video` element with `bg-scrim` on top, for each of `dark`, `light`,
   `high-contrast`. Read `getComputedStyle().color`, normalise via canvas round-trip, compute contrast
   in-process. Expected: new role ≥ 4.5 : 1 in all three (≈ 6.02/5.85, 5.85, 14.67); dark/hc equal the
   old `text-fg-muted` exactly; light old ≈ 4.02 FAIL, new ≈ 5.85 PASS.
4. If the stack is free (memory: *one machine, one Aspire stack*), repeat step 3's read once on a live
   `CameraDetailPage` in `connecting` with `data-theme` set by hand; otherwise record the gap, as 308's
   verification did.
5. Write every figure into `verification.md` as observed.

**Latency:** N/A — a static class swap on two existing elements, no runtime work; not on the
event-to-overlay path, and the ≤ 50 ms composite + render leg is unaffected (constitution §IV).

## 9. Risks and assumptions

- **A1** Adding `extend.textColor.fg` does not shadow `text-fg-primary` / `-muted` / `-disabled` /
  `-on-*`. Tailwind's `text-*` reads `--text-color-*` then falls back to `--color-*`; the same
  fallback already serves `text-accent` (12 call sites) although `textColor.accent` has no `DEFAULT`.
  §8 step 2 proves it on the compiled CSS.
- **A2** The guard measures against the bare `--color-bg-video`, not the scrim composite (same as spec
  308's A2); §1 records the scrim figures, minimum 5.85 : 1.
- **A3** #2736's black-ground confirmation is Chromium-only; Firefox is unverified. Shared with spec
  308, not widened by this spec.
- **R1** Name reads as "muted on video", parallel to `accent-fault-on-video` and the `fg-on-*`
  preposition (ink *on* that ground). Consistent, not ambiguous.
