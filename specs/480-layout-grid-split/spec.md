# Spec 480 — `LayoutGrid` and its `Tile` are broken up by responsibility

**Feature Branch**: `480-layout-grid-split`
**Created**: 2026-11-02
**Status**: Draft
**Issue:** #3195 (feature-level; found by the frontend refactoring survey; the last of the set with #3190–#3194, #3198).
**ADRs:** ADR-0037 (phases), ADR-0084 (frontend 50-LOC-per-function row: recorded there as **not yet enforced**), ADR-0112 (highlight),
ADR-0123 (a render leg is the operator's wait), ADR-0129 (labels are aged, not frame-matched), ADR-0145 (a kiosk's fab is derived
from its wall), ADR-0165 (an overlay element is one value object), ADR-0139 / ADR-0144 (phase 4a).
**No new ADR.** A decomposition inside existing conventions; follows specs 476–479, whose probe-first method it reuses.
**Latency budget (constitution §IV): this component is ON the wall path, so the claim is made per leg.** `LayoutGrid` and `Tile`
host the **event → overlay state** leg (the resolved-text and revision hub handlers, the snapshot query, the template-miss refetch),
the **label hold** that ages a label to its picture (ADR-0129, the `useLabelDelay` call and its `label_delay` report), the
**overlay-draw** leg measurement (`measureOverlayDraw`, ADR-0123) and the **composite + render** leg (the grid itself, up to 250
tiles). The split is a move of code between files and hooks. It must add **no work per render, per tile or per frame**, change no
effect dependency array, keep every `useStableByKey`/ref identity trick, and add no state or re-render. Every extraction commit states
which legs it touches and that they are unchanged (FR-010).
**Phase 4a colour:** behaviour-preserving → **characterisation**. The covering tests are the existing `LayoutGrid*.test.tsx` (3) and
`CellPage*.test.tsx` (8) families, `CameraTile.test.tsx` and `WallPage.test.tsx` (the other renderer of `LayoutGrid`), ~5,000
lines; they must pass with every assertion **unmodified**. An assertion that has to be edited is evidence the behaviour moved:
block, do not adjust (CLAUDE.md).

## Problem

`apps/kiosk-web/src/features/cell/LayoutGrid.tsx` is **682 lines** and holds two large functions, against ADR-0084's 50 LOC per
function. Measured on `develop` (2026-11-02), not only grepped:

- **`LayoutGrid` is 202 lines** (75–276) and **`Tile` is 243 lines** (387–629). The issue said "about 310 and 245": the second is
  right, the first is not. The module also holds `useStableByKey`, `paintedElementsKey`, `isTextElement`, `gridPlacementStyle`,
  `EmptyCell`, `FullScreen`, `WallHeader` and the `TileProps` interface.
- **23 hook calls** in the file (the issue said 12): 13 in `LayoutGrid`, 8 in `Tile`, 1 in `useStableByKey`, nested-generic-aware.
- **One `eslint-disable`** (`react-hooks/refs`, line 96), judged in #2118 and **kept**, as the issue says: the latest-token ref.
- **The covering family is large and spread over three renderers' tests**: `LayoutGridLabelPairing` (417), `…ResolvedTextRace` (360),
  `…WallCredential` (229), `CameraTile` (755), `CellPage` (185) + `…FabSkew` (486), `…LabelAge` (365), `…LabelSet` (303),
  `…LiveUpdates` (468), `…OmittedInputs` (225), `…ResolvedText` (402), `…WallFab` (239), and `WallPage.test` (160): **14 files,
  ~4,600 lines**. (The issue named "the `LayoutGrid*`/`CameraTile` suites"; `CellPage*` is where most of the tile behaviour is
  actually asserted.)
- **Callers**: `CellPage` and `WallPage` render `<LayoutGrid>`; nothing imports `Tile`. The exports to keep are `LayoutGrid` and
  `LayoutGridProps`.
- Concerns interleaved in the two functions, each with comments recording a real failure:

  | # | Concern | Where (lines) |
  |---|---|---|
  | A | The **latest-token** getter: a ref assigned in render (the one `react-hooks/refs` suppression) behind a stable `useCallback` | 86–98 |
  | B | **Unavailable handling**: `handleUnavailable` (callback or `navigate('/', replace)`) and the effect that fires it when a loaded layout has no Published revision | 78–84, 198–202 |
  | C | **The page's data**: the layout query, the Published revision, the tiles, `buildGridItems`, the wall credential, `wallFab` (ADR-0145) | 99–131 |
  | D | **Layout fault reporting**: the one-per-page latch set and the effect that logs `tile-without-overlay-identifier` and `layout-without-fab` | 138–182 |
  | E | **Live updates**: `useWallAlignment`, `useOverlayHubHandlers`, `useLayoutLifecycle` and the `onArchived`/`onReconnected` wiring | 133–147, 184–196 |
  | F | **The page's JSX**: loading / unavailable fallbacks, header, the grid, per-tile props | 204–276 |
  | G | **A tile's overlay data**: the overlay query, the Published revision, `overlayUnavailable`, text/placeholder flags, the label verdict effect | 404–452 |
  | H | **A tile's snapshot and live elements**: the skippable snapshot query, `textsByTemplate`, `liveElements`, the template-miss refetch | 453–538 |
  | I | **A tile's held elements**: `paintedElementsKey`, `useStableByKey`, `reportHeld`, `useLabelDelay`, `renderElements` | 539–571 |
  | J | **A tile's overlay-draw measurement**: `renderElementsKey` and the `measureOverlayDraw` effect | 573–598 |
  | K | **A tile's JSX** and the small presentational pieces (`EmptyCell`, `FullScreen`, `WallHeader`, `gridPlacementStyle`) | 600–682 |

### What the issue does not settle (premise check, 2026-11-02)

1. **A decomposition, not a deduplication: the line total will not fall.** Expect the file total to **rise**, as in 476 (+42 %), 477
   (+26 %), 478 (+16 %), 479 (+19 %). The first moves of **module-level** code barely shrink the components (478: −6 %, 479: 0 %), so
   the plan orders the stateful hooks early and keeps a measure-and-ask between the groups.
2. **This is the wall's hot path in render, not in timers.** A 250-tile wall renders 250 `Tile`s on every page re-render and on each
   tile's own subscriptions. The family asserts behaviour, not render counts, so a hook that adds an object per render, a `useMemo`,
   or a changed identity can regress unnoticed. The plan must **probe the identity tricks** (`useStableByKey` returning the held
   reference; the `getToken` identity; `reportHeld`'s identity; the `measureOverlayDraw` effect not re-firing on an equal re-render)
   and may add a characterisation test that pins render/effect **counts** (the draw measurement fires once per distinct painted set,
   not once per render) before moving them.
3. **Four identity tricks are load-bearing and their comments are evidence.** (a) `getToken` is stable and holds the newest token
   behind a ref — keying on the token restarts the overlay-draw and decode measurements on every silent renew (#1888/#1889). (b)
   `useStableByKey` returns the held reference while its derived scalar key is unchanged; it adjusts state during render on purpose
   (a ref would be refused by `react-hooks/refs`). (c) `measureOverlayDraw` is keyed on a derived scalar, never the element array.
   (d) `reportHeld` depends on `[tile.cameraIdentifier, getToken]`. None may be turned into `useMemo`, re-keyed, or have its
   dependency changed.
4. **The fault latch is one set per mounted page and an effect, not render.** `reportedLayoutFaultsRef` fires `tile-without-overlay-
   identifier` and `layout-without-fab` at most once per page (spec 141 FR-007), inside an effect (StrictMode doubles a render-body
   log). Its early return on `published === undefined` is what keeps `layout-without-fab` silent while the layout loads, and the
   comment says a future split must keep that coupling.
5. **`LayoutGrid` is re-mounted by `WallPage` on every scene change via `key`**, and each `Tile` is keyed on
   `${item.key}:${credentialGeneration}`. Neither key moves.
6. **The one suppression stays**, isolated in the unit that holds the token ref, rule and justification unchanged; none is added.

## Scope

**In:** `LayoutGrid.tsx`: its concerns moved into modules **next to the file** in `apps/kiosk-web/src/features/cell/` (a folder
`layoutGrid/` mirroring the `overlayEditor/` and `cameraViewer/` folders), one extraction per commit, each green on its own (bisect).

**Out (explicitly):**
- Any behaviour change, including "obvious" fixes found on the way (two issues, per CLAUDE.md).
- `useLabelDelay`, `useWallAlignment`, `useWallCredential`, `useOverlayHubHandlers`, `useTemplateMissRefetch`, `wallBindings`,
  `wallGrid`, `CameraTile`, `TileAlignmentBadge`, `WallCredentialNotice` (already separate); `CellPage`, `WallPage` and the props
  of `LayoutGrid`.
- The `react-hooks/refs` suppression's judgement (#2118): kept, isolated.
- New abstractions beyond what the split needs; changing any `useStableByKey`, key or identity.

## User Scenarios & Testing

### User Story 1 (P1) — The wall behaves exactly as before, on the same budget

As the kiosk wall I render the same grid, the same labels at the same moments, the same placeholders and badges, report the same
faults once, and measure the same legs. As the maintainer I can prove it.

**Independent test:** the 14 covering test files pass with no line edited.

```gherkin
Scenario: the existing tests are the safety net
  Given the LayoutGrid*, CellPage*, CameraTile and WallPage tests
  When they are run after each extraction
  Then every test passes
  And git diff shows no line of those test files changed

Scenario: the overlay-draw measurement fires once per distinct painted set
  Given a tile whose bound overlay is rendered
  When the page re-renders with an equal element set, and then with a changed one
  Then measureOverlayDraw is called once for the first set, not again for the equal re-render, and once more for the changed set

Scenario: a layout fault is logged once per page
  Given a layout whose tile has a blank overlay identifier and which carries no fab
  When the page renders, re-renders and the layout refetches
  Then each of the two faults is logged exactly once, and neither while the layout is still loading

Scenario: a silent token renew does not restart the measurements
  Given a rendered wall
  When the access token is renewed
  Then getToken keeps its identity and no tile effect is rebuilt
```

### User Story 2 (P2) — The components are short enough to read

```gherkin
Scenario: the components are short
  When the split is done
  Then LayoutGrid and Tile are well under their former 202 and 243 lines
  And each extracted unit is under 50 code lines where the logic allows (the issue's own bar)
```

### Edge cases

- **Hook order inside each function is preserved** (a moved cluster is called at the position of the code it replaces), and no hook
  becomes conditional.
- **`wallFab` is `undefined` while the layout loads** and `namedFab` classifies that, a blank and a whitespace fab as "no fab"; the
  snapshot query is skipped on an unnamed fab (cross-fab defect, spec 141 site 2). Unchanged.
- **`handleUnavailable`'s dependency array** (`[navigate, onUnavailable]`) feeds an effect's array; copied verbatim.
- **`Tile` is called with `key` containing the credential generation**; the `Tile` function identity does not matter, but moving it
  to its own file must not change how React sees the element type (same component, same key).
- **Comments are evidence, not decoration.** Each records a real failure and moves with its code, once.
- **A first extraction that does not help is a stop sign** (#3190, #3198, #3193, #3192, #3191, #3194): measure, then ask.

## Requirements

- **FR-001** Every test in the 14 covering files passes after each commit; none of those files is modified.
- **FR-002** `LayoutGrid` and `Tile` compose extracted modules for the concerns A–K; their own bodies are the composition and the JSX
  that remains.
- **FR-003** No change to what is queried, logged, reported or measured: same RTK Query arguments and `skip`s, same log events and
  fields, same `reportKioskLatency` / `measureOverlayDraw` calls, same keys, same effect dependency arrays, same ref scopes.
- **FR-004** The exports and props of `LayoutGrid.tsx` (`LayoutGrid`, `LayoutGridProps`) are unchanged; no caller changes.
- **FR-005** Exactly **one** `eslint-disable` remains across `LayoutGrid.tsx` and `layoutGrid/`, rule (`react-hooks/refs`) and
  justification unchanged, in the unit that holds the token ref; none is added.
- **FR-006** Comments move with their code; none is dropped, none duplicated; any "above"/"below" pointer is rewritten to name the
  new unit.
- **FR-007** Each commit builds and passes the family on its own; one concern per commit.
- **FR-008** No change outside `apps/kiosk-web/src/features/cell/` (new files and the grid), `specs/480-…` and `.specify/feature.json`.
- **FR-009** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` are clean for `kiosk-web` and `shared`, and
  `Architecture.Tests` pass (they scan `.tsx`).
- **FR-010** **Latency (§IV).** Each extraction commit states which legs it touches (event → overlay state, label hold, overlay draw,
  composite + render, or none) and that they are unchanged; no new timer, subscription, state, `useMemo` or render-time computation
  is introduced; the identity tricks and the draw-measurement count are pinned by a test (see plan) and equal before and after.

## Success Criteria

- **SC-001** The 14 covering files: same test count and names before and after, all passing, `git diff` on them empty.
- **SC-002** `LayoutGrid`'s and `Tile`'s own lengths and each new unit's length, reported before/after **with the file total**, code
  lines and comment lines separately.
- **SC-003** A search of the grid and its new units for `eslint-disable` finds exactly the **one** original, rule and justification
  unchanged.
- **SC-004** The `kiosk-web` suite (and `shared`, `management-web`, `management-cameras` as the other consumers of what it imports)
  passes, with counts and durations before and after.
- **SC-005** For every concern (A–K), a deliberate break is noticed by a named test; where the family notices nothing, a new
  characterisation test is written first, green on the unsplit file, and proved able to fail (the message read). Where a behaviour is
  defended jointly or is equivalent, that is recorded with evidence.
- **SC-006** The draw-measurement and identity facts of Scenario 1 are pinned by a test and equal before and after.

## Assumptions

- The family is strong on label pairing, the resolved-text race, fab skew, label age and live updates (they are the subject of the
  largest files); the probes decide where it is not, and the likeliest gaps are the identity tricks, the fault latch, the
  unavailable handling and the presentational pieces.
- Extracting custom hooks preserves behaviour as long as they are called unconditionally, in the same order, with the same
  dependency arrays.

## Dependencies

Independent of #3220 (spec 479) and the earlier specs; follows their method. This is the last issue of the survey.
