# Verification — spec 489 (#3240)

**Outcome: BLOCKED at T008 by one reproducing failure. The bump is not committed and no PR is opened** (plan D9: a characterisation that
finds a behaviour change reports it; it does not adjust the assertion). The branch holds documentation only. The failing run is
reproducible from the patch recipe at the end.

## What was observed

Node `v24.21.0` (engines `^22.22.2 || ^24.15.0 || >=26`). Counts come from vitest's JSON reporter; one vitest at a time; the baseline was
taken on a lockfile-true install and the installed `jsdom` was **read from `node_modules` in all five apps** (30.1.3, one copy).

| App                          | Baseline run 1                   | Baseline run 2                   | After the bump (jsdom 31.0.0)      |
| ---------------------------- | -------------------------------- | -------------------------------- | ---------------------------------- |
| `shared`                     | 92 files, 1395 passed, 0 skipped | 92 files, 1395 passed, 0 skipped | 92 files, 1395 passed, 0 skipped   |
| `kiosk-web`                  | 39 files, 392 passed, 0 skipped  | 39 files, 392 passed, 0 skipped  | 39 files, 391 passed, **1 failed** |
| `management-event-ingestion` | 4 files, 27 passed, 0 skipped    | 4 files, 27 passed, 0 skipped    | 4 files, 27 passed, 0 skipped      |
| `management-cameras`         | 24 files, 151 passed, 0 skipped  | 24 files, 151 passed, 0 skipped  | 24 files, 151 passed, 0 skipped    |
| `management-web`             | 102 files, 740 passed, 0 skipped | 102 files, 740 passed, 0 skipped | 102 files, 740 passed, 0 skipped   |

Baseline gates (`eslint src --max-warnings 0`, `tsc --noEmit`) exit 0 in all five apps. Test counts were identical between the two
baseline runs; only the count of console-noise blocks moved (`kiosk-web` 83 → 84, `management-web` 840 → 841), so noise is compared with a
tolerance of about ±1.

**The lockfile diff was clean (T007).** `git diff --stat` was the five manifests and `pnpm-lock.yaml`. Removed: jsdom 30's CSS and
selector tree (`css-tree`, `@csstools/*`, `@asamuzakjp/*`, `bidi-js`, `mdn-data`, `require-from-string`, `parse5@8.0.1`,
`entities@8.1.0`, `undici@8.12.2`) — gone entirely, so nothing else used them. Added: `bcp-47`, `bcp-47-match`, `is-alphabetical`,
`is-alphanumerical`, `is-decimal`, `parse5-parser-stream`, `parse5@8.1.1`, `entities@9.0.0`, `undici@8.12.4`, `jsdom@31.0.0` — `pnpm why`
traced every one to `jsdom@31.0.0`. The other `undici` in the tree (7.30.1, via Module Federation) is untouched. `@napi-rs/canvas`
appears only as jsdom's optional peer metadata; nothing canvas-related is installed.

## The failure (verbatim, reproduced alone once)

`apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx` — "LayoutGrid — the page > header and grid > lays the grid out with the
published revision columns and rows":

```
AssertionError: expected 'repeat(3, minmax(0px, 1fr))' to be 'repeat(3, minmax(0, 1fr))' // Object.is equality
    at apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx:170:46
```

Re-run alone: 1 failed, 11 passed in the file; the same message.

**Classification: a jsdom behaviour change, not a regression in our code.** The component sets the style from `LayoutGrid.tsx:149–150`
(`repeat(${published.gridCols}, minmax(0, 1fr))`, unchanged). The test reads it back through `element.style.gridTemplateColumns` and
hard-codes jsdom 30's echo of the string as written. jsdom 31's new integrated CSS engine ("CSS serialization and computed values can
differ from previous releases", 31.0.0 release notes) reads it back normalised, `minmax(0px, 1fr)`. The next line of the test asserts
`gridTemplateRows` the same way; it was not reached, because the first `expect` throws, so whether it also differs is **not observed**
(it is expected to). The test file is the only one matching `minmax` / `gridTemplate` in the `kiosk-web`, `shared` and `management-web`
test sources (`management-cameras` and `management-event-ingestion` were not searched; their suites passed).

This is the risk the spec named, observed once in 2,705 tests.

## Not yet done

- **T009**, the console-noise diff, is not finished. After the bump `management-web` shows 835 noise blocks against 840 and 841 in the two
  baselines (5–6 fewer, outside the ±1 tolerance) and `kiosk-web` 84 against 83 and 84. Which messages disappeared is **unread**.
- **T010–T014** (after-bump gates, the visibility/style probes) were not run, because T008 stopped the line.

## Decision needed (plan D9)

1. **Hold at jsdom 30.** Nothing changes; #3240 stays the tracker.
2. **Make the one assertion engine-independent first, as its own change (recommended).** Test-only, lands on `develop` while jsdom is
   still 30, and is shown to pass on **both** 30.1.3 and 31.0.0 and to fail on a real change (a counterfactual, for example 3 → 4
   columns). Then this bump goes through unmodified with zero edits, and the characterisation stays honest.
3. **Edit the assertion inside the bump PR.** Not recommended: the bump would no longer pass "unmodified", which is the evidence this
   spec exists to produce, and a reader of the PR cannot tell an adaptation from a masked regression.

## Probe declarations (T012, written before any probe ran)

Sample: one file per app, both matchers. A Tailwind class does nothing in jsdom (no stylesheet), so each probe hides or resizes the element
with an **inline style**, which jsdom 31's new CSS engine must still honour. Each is an exactly-once substitution in the component under
test, run against the file's own tests on jsdom **31.0.0**, restored from git afterwards.

| Id  | App            | Test file (matcher)                                                  | Substitution (component)                                                                                        | Expected to fail                                                       |
| --- | -------------- | -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| p1  | management-web | `TileFrame.test.tsx` (`toHaveStyle`)                                 | `TileFrame.tsx`: `width: \`${widthPx}px\`` becomes `width: \`${widthPx + 1}px\``                                | "is exactly the cell size it is given" (expects `635px`)               |
| p2  | kiosk-web      | `CameraTile.test.tsx` (`toBeVisible`)                                | `ViewerOverlay.tsx` (shared; renders the label the tile shows): the label `<span>` gains `style={{ display: 'none' }}` | the tests that assert `getByText('Connecting…')` / `'Stream is offline'` `toBeVisible` |
| p3  | shared         | `ConfirmDialog.test.tsx` (`toBeVisible`)                             | `ConfirmDialog.tsx`: the description `<div className="mt-2 …">` gains `style={{ display: 'none' }}`             | "Renders the caller words describing what confirming will do"          |

A matcher that did not notice is a finding, filed and not fixed in this change.

## Reproducing the after-bump run

On this branch: set `"jsdom": "31.0.0"` in the five `apps/*/package.json`, `pnpm install --lockfile-only`, `pnpm install
--frozen-lockfile`, then `vitest run` in `apps/kiosk-web`.

## §IV legs touched: none

`jsdom` is a dev dependency that ships in no bundle, and no source or test file was changed.
