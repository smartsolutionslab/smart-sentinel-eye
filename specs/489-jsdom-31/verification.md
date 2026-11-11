# Verification — spec 489 (#3240)

**Outcome: the bump lands unmodified.** Five `devDependencies` pins, `30.1.3` → `31.0.0`, plus the lockfile. No source, test or snapshot
file changed (FR-001). The first attempt was blocked by one jsdom behaviour change in a kiosk-web assertion (history below); that assertion
was made engine-independent as its own test-only change (#3243, PR #3250, merged), and this bump was then re-made on top of it and passes
with identical counts in all five apps. Observed under jsdom in vitest, not in a browser.

## §IV legs touched: none

`jsdom` is a dev dependency used only by the test runner. It ships in no bundle, and no source or test file changed.
`@microsoft/signalr` was deliberately not touched (owner instruction, 2026-11-10).

## Versions and environment

Node `v24.21.0` (engines `^22.22.2 || ^24.15.0 || >=26`). One vitest at a time. The installed `jsdom` was **read from `node_modules` in each
of the five apps**, not from the manifest:

|        | `shared` | `kiosk-web` | `management-event-ingestion` | `management-cameras` | `management-web` |
| ------ | -------- | ----------- | ---------------------------- | -------------------- | ---------------- |
| before | 30.1.3   | 30.1.3      | 30.1.3                       | 30.1.3               | 30.1.3           |
| after  | 31.0.0   | 31.0.0      | 31.0.0                       | 31.0.0               | 31.0.0           |

Single copy: `pnpm-lock.yaml` mentions only `jsdom@31.0.0` (15 references) and nothing at 30. A `jsdom@30.1.3` directory still sits under
`node_modules/.pnpm` as residue of the earlier install; no app resolves it and the lockfile does not name it.

## FR-002 / SC-001 — counts identical, unmodified

Counts are vitest's JSON reporter; every run 0 skipped, 0 todo, exit 0.

| App                          | Baseline 1 (30.1.3)   | Baseline 2 (30.1.3)   | After (31.0.0)                  |
| ---------------------------- | --------------------- | --------------------- | ------------------------------- |
| `shared`                     | 92 files, 1395 passed | 92 files, 1395 passed | 92 files, 1395 passed, 0 failed |
| `kiosk-web`                  | 39 files, 392 passed  | 39 files, 392 passed  | 39 files, 392 passed, 0 failed  |
| `management-event-ingestion` | 4 files, 27 passed    | 4 files, 27 passed    | 4 files, 27 passed, 0 failed    |
| `management-cameras`         | 24 files, 151 passed  | 24 files, 151 passed  | 24 files, 151 passed, 0 failed  |
| `management-web`             | 102 files, 740 passed | 102 files, 740 passed | 102 files, 740 passed, 0 failed |

Total 2,705 tests in both. `LayoutGridPage.test.tsx` passes on 31 because of #3250.

## The lockfile diff is jsdom's own tree (T007)

`git diff --stat`: the five manifests and `pnpm-lock.yaml`. Removed with jsdom 30: its CSS and selector tree (`css-tree`,
`@csstools/*`, `@asamuzakjp/*`, `bidi-js`, `mdn-data`, `require-from-string`, `parse5@8.0.1`, `entities@8.1.0`, `undici@8.12.2`). Added:
`bcp-47`, `bcp-47-match`, `is-alphabetical`, `is-alphanumerical`, `is-decimal`, `parse5-parser-stream`, `parse5@8.1.1`, `entities@9.0.0`,
`undici@8.12.4`, `jsdom@31.0.0`. The `vitest@5.1.0(...)` peer key changed from `jsdom@30.1.3` to `jsdom@31.0.0`. Nothing outside jsdom's
tree moved. `@napi-rs/canvas` appears only as jsdom's optional peer metadata; nothing canvas-related is installed (the canvas backend change
in 31 therefore does not reach us).

## FR-005 / SC-003 — console noise (T009)

Noise-block counts (`stderr |` / `stdout |` lines), baseline 1 / baseline 2 / after:

| App                          | Baseline 1 | Baseline 2 | After |
| ---------------------------- | ---------- | ---------- | ----- |
| `shared`                     | 134        | 134        | 135   |
| `kiosk-web`                  | 83         | 84         | 84    |
| `management-event-ingestion` | 2          | 2          | 2     |
| `management-cameras`         | 46         | 46         | 45    |
| `management-web`             | 840        | 841        | 844   |

Counts move by a few blocks run to run on the same version (the two baselines already differ). `management-web` is the one app outside ±1,
so it was read rather than assumed:

- **Same files.** The four noisiest files are the same in baseline 2 and after (`OverlayEditPage`, `OverlayDraftFormLayers`, `OverlayDraftForm`,
  `LayoutsPageArchive`); the movement is a few blocks in some of them (`OverlayDraftForm` 66 → 69). One file, `SystemVariablesPageFocusRefetch`,
  printed noise in baseline 2 only (not in baseline 1 and not after), so it is itself intermittent.
- **No new kind of message.** The classified messages are identical in kind and in count: `Invalid URL` 105, `Failed to parse URL from
/camera-catalog/cameras?…` 49, `…/overlay-designer/overlays?…` 45, `…/layout-composition/layouts?…` 10, `ErrorBoundary` 5,
  `useNavigation must be used within a NavigationProvider` 4, one `audit` URL, one `Cannot read properties of undefined`. Present only
  after: none. Present only before: none.

These are existing test-environment messages (relative-URL fetches in jsdom, deliberate error-boundary tests); none is new and none is a
jsdom 31 diagnostic.

## FR-007 — gates on 31 (exit codes read)

| Check                                                                                                         | Result                        |
| ------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| `tsc --noEmit` in `shared`, `kiosk-web`, `management-event-ingestion`, `management-cameras`, `management-web` | 0 in all five                 |
| `prettier --check` on the five manifests                                                                      | 0                             |
| `eslint src --max-warnings 0` in all five apps                                                                | 0 in all five                 |
| `Architecture.Tests` (one foreground attempt)                                                                 | 3254 / 3254 passed (5 m 33 s) |

## FR-006 — are the visibility and style matchers live on 31? (T012–T014)

The risk: jsdom 31's new CSS engine could make `toBeVisible` / `toHaveStyle` pass vacuously, so a real regression would go unseen. Three
probes, one per app, both matchers, declared with their expected failing tests **before** running (commit `docs(489): declare the
visibility and style probes…`). Each is an exactly-once substitution in the component under test, run against the file's own tests on
jsdom **31.0.0**, restored from git afterwards; the runner refuses a dirty tree. Tree clean after.

| Id  | App / matcher                  | Break                                                                       | Result on 31.0.0                                                                                                                                                                                                                           |
| --- | ------------------------------ | --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| p1  | `management-web` `toHaveStyle` | `TileFrame.tsx` width `${widthPx}px` → `${widthPx + 1}px`                   | **fails** 1: "TileFrame is exactly the cell size it is given" (6 pass)                                                                                                                                                                     |
| p2  | `kiosk-web` `toBeVisible`      | `ViewerOverlay.tsx` label `<span>` gains `style={{ display: 'none' }}`      | **fails** 6 of 9 in `CameraTile.test.tsx`: the camera-A→B swap tests (`Stops showing camera A…`, `Never resolves to camera A…` ×2, `Reads "Stream is offline"…` ×2, `Reads Viewer error, not Idle…`), each `expect(element).toBeVisible()` |
| p3  | `shared` `toBeVisible`         | `ConfirmDialog.tsx` description `<div>` gains `style={{ display: 'none' }}` | **fails** 1: "Renders the caller words describing what confirming will do" (12 pass)                                                                                                                                                       |

All three matched their declared expectation. Inline `display: none` is honoured by `toBeVisible` and an inline pixel width by
`toHaveStyle` on the new engine, so the sampled assertions are live. **Limit, stated in the spec:** this samples three of the 13
visibility/style files; it shows those matchers can fail on 31, not that every assertion in the other ten does. An assertion that was already
vacuous on 30 would stay vacuous on 31, and this change would not reveal it.

## History: the first attempt was blocked, and why that was resolved this way

The first bump run failed one test, `kiosk-web` `LayoutGridPage.test.tsx` "lays the grid out…":
`expected 'repeat(3, minmax(0px, 1fr))' to be 'repeat(3, minmax(0, 1fr))'`. It reproduced alone. Classification: a jsdom behaviour change,
not a regression: `LayoutGrid.tsx:149–150` (`repeat(${published.gridCols}, minmax(0, 1fr))`) is unchanged and jsdom 31's integrated CSS engine
reads a zero length back as `0px`. Per plan D9 the assertion was not edited inside the bump. The owner chose to make it engine-independent
first, as its own test-only change (#3243 / PR #3250, which keeps the column count, row count and `minmax(0, …)` shape pinned, with four
counterfactuals). This bump was then re-made on top of it and needed zero edits.

## Reproducing

On this branch (`489-jsdom-31`): `pnpm install --frozen-lockfile`, then `vitest run` in each of the five apps. To return to the baseline,
set the five pins to `30.1.3` and reinstall.
