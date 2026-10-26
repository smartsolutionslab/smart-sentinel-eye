# Spec 463: A colour too bright for the wall

**Issue:** #3108 (feature-level, Project #13) — spec 300 US4 / T027, slice 3 of spec 441's plan
**Branch:** `feat/3108-element-colour` · **Base:** `origin/develop` at `f848cb39`
**ADRs:** **ADR-0190** (luminance cap 0.35, domain invariant, migrate-and-record), ADR-0165 (arbitrary
`#RRGGBBAA`, the text legibility floor, §3 "authored colour is data, not a token"), ADR-0146 (the wall's
prohibitions; item 4's status triad), ADR-0148 (two-layer OKLCH tokens; primitives fixed across themes),
ADR-0123 + ADR-0117 (render leg, measured), ADR-0067 (MigrationRunner), ADR-0077 / ADR-0079 (Radix primitives,
RHF + Zod), ADR-0144 (lane). Constitution §II, §IV, §Testing.
**Lane:** autonomous (ADR-0144). **New ADR:** none — every decision below traces to ADR-0190's text or an
existing ADR (§"Decisions settled here" says which).
**Supersedes the uncommitted draft** `D:\Github\sse-3108\specs\456-the-label-the-console-could-not-colour\spec.md`
(456 is taken on `develop` by `456-a-codespace-that-builds`; its "open decision" is ADR-0190). Its settled
consequences 1-6 are carried over unchanged into US2.

---

## Premise, checked (on `f848cb39`)

| Claim | State | Evidence |
|---|---|---|
| Any well-formed `#RRGGBBAA` is a valid colour | **Yes** — `OverlayColor` is "kind-agnostic by design" | `src/OverlayDesigner/Domain/Overlay/OverlayColor.cs` |
| The default colour is white | **Yes** — `#FFFFFFD9`, luminance **1.0** | `OverlayColor.DefaultValue`, `DEFAULT_OVERLAY_COLOR` |
| The wall paints the default as a token, not as its hex | **Yes** — `var(--color-bg-label)` = white 85 % | `overlayLabelStyle.ts:64-69`, `tokens.css:109` |
| Relative luminance is computed in one place | **No** — a private copy in `TextLegibility.cs`, another in `textLegibility.ts` | both files |
| The console can author colour | **No** — no colour control under `apps/` | — |
| Another context persists element colour | **No** — LayoutComposition relays `OverlayRevisionPublishedV3`, SystemVariables keeps no colour | handlers grep |

**Consequence the owner should see first:** the default is over the cap, so ADR-0190 changes the colour of
**every default-coloured element on every wall** — not only operator-chosen bright ones. That is what decision
D3 below implements; it is not optional under ADR-0190 §1-§3.

---

## Decisions settled here (from existing ADRs — no new decision)

**D1. The luminance is WCAG 2.x relative luminance of the RGB channels; alpha is ignored.**
`L = 0.2126·R + 0.7152·G + 0.0722·B` on linearised sRGB (`c ≤ 0.04045 ? c/12.92 : ((c+0.055)/1.055)^2.4`) — the
formula `TextLegibility` already uses. Valid ⇔ `L ≤ 0.35` (inclusive). *Why:* ADR-0190 names "WCAG relative
luminance", which WCAG defines on an opaque colour; reading alpha in would be an extension the ADR does not make.
It is also the conservative reading (a translucent bright colour still adds brightness over dark video), and it
keeps the cap independent of alpha, so the cap and ADR-0165's alpha floor never trade against each other.
*Rejected:* luminance of the colour composited over black (`a·L`) — would admit `#FFFFFF59`, a white wash, which is
the hazard ADR-0190 removes.

**D2. One formula, one constant, per language, pinned across the boundary.**
- C#: `Domain/Overlay/RelativeLuminance.cs` (new, static) is the only implementation; `TextLegibility` calls it
  (behaviour-preserving extraction). The cap is `OverlayColor.MaximumRelativeLuminance = 0.35`.
- TS: `textLegibility.ts` exports `relativeLuminance(hex)` (its existing internal function, exported);
  `overlays.schema.ts` exports `OVERLAY_COLOR_MAX_LUMINANCE = 0.35`.
- The existing FR-020 pin test (`overlays.schema.test.ts`, reads backend source) gains the constant, so the two
  cannot drift. ADR-0190's "one place" is met per language with a pin between them — the same arrangement
  ADR-0165 already uses for the default and the pattern.

**D3. The nearest allowed colour keeps chromaticity and lowers luminance to the cap.**
For a colour with `L > 0.35`: scale the three **linear** channels by `k = 0.35 / L`, re-encode to sRGB, and
**floor** each channel to 8 bits. Alpha is kept. If the element is `Text` and the result fails ADR-0165's legibility
rule, alpha is raised to `TextLegibility.MinimumAlpha` (an opaque colour is always legible, so this terminates).
*Why this rule:* luminance is linear in linear-RGB, so a uniform linear scale is the exact, closed-form path that
keeps hue **and** saturation (chromaticity) unchanged and lands on the brightest compliant colour of that
chromaticity — "nearest" in the only sense that changes brightness alone. Flooring guarantees `L ≤ 0.35` after
quantisation, because luminance is monotone in each channel. *Rejected:* OKLCH/CIELAB lightness reduction (needs
gamut mapping — a second judgement call per colour); HSL lightness (not luminance-linear, needs a search, shifts
saturation).

Pinned vectors (computed with D1/D3; the domain test asserts them):

| Stored | L before | Kind | Migrated | L after |
|---|---|---|---|---|
| `#FFFFFFD9` (old default) | 1.0000 | any | `#9F9F9FD9` | 0.3467 |
| `#FFEB3BFF` | 0.8099 | Box | `#AFA125FF` | 0.3474 |
| `#FFFF0099` | 0.9278 | Text | `#A5A500BA` (alpha raised from 99) | 0.3491 |
| `#FFFFFF80` | 1.0000 | Box | `#9F9F9F80` | 0.3467 |
| `#00C853FF` | 0.4193 | Text | `#00B84BFF` | 0.3479 |
| `#D32F2FFF` | 0.1609 | any | unchanged | — |

Boundary: `#9F9F9F` (L 0.34670) is accepted; `#A0A0A0` (L 0.35153) is refused.

**D4. The new default is the old default's nearest allowed colour: `#9F9F9FD9`.**
Derived, not chosen: the migration turns every stored `#FFFFFFD9` into `#9F9F9FD9`, so any other default would make
a label created tomorrow differ from one migrated today with nothing to tell them apart. It is legible as text (worst
case 5.87:1 with the dark ink). `OverlayColor.DefaultValue`, `DEFAULT_OVERLAY_COLOR`, the ScenarioSimulator seed and
`OverlayEditPage.tsx`'s literal all move to it.

**D5. The default is painted as its hex, like every other colour.** `overlayLabelSurfaceStyle`'s default branch
(→ `var(--color-bg-label)`, white 85 %) is removed. Keeping it would paint white on the wall while the element
stores grey — the "operator sees one colour, the wall another" outcome ADR-0190 rejects for a kiosk clamp. The
`--color-bg-label` token itself is left in place (pinned by `tokens.test.ts` and `DesignTokenLayerTests`); retiring
it is a token-layer change outside this spec.

**D6. The migration rewrites every stored row, published and archived revisions included.** EF materialises colour
through `OverlayColor.From`; once the invariant exists, an unmigrated row would throw on read. ADR-0190 rejects
"reject on read". The record is a table, `overlay_colour_migrations` (revision, ordinal, previous colour, migrated
colour, migrated-at), written in the same transaction as the update.

**D7. Palette = ADR-0148 primitives under the cap, status hues excluded.** Swatches are `#RRGGBBFF` presets
resolved from `tokens.css` primitives; choosing one writes its hex (ADR-0165 §3 — no stored token reference).
Offered: `gray-500 … gray-950`, `cyan-600 … cyan-950`, `black` (13). Excluded and listed by name with a reason:
`gray-50…400`, `cyan-50…500`, `white` (over the cap); every `green-*`, `red-*`, `amber-*` (ADR-0146 item 4 — status
vocabulary; a swatch would suggest an authored zone is a status). Free hex can still produce any colour under the
cap, status values included — that is content, as the draft settled.

**[ASSUMPTION A1] A live wall repaints a migrated overlay at its next overlay fetch** (reload, reconnect, or the
overlay's next publish). The migration runs in MigrationRunner before any Api starts (ADR-0067); it does not
re-announce published revisions. Forcing a repaint would be a new integration message and is out of scope; it is
recorded so a reviewer can disagree. **[ASSUMPTION A2]** The record table is queryable by an administrator; an
API or console view of it is out of scope (ADR-0190 asks for the record, not a surface).

---

## User story 1 (P1): A bright colour cannot be stored, and the ones already stored are darkened

*As the fab, I want no write path to store a colour brighter than relative luminance 0.35, and every colour already
stored above it moved to the nearest allowed colour with a record of the change, so the unattended wall carries no
bright static region an operator authored (ADR-0190).*

Independently observable: before deploy, a draft holds `#FFFF0099`; after MigrationRunner, `GET /overlays/{id}`
returns `#A5A500BA` and `overlay_colour_migrations` holds one row naming both; `POST` with `#FFFFFF` answers 400.

### Acceptance scenarios

```gherkin
Scenario: A colour at the cap is accepted
  Given an operator holding sse.overlays.write
  When they create an overlay whose Box element color is "#9F9F9F"
  Then the response is 201 and GET returns color "#9F9F9FFF"

Scenario: The default is under the cap and paints as its hex
  When an element is created with no explicit colour choice in the console
  Then its color is "#9F9F9FD9"
  And the wall paints background "#9F9F9FD9" with the dark ink, not var(--color-bg-label)

Scenario: Stored bright colours are migrated and recorded
  Given stored elements coloured #FFFFFFD9 (Text), #FFFF0099 (Text), #FFEB3BFF (Box) and #D32F2FFF (Text)
    in draft, published and archived revisions
  When MigrationRunner runs
  Then they read #9F9F9FD9, #A5A500BA, #AFA125FF and #D32F2FFF
  And overlay_colour_migrations holds exactly three rows, each with previous and migrated colour
  And the migrated published revision still reads as published, at the same revision number

Scenario: The migration is idempotent
  Given MigrationRunner has already run
  When it runs again
  Then no element changes and no record row is added
```

**Bad request**

```gherkin
Scenario: The API refuses a bright colour on every write path
  When a POST /overlays or a draft PATCH carries an element color "#A0A0A0" (L 0.3515)
  Then the response is 400, the problem detail names "color", the cap 0.35 and the nearest allowed colour
  And nothing is stored

Scenario: Alpha does not lift the cap
  When an element color is "#FFFFFF10"
  Then the response is 400

Scenario: The console's schema refuses the same set before submit
  When the Zod schema validates an element coloured "#A0A0A0"
  Then it fails on path color with a message naming the 0.35 cap
```

**Conflict**

```gherkin
Scenario: A stale write with a valid colour is refused as stale
  Given a draft at version 4
  When a PATCH with If-Match 3 carries element color "#9F9F9F"
  Then the response is 409 OVERLAY_REVISION_STALE, as today

Scenario: The refusal order is unchanged — a bright colour fails with the body
  Given a draft at version 4
  When a PATCH with If-Match 3 carries element color "#FFFFFF"
  Then the response is 400 naming color, because elements are parsed and validated before the version check
    (EditDraftRefusalOrderCharacterisationTests pins this order; this spec does not move it)
```

**Auth**

```gherkin
Scenario: Auth is unchanged
  Given a caller without sse.overlays.write
  When they POST an overlay with a bright colour
  Then the response is 403, as today — scope is checked before the body
```

### Independent end-to-end test (US1)

1. On a scratch database (spec 317's pattern), migrate to the last migration before this spec, insert the four
   rows above by raw SQL, run the remaining migrations and the colour migrator; assert colours and record rows.
2. On the Aspire stack: `POST /overlays` with `#FFFFFF` → 400; with `#9F9F9F` → 201.
3. Kiosk e2e (`kiosk-shows-a-label-over-video.spec.ts`, default-coloured label): the tile's label node carries
   `background: #9F9F9FD9`; its render-leg record is collected (§"Latency").

## User story 2 (P2): An operator colours the element they are editing

*As an operator on the overlay page, I want to pick a colour for the selected element from a palette or as hex, so a
reading stands out without dropping to the API — and the console tells me before saving when a colour is too
bright or too transparent.*

Depends on US1 (the editor validates against the schema US1 tightens). Carries the draft's settled consequences:
a swatch is a preset, not a stored reference; swatch values are theme-independent primitives; the triad is not
offered; text legibility needs no runtime guard beyond ADR-0165's; strokes keep "no guarantee"; no kiosk change.

### Acceptance scenarios

```gherkin
Scenario: A swatch writes its hex and the preview follows
  Given an operator holding sse.overlays.write editing a draft [Text "T1"]
  When they choose the swatch "cyan-700"
  Then the hex field reads "#0F707DFF"
  And the preview surface is #0F707DFF with the ink overlayLabelSurfaceStyle computes
  And saving sends exactly one PATCH whose element 0 color is "#0F707DFF"

Scenario: Free hex is accepted and canonicalised
  When the operator types "#d32f2f" into the hex field
  Then the element's color is "#D32F2FFF"

Scenario: A shape's colour can be authored too
  Given the selected element is a Box
  When the operator chooses the swatch "gray-500"
  Then the Box's color is "#818A98FF" and its stroke previews in it

Scenario: The opacity control cannot go below a label's floor
  Given the selected label's colour is #D32F2F
  Then the opacity control's minimum is F9
  And for a Box the minimum is 00

Scenario: Only primitives under the cap, and no status hue, are offered
  Then the swatches are exactly gray-500..950, cyan-600..950 and black
  And no swatch value is a green-*, red-* or amber-* primitive
```

**Bad request**

```gherkin
Scenario: A too-bright hex is refused before submit
  When the operator types "#FFFFFF"
  Then no request is sent
  And the field says the colour is brighter than the wall allows (relative luminance above 0.35)

Scenario: An illegible text hex is refused and names the floor
  When the operator types "#D32F2F80" for a label
  Then no request is sent and the field says it needs alpha of at least F9

Scenario: A malformed hex is refused
  When the operator types "red", "#FF00" or "#GG0000"
  Then no request is sent and the field names the #RRGGBB or #RRGGBBAA form
```

**Conflict**

```gherkin
Scenario: A stale version loses a colour change, entire
  Given two operators editing the same draft at version 4
  When the second saves a colour change after the first saved
  Then the second sees the existing stale-conflict notice with Reload, never Retry
```

**Auth**

```gherkin
Scenario: A reader cannot change colour
  Given an operator without sse.overlays.write
  Then the swatches, hex field and opacity control are not editable (existing read-only page behaviour)
```

### Independent end-to-end test (US2)

Playwright (`e2e/overlay-colour.spec.ts`): open a draft, select the label, pick `cyan-700`, set opacity to its
floor, save; `GET /overlays/{id}` returns that `#RRGGBBAA`; type `#FFFFFF` and observe no request; publish and
assert the kiosk tile's label node carries the authored `background`.

---

## Latency (constitution §IV)

**Leg: overlay composite + render (≤ 50 ms), measured as `overlay_draw` per ADR-0123.** US1 changes what that leg
paints for every default-coloured label: the token branch is removed, so each default label now runs
`inkFor` (three luminance evaluations) per render, as every non-default colour already does. US2 changes no kiosk
code. No figure is claimed in advance.

**How it is measured:** spec 225's method — `gh run download <id> -p 'playwright-report-*'` for the PR's CI runs and
`readRenderLegRecords` (`e2e/support/render-leg.ts`) over the nine-tile fixture of
`kiosk-shows-a-label-over-video.spec.ts`. At least **two** PR runs (a single run after machine churn reads like a
regression), set beside the two most recent green `develop` push runs read the same way. p50 / p95 / max and the
observed frame interval are written to `verification.md` truncated to 0.01 ms, whatever they show. The §IV table is
not edited by this spec: the leg is already *Measured: yes*.

## Out of scope

Forcing live walls to repaint after the migration (A1); a console view of the migration record (A2); retiring
`--color-bg-label`; stroke contrast guarantees (ADR-0165 gate decision 2); per-theme authored colour; amending
ADR-0165's text, which still names `#FFFFFFD9` (the lane does not edit ADRs — flagged for the owner).
