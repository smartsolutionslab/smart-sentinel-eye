# Implementation Plan: Make the kiosk grid-template assertion independent of jsdom's serialiser

**Branch**: `490-layout-grid-assertion` | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md) | **Issue**: #3243

## Summary

Edit the two assertions of one test so they read the grid's template back in a form that does not depend on how a CSS engine spells a zero
length, keeping the column count, the row count and the `minmax(0, 1fr)` track shape pinned. Prove it by running the file on jsdom 30.1.3
and on 31.0.0, and by four counterfactuals that change what the component really does.

## Technical Context

`apps/kiosk-web`, vitest 5.1.0 under jsdom. The test (`LayoutGridPage.test.tsx:165–172`) renders `LayoutGrid` from fixtures
(`layoutOf(..., { rows: 2, cols: 3 })`) and reads `grid.style.gridTemplateColumns` / `gridTemplateRows`. The component
(`LayoutGrid.tsx:149–150`) writes `repeat(${published.gridCols}, minmax(0, 1fr))` and the rows twin.

## Constitution Check

§IV: not on the path (one test file). ADR-0036: smallest change; a comment only where the why is non-obvious (FR-006). §Testing: a
test-only change whose subject did not move; the rule "an edited assertion means the behaviour moved" is answered by evidence, not
waived (spec colour paragraph). No ADR.

## Decisions

**D1. A targeted normaliser, local to the test, over `toHaveStyle`.** A small helper in the test file rewrites the one thing an engine may
spell differently, the zero minimum of a track: `value.replace(/minmax\(0px,/g, 'minmax(0,')`. The assertions then compare the
normalised read-back with the same literal as before, so the column count, row count and `minmax(0, 1fr)` shape all stay pinned (FR-002).
Rejected: `expect(grid).toHaveStyle({ gridTemplateColumns: … })`. jest-dom normalises the expected value through the same engine, which
would make the test engine-independent for free, but it can **pass vacuously** if the engine drops a declaration it does not parse (the
expected side collapses to nothing). The counterfactuals in D4 would catch that, but an explicit normaliser makes the guarantee
readable instead of dependent on a matcher's internals. If the normaliser fails on 31 for a reason other than `0px` (D3 finds out), the
helper grows by that one case, stated and justified, not a blanket normalisation (spec edge case 1).

**D2. A why-comment, three lines at most (FR-006).** It says a CSS engine may serialise a zero length as `0px`, that jsdom 31 does, and
that the literal below is the authored string. No issue number in the code (ADR-0036: references belong in the PR).

**D3. Observe on jsdom 31 by re-making the bump, not by applying the old patch.** `develop` has moved since the spec 489 patch was saved
(PR #3242 changed the lockfile's `@hookform/resolvers` rows), so the patch's lockfile hunks may not apply. Instead, after the test change
is **committed**: set `"jsdom": "31.0.0"` in the five `apps/*/package.json`, `pnpm install --lockfile-only`, `pnpm install
--frozen-lockfile`, read the installed version from `node_modules`, run the file and then the full `kiosk-web` suite; then restore
**only those six paths** from git and `pnpm install --frozen-lockfile` again, and read `30.1.3` back from disk. Never `git stash` and never
`git checkout -- .`. The rows assertion, not reached by the first failure in spec 489, is observed here.

**D4. Four counterfactuals, observed on 30.1.3, each an exactly-once substitution restored from git (FR-004).** A scratchpad probe
with a runner that restores only the file it broke and refuses to start on a dirty tree:

| Id | Break | Expected |
| -- | ----- | -------- |
| a  | in the test, `cols: 3` → `cols: 4` (component renders 4 columns, assertion expects 3) | fails |
| b  | in the test, `rows: 2` → `rows: 3` | fails |
| c1 | in `LayoutGrid.tsx`, the **columns** track `minmax(0, 1fr)` → `1fr` | fails |
| c2 | in `LayoutGrid.tsx`, the **rows** track `minmax(0, 1fr)` → `1fr` | fails |

c1/c2 touch a production file as a probe only, after the change is committed, so there is a clean tree to restore from. A break that
leaves the test green means the normaliser has weakened the assertion: fix the helper, not the probe.

**D5. Gates (FR-005).** In `kiosk-web`: `eslint src --max-warnings 0` (exit code read), `tsc --noEmit`, `prettier --check` on the file.
The full `kiosk-web` suite on 30.1.3 has the same counts as before (39 files, 392 tests) and on 31.0.0 shows 392 passed, 0 failed. One
vitest at a time. One foreground attempt at `Architecture.Tests`, which a `.tsx` change meets in CI regardless.

**D6. Read-only except one file.** `git diff origin/develop --stat -- . ':!specs' ':!.specify'` shows `LayoutGridPage.test.tsx` only (SC-003).

## Phases and commits

1. `docs(490): plan`, `docs(490): tasks`.
2. `test(kiosk-web): read the layout grid template back independent of the CSS engine's zero-length spelling` (the one test file), made
   **before** the D3/D4 probes so they have a clean tree to restore from.
3. `docs(490): verification note, tick tasks`.

## Risks and how they are covered

- **The normaliser weakens the test.** D4 shows it still fails on a wrong count, a wrong row count and a changed track shape.
- **jsdom 31 differs in more than `0px`.** D3 observes it; the helper grows by one stated case if so.
- **The probe edits a production file.** After the commit only, restored from git, and the runner refuses to start on a dirty tree.
- **The local install is left on jsdom 31.** D3 ends with a restore of the six paths, a frozen install and a read-back of `30.1.3`.
- **Merge friction with #3240.** #3240's bump PR rebases onto this; the lockfile is re-resolved with `pnpm install --lockfile-only`.

## Project Structure

```
specs/490-layout-grid-assertion/
  spec.md  plan.md  tasks.md  verification.md  checklists/requirements.md
apps/kiosk-web/src/features/cell/LayoutGridPage.test.tsx    (one test, two assertions, one local helper)
```
