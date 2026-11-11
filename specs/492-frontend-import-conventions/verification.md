# Verification: frontend import and export conventions

Branch `492-frontend-import-conventions`, measured 2026-11-11. Baseline on `develop` at `77329946`; final on the branch tip.

## Gates, before and after

| App                        | tsc   | eslint | prettier (src) | vitest files / tests | vite build |
| -------------------------- | ----- | ------ | -------------- | -------------------- | ---------- |
| shared                     | 0 / 0 | 0 / 0  | 0 / 0          | 93 / 1398, same      | n/a        |
| kiosk-web                  | 0 / 0 | 0 / 0  | 0 / 0          | 39 / 392, same       | 0 / 0      |
| management-cameras         | 0 / 0 | 0 / 0  | 0 / 0          | 24 / 151, same       | 0 / 0      |
| management-event-ingestion | 0 / 0 | 0 / 0  | 0 / 0          | 4 / 27, same         | 0 / 0      |
| management-web             | 0 / 0 | 0 / 0  | 0 / 0          | 103 / 748, same      | 0 / 0      |

Each cell is baseline / final exit code. Eslint in the final column runs with the three new rules wired. `prettier --check .` at baseline
failed in three apps only because the baseline's own `vite build` had written `dist/`; the check above is on `src`.

## US1: the rewrite

- Codemod: 685 specifiers changed in 329 files, 0 left; a second run changed 0 (idempotent). The three remaining lines containing `.js'` are not imports (`remoteEntry.js` path strings in `remotes.test.ts`, a comment).
- Resolution snapshot, 732 relative specifiers in the five apps: **before 732, after 732, differing 0**, unresolved 4 before and 4 after (stylesheet imports).
- Counterfactual for the snapshot: pointing `./radixJsdom.js` at `./focusRefetch.js` gave `DIFF apps/shared/src/test/setup.ts#0: radixJsdom.ts -> focusRefetch.ts` and exit 1; reverting gave 0 differing, exit 0.
- Test files: 229 test and harness files changed. All differ from baseline by the suffix only (compared line by line); the one exception, `LayoutsPageArchive.test.tsx`, differs by prettier joining a wrapped import onto one line, token for token the same. No assertion, name or setup line changed. Two files needed that join (this and `gateway.test.ts`); `prettier --write` was run on those two files only.

## US2 and US3: the rules, red first

On the untouched tree, with the rules wired and nothing rewritten, lint failed in all five apps: **518 `no-restricted-imports` and 169 `no-restricted-syntax` reports**, for example
`'./gateway.js' import is restricted from being used by a pattern. A relative import names the module, not an emitted file: drop the extension (...)`.

Three places where a later flat-config block would have silently replaced a rule, each found by the red run showing fewer hits than the specifier count: the test block (`no-restricted-syntax`, ADR-0150), shared's composites block and the two remotes' "no import from management-web" block (`no-restricted-imports`). Each now spreads the shared pattern or selectors in.

Counterfactuals (constructed files in `apps/management-web/src/cf/`, removed afterwards), eslint output:

| Constructed                                                                    | Result                                       |
| ------------------------------------------------------------------------------ | -------------------------------------------- |
| `import { a } from './x.js'`                                                   | fails, extension message                     |
| `import './x.js'`                                                              | fails                                        |
| `import('./x.js')`                                                             | fails                                        |
| `export { c } from './x.js'`                                                   | fails (extension, and re-export)             |
| `vi.mock('./x.js')` in `*.test.ts`; `vi.mock('./y')` beside it                 | `.js` fails, extensionless passes            |
| `.test.ts` with `import('./x.js')`, `export * from './y'`, `vi.mock('./x.js')` | all three fail (the merged test block works) |
| `export default function F` in a normal file                                   | `Exporting 'default' is restricted`          |
| `export * from './y'` and `export { z } from './y'` in a normal file           | both fail, re-export message                 |
| `./x.css`, `./x.svg?url`, `react`                                              | pass                                         |

After removal, `eslint src --max-warnings 0` exits 0 in all five apps. The real tree contains the allowed cases and they pass: the three
default exports, the six barrels and `CameraViewer`.

## FR-010: what each existing export form is

- Default exports in `src` (3): `CamerasSurface.tsx`, `EventIngestionSurface.tsx` (the shell reads `module.default`), `mismatchRuntimePlugin.ts` (factory the federation plugin calls). Allowed, listed in `eslint.frontend-imports.mjs`. Six config files are outside `src`.
- Barrels (7): six are `exports` targets and allowed. `ui/primitives/index.ts` is `export {}`, re-exports nothing, passes, left as a placeholder.
- **Found by the rule:** `ui/composites/CameraViewer.tsx` re-exports its overlay types and elements. It is an `exports` target, and four other apps import and mock it by that path, so it is a public facade and is allowed with that reason. Changing it would mean editing other apps' mocks.

## Not done here

- Shell loading both remotes (SC-005) and the Architecture.Tests source scanners: CI's full-stack e2e and backend jobs. `typecheck:e2e` is known to fail on a clean `develop` and was not run.
