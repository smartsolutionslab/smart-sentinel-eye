# Verification — spec 490 (#3243)

A **test-only** change whose subject did not move. The diff against `origin/develop` is one test file
(`apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx`, +5 −2) plus the spec documents; **no production file changed**. Observed
under jsdom in vitest, not in a browser. The usual phase-4a rule ("an assertion that has to be edited means the behaviour moved") is
answered with evidence rather than waived: the component is byte-identical, the edited assertion passes on **both** jsdom versions, and it
fails on four counterfactuals that change what the component really does.

## §IV legs touched: none

One test file in `kiosk-web`. No source file changed, so nothing between event arrival and overlay render is touched.

## The change

The two read-backs go through a local helper that rewrites only `minmax(0px,` to `minmax(0,`; the literals they are compared with are
unchanged (`repeat(3, minmax(0, 1fr))`, `repeat(2, minmax(0, 1fr))`). A two-line comment says why (a CSS engine may serialise a zero
length as `0px`; jsdom 31 does). The column count, the row count and the `minmax(0, 1fr)` track shape all stay pinned.

## FR-003 / SC-001 — passes on both jsdom versions

Installed `jsdom` was read from `node_modules` each time, not from the manifest.

| jsdom (read from disk) | `LayoutGridPage.test.tsx` | full `kiosk-web` suite         |
| ---------------------- | ------------------------- | ------------------------------ |
| 30.1.3 (before edit)   | 12 passed                 | 39 files, 392 passed           |
| 30.1.3 (after edit)    | 12 passed                 | 39 files, 392 passed           |
| 31.0.0 (after edit)    | 12 passed, 0 failed       | 39 files, 392 passed, 0 failed |

On 31.0.0 the bump was re-made temporarily (the five `jsdom` pins, `pnpm install --lockfile-only`, `pnpm install --frozen-lockfile`),
because `develop` has moved since spec 489's patch was saved. The six paths were then restored from git, the install redone, `30.1.3` read
back from disk, and `git status` confirmed clean. The rows assertion, which spec 489's run never reached, passes on 31.0.0.

## FR-004 / SC-002 — four counterfactuals, observed on jsdom 30.1.3

Each an exactly-once substitution, restored from git after each run; the runner refuses to start on a dirty tree. The tree was clean
afterwards.

| Id  | Break                                                      | Result                                                                   |
| --- | ---------------------------------------------------------- | ------------------------------------------------------------------------ |
| a   | test data `cols: 3` → `cols: 4`, assertion still expects 3 | **fails**: `expected 'repeat(4, minmax(0, 1fr))' to be 'repeat(3, …)'`   |
| b   | test data `rows: 2` → `rows: 3`, assertion still expects 2 | **fails**: `expected 'repeat(3, minmax(0, 1fr))' to be 'repeat(2, …)'`   |
| c1  | `LayoutGrid.tsx` column track `minmax(0, 1fr)` → `1fr`     | **fails**: `expected 'repeat(3, 1fr)' to be 'repeat(3, minmax(0, 1fr))'` |
| c2  | `LayoutGrid.tsx` row track `minmax(0, 1fr)` → `1fr`        | **fails**: `expected 'repeat(2, 1fr)' to be 'repeat(2, minmax(0, 1fr))'` |

Each leaves 11 of 12 passing and fails only this test. c1 and c2 show the normaliser did not weaken the track-shape guarantee: only the
zero's spelling is tolerated, the `minmax(0, …)` shape is not. c1 and c2 touched a production file as a probe, after the change was
committed; it was restored from git and is not in any commit.

## FR-005 — gates (exit codes read)

| Check                                       | Result                         |
| ------------------------------------------- | ------------------------------ |
| `eslint src --max-warnings 0` (`kiosk-web`) | 0                              |
| `tsc --noEmit` (`kiosk-web`)                | 0                              |
| `prettier --check` on the test file         | 0                              |
| `kiosk-web` suite, file and test counts     | 39 files, 392 tests, unchanged |
| `Architecture.Tests` (foreground)           | 3254 / 3254 passed (4 m 40 s)  |

## What this does not show

- Only `kiosk-web` was run on jsdom 31.0.0 here. The other four apps' results on 31 are spec 489's: identical to baseline, with this
  one failure the only difference, which this change removes.
- The console-noise comparison and the visibility/style probes of spec 489 (T009, T012–T014) are not part of this spec; they resume with
  #3240 once this lands.

## Code review (T009)

Not yet run.
