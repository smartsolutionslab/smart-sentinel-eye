# Implementation Plan: `LayoutGrid` and its `Tile` are broken up by responsibility

**Branch**: `480-layout-grid-split` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/480-layout-grid-split/spec.md`

## Summary

Move the concerns of the 202-line `LayoutGrid`, the 243-line `Tile` and the module-level helpers into small hooks and components
**next to the file**, one extraction per commit, each green on its own. **First probe, then pin, then move**: break each concern once
in the unsplit file, see whether the 14-file family notices, and write characterisation tests, in **new files**, for what it does
not. **This is the wall's render path**, so a new test pins the **identity and measurement counts** (the draw measurement fires once
per distinct painted set; `getToken` and the held element reference keep their identity; the fault latch fires once per page) before
any stateful hook moves. Behaviour-preserving, so **characterisation** (ADR-0139/0144 phase 4a). Reuses the method of specs 476–479.

**Sizing, stated before building.** A decomposition: the file total will rise (476 +42 %, 477 +26 %, 478 +16 %, 479 +19 %). The value
is two functions that read as a list of what they compose. Moving module-level code first barely shrinks a component (478 −6 %, 479
0 %), so the order is: a first group of presentational/pure moves **plus the two lowest-risk hooks**, a **measure-and-ask** (T-D1),
then the rest of the stateful hooks.

## Technical Context

- **Stack**: TypeScript, React 19.3, RTK Query, `react-router-dom`, `react-oidc-context`, Vitest + Testing Library (jsdom),
  `apps/kiosk-web`. Callers: `CellPage` and `WallPage`.
- **Baseline (2026-11-02, `develop` after #3219):** `LayoutGrid.tsx` 682 lines (`LayoutGrid` 202, `Tile` 243, 23 hook calls, 1
  `eslint-disable`); covering family 14 files / ~4,600 lines.
- **Touches**: new files in `apps/kiosk-web/src/features/cell/layoutGrid/`, the grid, and new test files. No existing test file, no
  caller, no config.
- **Latency budget (§IV)**: the grid hosts the event → overlay state leg (hub handlers, the snapshot query, the template-miss
  refetch), the label hold (`useLabelDelay`, `label_delay`), the overlay-draw measurement and the composite + render leg. **Every
  extraction commit states the legs it touches and that they are unchanged** (FR-010).

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: the family unmodified, plus gap tests written first and seen green on the unsplit file. |
| §IV latency budget is sacred | No new timer, subscription, state, `useMemo` or render-time work; identities, keys and dependency arrays copied verbatim; counts pinned by a test before and after. |
| ADR-0036 — smallest change, no speculative generality | Each unit has one caller (stated); no shared abstraction; the fourth copy of the latest-token hook is **recorded as a follow-up**, not merged here (R6). |
| ADR-0123 / 0129 / 0145 | Unchanged: the same draw measurement, the same label hold, the same fab derivation and `namedFab` guards. |
| Lane may not weaken a gate | No test edited; the one suppression (#2118) kept and isolated, none added. |
| No new ADR | Decomposition inside existing conventions. |

No violation.

## Research (decisions that shaped the plan)

### R1 — What pins what (read from the family's names, then probed)

| Concern | Visibly pinned by | Probe expectation |
|---|---|---|
| A latest-token getter | indirectly (`CameraTile`, `…LiveUpdates`) | **probe**: stale token, identity across renders |
| B unavailable handling | `CellPage.test`, `WallPage.test` | probe: callback vs `navigate('/', replace)`, the effect's conditions |
| C page data (query, Published, items, credential, `wallFab`) | `CellPage*`, `…WallFab`, `…FabSkew`, `LayoutGridWallCredential` | probe: `skip` on an empty id, Published selection, the credential's input keys |
| D layout fault reporting | `CellPageOmittedInputs`, `…FabSkew` | probe: each fault once, the `published === undefined` guard, `namedFab` |
| E live updates wiring | `CellPageLiveUpdates` (468) | probe: `onArchived` matches the layout, `onReconnected` refetches, `enabled` |
| F page JSX | `CellPage.test`, `WallPage.test`, `LayoutGridWallCredential` | probe: headings, fallbacks, Back button rule, grid template, per-tile props |
| G tile overlay data | `LayoutGridLabelPairing`, `CellPageLabelSet` | probe: `skip`, unavailable derivation, placeholder/known flags, the verdict |
| H snapshot and live elements | `LayoutGridResolvedTextRace` (360), `CellPageResolvedText` (402) | probe: each `snapshotSkipped` term, template lookup and fallback, colour default, template-miss refetch |
| I held elements | `CellPageLabelAge` (365), `…LabelSet` | probe: every painted field in the key, the stable reference, `reportHeld`, the unavailable bypass |
| J draw measurement | indirect | **probe**: fires once per distinct painted set, not per render; `highlighted` re-fires |
| K tile/page pieces | `CellPage*` | probe: test ids, badge text, highlight class/attribute, alignment badge |

### R2 — Method: break, observe, then write what is missing

For each row, apply **one deliberate break** to the unmigrated grid (scratch, not committed), run the whole family, record which test
failed and why, or "none". Planned probes (a script of named breaks, each asserted to match exactly once, restored from git after;
**one vitest at a time**, batches of ten, direct background runs only; the family is ~14 files so each run is slower than 479's):

- **A** the ref assignment dropped (stale token); `useCallback` deps changed (a new identity per render).
- **B** `onUnavailable` ignored; `navigate('/')` without `replace`; the effect fires while loading, with an error, with no data, or
  while a Published revision exists.
- **C** `skip` dropped on an empty id; Published chosen as the first revision; `items` empty when published; the credential fed all
  tiles instead of populated ones; `wallFab` read from elsewhere.
- **D** each fault's latch dropped (fires every effect); the `published === undefined` guard dropped; `namedFab` replaced by a bare
  `=== undefined`; the affected-tile filter loosened; `layout` field dropped from the detail.
- **E** `onArchived` ignoring the layout match; `onReconnected` not refetching; `enabled` always true.
- **F** the loading message; the unavailable message; the Back button shown with `headerTitle`; the header title precedence; the grid
  template rows/cols swapped; `key` without the credential generation; each per-tile prop dropped or swapped (`unavailable`,
  `highlighted`, `playoutTargetMilliseconds`, `outOfAlignment`, `onLagMeasured` using the wrong key).
- **G** `skip` dropped; `overlayUnavailable` ignoring the fetched-but-unpublished case; `hasPlaceholder` using `startsWith`; the
  verdict effect firing while text is unknown.
- **H** each `snapshotSkipped` term dropped (`!hasPlaceholder`, `namedFab(fab)`, `overlayIdentifier`); the `?.texts` guard removed;
  the fallback to the raw template; `DEFAULT_OVERLAY_COLOR`; the shape mapping; `hasTemplateMiss` loosened; `skip` of the refetch.
- **I** each painted field dropped from `paintedElementsKey` (kind, colour, four geometry fields, font size, text); `useStableByKey`
  always returning the fresh value; `reportHeld`'s dependencies; the `overlayUnavailable` bypass dropped.
- **J** the effect keyed on the element array; `highlighted` dropped from the dependencies; the `undefined` guard dropped.
- **K** the testids; "Overlay unavailable" text; `data-highlighted`; the highlight class; the alignment badge condition; the
  `CameraTile` props (`onRefused`/`onLive`/`onRefusalLifted` keys).

A probe that bites is recorded in `verification.md` and needs no new test. One nobody notices gets a new test **before** the extraction
touches that concern; a break masked by another term is checked with a **double break**, or recorded as equivalent with the reason.

### R3 — The identity and count test (SC-006) and the other new tests go in **new files**

`LayoutGridIdentity.test.tsx` (name proposed), with `measureOverlayDraw`/`reportKioskLatency` mocked as the existing grid tests mock
them: (a) a page re-render with an **equal** element set calls `measureOverlayDraw` **once**, a changed set once more, a change of
`highlighted` once more; (b) a token renew (a new `auth.user.access_token`) does not re-fire the measurement (`getToken` identity);
(c) the layout fault latch: two re-renders and a refetch log each fault once, none while loading; (d) `useStableByKey`'s held
reference: a re-render with an equal set hands `CameraTile` the **same** `overlays` array reference. Written first, green on the
unsplit file, re-run unchanged after every moved hook. Further gap tests by concern as the probes decide. Each is proved able to fail by
a break, with the failure message read.

### R4 — The units (names are proposals; each ≤ 50 code lines where the logic allows)

New folder `apps/kiosk-web/src/features/cell/layoutGrid/` (mirroring `overlayEditor/`, `cameraViewer/`):

| Unit | Kind | Holds | Concern |
|---|---|---|---|
| `gridPieces.tsx` | components, pure | `EmptyCell`, `FullScreen`, `WallHeader`, `gridPlacementStyle` | F, K |
| `Tile.tsx` | component | `Tile`, `TileProps`, and (until later moves) the helpers it uses | K |
| `useLatestAccessToken(accessToken)` | hook | the ref, **the one suppression (`react-hooks/refs`)**, the stable `getToken` | A |
| `useLayoutData(layoutIdentifier, accessToken)` | hook | `useGetLayoutQuery` (with `skip`), `published`, `tiles`, `items`, `useWallCredential`, `wallFab`; returns them | C |
| `useLayoutFaultReporting({ layoutIdentifier, published, wallFab })` | hook | the once-per-page latch ref and the fault effect | D |
| `useTileOverlay({ tile, unavailable, onLabelVerdict })` | hook | `overlayIdentifier`, the overlay query, `publishedOverlay`, `overlayUnavailable`, `elements`, `textElements`, `hasPlaceholder`, the verdict effect; `isTextElement` | G |
| `useTileLiveElements({ overlayIdentifier, publishedOverlay, elements, textElements, hasPlaceholder, fab })` | hook | `snapshotSkipped`, the snapshot query, `textsByTemplate`, `liveElements`, `hasTemplateMiss`, `useTemplateMissRefetch` | H |
| `useHeldOverlay({ liveElements, frameAgeMilliseconds, overlayUnavailable, cameraIdentifier, getToken, highlighted })` | hook | `paintedElementsKey`, `useStableByKey`, `reportHeld`, `useLabelDelay`, `renderElements`, the draw-measurement effect | I, J |

`LayoutGrid` keeps the composition: `useNavigate`, `useAuth`, **B** (`handleUnavailable` and the leave-when-unavailable effect — two
seven-line blocks whose effect position matters and which gain nothing from a file), **E** (`useWallAlignment`,
`useOverlayHubHandlers`, `useLayoutLifecycle` with the `onArchived`/`onReconnected` wiring: already hooks), the page JSX and its
exports. `Tile` keeps the `frameAgeFor(tileKey)` read at render (spec 204), the composition of the three hooks, and its JSX.

### R5 — What must not change (checked per extraction)

- **Hook and effect order inside each function is preserved.** `LayoutGrid`: `useNavigate`, `useAuth`, [B callback], token (A), layout
  data (C incl. the credential), `useWallAlignment`, `useOverlayHubHandlers`, **fault reporting (D, after the hub hook)**,
  `useLayoutLifecycle`, [B effect]. `Tile`: the overlay query and verdict effect (G), the snapshot query and template-miss refetch
  (H), `useStableByKey`, `reportHeld`, `useLabelDelay`, the draw effect (I, J).
- **The identity tricks stay as written**: `getToken` stable over a ref; `useStableByKey` adjusting state during render (never a ref,
  never `useMemo`); `measureOverlayDraw` keyed on a derived scalar; `reportHeld` on `[tile.cameraIdentifier, getToken]`.
- **`items` is computed in render, unmemoised, exactly as today**, and passed on; no `useMemo` is introduced.
- **The fault latch is one set per mounted page** and the effect keeps its `published === undefined` early return.
- **Keys are unchanged**: the tile's `${item.key}:${credentialGeneration}`, and `WallPage`'s `key` on `LayoutGrid`.
- **The exports stay** (`LayoutGrid`, `LayoutGridProps`); `Tile` and `TileProps` stay private to the folder.
- **Each comment moves with its code once; any "above"/"below" pointer is rewritten** to name the new unit (grep after each move).
  The #1888/#1889, spec 141/204/301 and ADR-0145 evidence comments are kept whole.
- **Exactly one `eslint-disable` remains**, in `useLatestAccessToken`, rule and justification unchanged.
- **No work is added per render:** a hook returns the same values the function computed before; if a returned bag costs a measurable
  extra render (a probe with a render counter would show it), the unit is not extracted.

### R6 — The latest-token hook now exists four times (stated, not hidden)

`layouts/layoutEditor/useLatestAccessToken.ts` and `overlays/overlayDraftForm/useLatestAccessToken.ts` (`management-web`), the inline
copy in `CameraDetailPage`, and now this one in `kiosk-web`. Sharing means moving one to `@smart-sentinel-eye/shared/hooks` and
editing other features — outside FR-008 and outside a behaviour-preserving split. **Recorded as a follow-up**, as in specs 476 and 477.
This copy reads `auth.user?.access_token` (not `auth?.`, which the management copies need because their tests mount no provider).

### R7 — Not in this plan

`useLabelDelay` and the other already-separate hooks; `CellPage`, `WallPage`; the props; changing any identity trick or key; any
behaviour change; sharing `useLatestAccessToken`.

## Project Structure

```text
apps/kiosk-web/src/features/cell/
  LayoutGrid.tsx                                (shrinks: composition + page JSX; re-exports nothing new)
  layoutGrid/                                   (new folder beside the grid)
    gridPieces.tsx
    Tile.tsx
    useLatestAccessToken.ts
    useLayoutData.ts
    useLayoutFaultReporting.ts
    useTileOverlay.ts
    useTileLiveElements.ts
    useHeldOverlay.ts
  LayoutGridIdentity.test.tsx                   (new: SC-006)
  LayoutGrid<Concern>.test.tsx                  (new: other gap tests, as the probes decide)
specs/480-layout-grid-split/
```

## Delivery: one PR, commits that each stay green

1. The identity/count test and the other gap tests (new files), **green on the unsplit file**, with their breaks proved.
2. First group, least coupled: `gridPieces`, `Tile.tsx` (the whole component moved as is), `useLatestAccessToken`,
   `useLayoutFaultReporting` → **T-D1 measure-and-ask** (component lengths, file total, hook calls left).
3. Second group: `useLayoutData`, `useTileOverlay`, `useTileLiveElements`, `useHeldOverlay`.
4. Task ticks and the verification note (with the per-commit leg statement).

Every commit: the 14 covering files and the new gap files run (from `apps/kiosk-web`), `eslint --max-warnings 0`, `tsc --noEmit`,
`prettier --check`; `shared` and `management-web` re-run at the gate and at the end.

## Verification (what "unchanged" means, concretely)

Recorded on the unmigrated tree, repeated after:

- **Family:** sorted `file :: full name :: status` and sorted assertion lines of the 14 files — `diff` empty; `git diff
  origin/develop...HEAD` on them empty (FR-001).
- **Identity/count test:** the `LayoutGridIdentity` assertions, identical before and after (SC-006).
- **Probe matrix:** every R2 break, before (family) and after (family + gap files), in `verification.md`; double breaks and
  equivalents recorded.
- **Per extraction:** the key break applied to the **new unit** (not the old location) fails the matching test there; and the commit's
  leg statement (which §IV legs, unchanged).
- **Lengths (SC-002):** `LayoutGrid`, `Tile`, every unit (code and comment lines separately) and the **file total**.
- **Greps:** exactly one `eslint-disable`; distinctive comment phrases once; no stale "above"/"below" pointers.
- `kiosk-web`, `shared`, `management-web`, `management-cameras` suites; `eslint --max-warnings 0`, `tsc --noEmit`, `prettier
  --check`; `dotnet test tests/Architecture.Tests -c Release` (scans `.tsx`).

## Lessons carried from specs 470–479 (each cost time once)

Anchor the working directory in every command (print `pwd`); write scripts and notes with the file tool (a `node -e` string with
`/tmp` paths is wrong on Windows node); node scripts live in the scratchpad and run with the cwd at the folder they edit; `grep -E`
with `\|` matches a literal pipe; count hook calls with a regex that allows **nested** generics; grep for **other uses** before
removing an import; format every generated file each step; add only the step's files to a commit; a counterfactual must fail for the
right reason, so read the message; a **new untracked unit cannot be restored by `git checkout`**, so stage it before its
counterfactuals; **branches are cut from `origin/develop`**; **one vitest at a time**, long runs as a direct background command, a
break applied to the file **restored before anything else runs**; every spec rewrites `.specify/feature.json`, so each rebase
conflicts there — keep the branch's own value; a **foreign edit** in the working tree is never staged and a rebase uses
`--autostash`; a hook that now receives refs through an object needs them in its dependency array (`react-hooks/exhaustive-deps`),
which is stated in the commit, never suppressed; type a hook's bag with an explicit interface, not `ReturnType<typeof hook>`.

## Risks

| Risk | Mitigation |
|---|---|
| A moved hook returns a fresh bag and a 250-tile wall re-renders more | R5 last bullet; the identity test; a render counter probe; do not extract if it shows. |
| `useStableByKey` is turned into `useMemo`/a ref, or its key changes, and every render looks like a change | R5; probe I; the identity test (d). |
| The draw measurement re-fires per render (the #1888/#1889 failure) | R3 (a); probe J. |
| The fault latch resets per render or fires while loading | R5; R3 (c); probe D. |
| `useLayoutFaultReporting` lands before the hub hook and an effect order changes | R5's order; the call goes right after `useOverlayHubHandlers`. |
| The credential-generation `key` or the page `key` is touched | R5; probe F. |
| A unit needs more than ~8 inputs (`useHeldOverlay` is the widest, 6) | Stop and ask rather than push on. |
| The decomposition is longer and no easier to read | Stated up front; T-D1 stop point. |
