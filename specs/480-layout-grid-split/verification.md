# Verification 480 — `LayoutGrid` and its `Tile` are broken up by responsibility

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **This is the kiosk wall's render path (§IV)**, so the
evidence includes the per-commit leg statements and the identity/count facts (SC-006). Not observed on a running wall (see "Not
observed"); the evidence is the 14 covering files and six new files under jsdom.

## Size, as measured (it is a decomposition: the total rises)

|                                              | Before                          | After                 |
| -------------------------------------------- | ------------------------------- | --------------------- |
| `LayoutGrid.tsx`                             | 682                             | **185**               |
| `LayoutGrid` (the page component)            | 202                             | **123** (−39 %)       |
| `Tile`                                       | 243                             | **76** (−69 %)        |
| hook calls in `LayoutGrid` / `Tile`          | 13 / 8 (+1 in `useStableByKey`) | **10 / 3**            |
| **File total (grid + `Tile.tsx` + 7 units)** | 682                             | **805** (+123, +18 %) |

Units (`layoutGrid/`, total / code lines): `Tile` 145 / 96, `useHeldOverlay` 130 / 55, `useTileLiveElements` 114 / 64,
`useTileOverlay` 65 / 30, `gridPieces` 55 / 48, `useLayoutFaultReporting` 52 / 29, `useLayoutData` 41 / 17,
`useLatestAccessToken` 18 / 7. Over the issue's 50-code-line bar: `Tile.tsx` (96, the component plus its props interface and JSX),
`useTileLiveElements` (64) and `useHeldOverlay` (55): each is one block of the old `Tile` moved whole. ADR-0084's limit is not
enforced, so no failing gate is being fixed.

**Not extracted:** the unavailable handling (`handleUnavailable` and the leave-when-unavailable effect: two short blocks whose
effect position matters) and the three live-update hook calls with their `onArchived`/`onReconnected` wiring (already hooks) stay in
`LayoutGrid`, as the plan said.

**The measure-and-ask (T009)** after the first group showed `LayoutGrid` 202 → 152 and `Tile` still 243 (it had only changed files),
file total +4 %. The maintainer was asked and chose to continue; the four hooks took `Tile` to 76.

## Characterisation (FR-001)

- 14 covering files: **90 test names with status identical** (the only `diff` line is a missing trailing newline in the "before"
  file), **247 assertion lines identical**; `git diff origin/develop...HEAD` on them is empty. Only these test files were added:
  `LayoutGrid{Identity,Unavailable,Live,Page,Snapshot,HeldReport}.test.tsx` (61 tests) and the plain-data
  `layoutGridGapFixtures.ts`.
- `kiosk-web` **390 passed** (38 files, 34 s; 329 before + 61); `shared` **1,387**; `eslint --max-warnings 0`, `tsc --noEmit`,
  `prettier --check` clean; `Architecture.Tests` **3254 / 3254**.
- `eslint-disable`: exactly **one**, `react-hooks/refs` in `useLatestAccessToken`, rule and justification unchanged (SC-003).

## §IV: the legs, per commit, and the identity facts (FR-010, SC-006)

`LayoutGridIdentity.test.tsx` (19 tests) was written first, green on the unsplit grid, and **run unchanged after every moved hook**:

| Fact                                                                                   | Pinned                                                                                                                                                                                             |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `measureOverlayDraw` on mount                                                          | once                                                                                                                                                                                               |
| a re-render with an equal element set                                                  | 0 further calls                                                                                                                                                                                    |
| a change to any one painted field (kind, colour, x, y, width, height, font size, text) | once more, each of the eight                                                                                                                                                                       |
| a change of `highlighted`                                                              | once more                                                                                                                                                                                          |
| no Published revision, or the overlay becomes unavailable                              | never                                                                                                                                                                                              |
| a token renew                                                                          | 0 extra measurements; `getToken` keeps its identity and yields the new token                                                                                                                       |
| layout faults                                                                          | each logged once per mounted page across re-renders and a refetch (exactly 2 lines: `tile-without-overlay-identifier` `{layout, tiles:1}` and `layout-without-fab` `{layout}`); none while loading |
| an equal re-render                                                                     | hands `CameraTile` the **same** `overlays` array reference; a changed set a new one                                                                                                                |
| a held change                                                                          | measured when it is shown, not when it arrives; a set that flaps A → B → A while held is not re-measured                                                                                           |

Per extraction (each commit body repeats this):

| Commit                    | §IV legs touched                                              | Result                                                                                                        |
| ------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `gridPieces`              | composite + render (static presentational pieces)             | unchanged                                                                                                     |
| `Tile.tsx`                | none (a module move, same component and key at the call site) | unchanged                                                                                                     |
| `useLatestAccessToken`    | the token identity every tile effect keys on                  | unchanged                                                                                                     |
| `useLayoutFaultReporting` | none (log only)                                               | unchanged; called right after `useOverlayHubHandlers`, so effect order is unchanged                           |
| `useLayoutData`           | composite + render (the page's data)                          | unchanged; `items` still computed in render, no `useMemo`                                                     |
| `useTileOverlay`          | event → overlay state (the overlay read and verdict)          | unchanged                                                                                                     |
| `useTileLiveElements`     | event → overlay state (the resolved-text path)                | unchanged                                                                                                     |
| `useHeldOverlay`          | label hold (ADR-0129) and overlay draw (ADR-0123)             | unchanged: `useStableByKey` still state-adjusted-in-render, the draw effect still keyed on the derived scalar |

No dependency array was changed; no `useMemo`, state or ref was added anywhere.

## Probing before moving: 42 of 82 deliberate breaks went unnoticed by the family

Each break was one named, exactly-once substitution into the unsplit grid (`probe480.cjs`), the covering family run, the file restored
from git; matrix: 40 noticed, **42 not** (51 %). The 42, by area: the whole unavailable-handling block (the callback, `replace`, the
loading/error conditions, both dependency arrays); the live-update wiring (`onArchived` matching any layout, no refetch on reconnect,
the lifecycle always enabled, the alignment fed the wrong count); the fault latch's once-per-page guards and its dependency array; the
painted-element key's kind, y, width and height and the draw measurement's keying, `highlighted` dependency and guard; the default
colour, a shape's height, the template-miss guards, its `skip` and `publicationKey`, the verdict's dependency; the page's playout and
out-of-alignment props, the Back button rules, the grid's rows and columns, the highlight class, the alignment badge, the lifted-refusal
key, `data-camera-identifier`.

**61 tests in six new files** pin 41 of them plus one found later (below). Each was green on the unsplit grid and proved able to fail
with the message read. Examples: A2 `expected [Function] to be [Function] // Object.is equality` (a new `getToken` per render); D7
`expected [] to deeply equal [ [ 'hub', …(2) ] ]` (the fab fault when only the fab changes); I1/I4/I5/I6 `expected "vi.fn()" to be
called 2 times, but got 1 times` (the key must see kind, y, width, height); J1 `expected "vi.fn()" to be called 1 times, but got 2
times` (the measurement keyed on the array re-fires when a hold flaps back); K9 `expected [ null, null ] to deeply equal [ 'cam-0-0',
'cam-0-1' ]`.

After each extraction the matching break was applied to the **new unit** and the same test failed there.

### One result that did not reproduce: I10 (`reportHeld`'s dependency array)

In the matrix, breaking `reportHeld`'s deps to `[]` failed one timing-sensitive test (`CellPageLabelAge`, "still withheld 115 ms…").
Re-applying it to the pre-extraction `Tile.tsx` left the whole family green, so the one failure was a flake, not a pin: the break was
**unnoticed**. A fresh test (`LayoutGridHeldReport.test.tsx`, 2 tests) now pins the behaviour: after a camera changes in place the
`label_delay` report names the new camera (`expected "vi.fn()" to be called with arguments: [ 'label_delay', 'cam-B', …(2) ]`, received
`cam-A`), and the report uses the page's stable `getToken`.

### Defended jointly

| Break                                                              | Result                                                                                                                                                                      |
| ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| B3 (the leave-when-unavailable effect without `!isLoading`), alone | green: the `data !== undefined` term masks it, because RTK's `isLoading` is false whenever `data` is defined                                                                |
| B3 **and** `data !== undefined` removed                            | "does not leave before the layout has arrived" fails (`expected "vi.fn()" to not be called at all, but actually been called 1 times`)                                       |
| H9 (the template-miss refetch never skipped)                       | pinned at the input of the mocked `useTemplateMissRefetch`; under real RTK it is behaviourally redundant, because a skipped query has no data so `hasMiss` is already false |

F10 and K5 (the out-of-alignment prop and the badge) cannot be told apart by behaviour; each alone fails the one test that marks only the
released tile. **No break was equivalent.**

## Slips and judgements, kept visible

- Two unrelated files, `src/ScenarioSimulator/CameraCatalog/CameraCatalogClient.cs` and `src/StreamDistribution/Api/Program.cs`, were
  modified in the working tree during this work. Neither is from this branch; neither was ever staged, and no commit of the branch
  contains them (`--autostash` restored the first byte-identically across the rebase).
- The first import pass for the new `Tile.tsx` copied the grid's `../` imports unchanged; `tsc` flagged them before any commit.
  Several scripted extractions needed a second run (an unused import, a comment quoted differently than it is wrapped).
- The "latest-token" hook now exists four times in the repo (`management-web` ×2, an inline copy in `CameraDetailPage`, and this one in
  `kiosk-web`): recorded as a follow-up, not merged here.

## Not observed

CI on Linux (the first run is this PR's); a running wall. The §IV claim rests on the family plus the identity/count tests under jsdom,
not on a measured wall.

## QA

Recorded in the PR (Phase 6, `/code-review medium`).
