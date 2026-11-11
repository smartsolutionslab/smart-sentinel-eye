# Implementation Plan: Frontend import and export conventions

**Branch**: `492-frontend-import-conventions` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/492-frontend-import-conventions/spec.md`. Issue #3257. Lane: supervised.

## Summary

Three changes to the five frontend apps, behaviour-preserving, in this order: (1) drop the `.js` suffix from relative specifiers with an
AST codemod, proven by resolving every specifier before and after and comparing the files it lands on; (2) one eslint rule that fails a
relative specifier ending in `.js`/`.jsx`/`.ts`/`.tsx`; (3) two eslint rules for export shape: no default exports outside three
tool-required files, and re-export syntax only in the files that are a package's public entry. No new dependency: every rule is an ESLint
built-in. The rules are written down in `CONTRIBUTING.md` with their reasons.

**Latency budget (§IV): legs touched: none.** ADR-0074 (two frontend apps) is the governing frontend ADR; ADR-0150 is the precedent for a
lint rule that states what it cannot see. `@microsoft/signalr` is not touched.

## Technical Context

**Language/Version**: TypeScript (ES2023, `module: ESNext`, `moduleResolution: Bundler` in `tsconfig.base.json`, `noEmit`)

**Primary Dependencies**: Vite, vitest, `@module-federation/vite`, ESLint 10 flat config (`@typescript-eslint` 8.72.1). No addition.

**Testing**: vitest per app (the characterisation), `tsc --noEmit`, `eslint src --max-warnings 0` by exit code, `prettier --check`, `vite build`

**Project Type**: pnpm workspace, four apps plus `apps/shared`. There is no `kiosk-wall` app on this tree.

**Constraints**: no test file edited; identical file and test counts per app; no change to `apps/shared`'s `exports` map or any entry point.

**Scale/Scope**: about 680 specifiers in roughly 500 files, 5 eslint configs, 1 contributor document.

## Constitution Check

| Gate                                                            | Result                                                                                                                                          |
| --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| §Frontend stack is locked: no new library                       | Pass. Built-in rules only.                                                                                                                      |
| §Testing: refactor stays green, characterisation observed first | Pass. Baselines captured before the first edit (T-baseline). The lint rules are new behaviour, so each is shown failing first (counterfactual). |
| §IV latency legs                                                | None touched.                                                                                                                                   |
| ADR-0036: smallest change, no speculative generality            | Pass. No alias, no import-order rule, no unused-export rule (spec Assumptions).                                                                 |
| The lane may not weaken a gate                                  | Pass. Three rules are added, none relaxed.                                                                                                      |

Re-checked after design: unchanged.

## Findings that change the spec's assumptions (research.md)

1. **`moduleResolution` is already `Bundler`** in `tsconfig.base.json`, inherited by all five apps. Extensionless relative imports are legal today and already used. Nothing to change.
2. **There is no single default export.** There are nine, in three groups, and all are tool-required: six configs (`vite`, `vitest`, `tailwind`) outside the linted `src`; `CamerasSurface.tsx` and `EventIngestionSurface.tsx`, which the shell reads as `module.default` (`RemoteSurface.tsx:171`); and `mismatchRuntimePlugin.ts`, which the federation plugin calls as a factory. The spec's "the one default export in management-web" is wrong; FR-010 is met by recording all three `src` ones as allowed.
3. **`exports` map targets that are barrels:** `.` (via `src/index.ts`), `./api`, `./streaming`, `./ui`, `./hooks`, `./types`. The seventh barrel, `ui/primitives/index.ts`, is `export {}` and is re-exported only by `ui/index.ts`. Apps import by subpath, never by relative path into shared.
4. **Six file-and-directory name pairs** exist (`App.tsx` beside `App/`, `LayoutGrid`, `OverlayDraftForm`, `CameraViewer`, `OverlayEditor`). `./App` is resolved file-first by both TypeScript and Vite, as `./App.js` is today, but this is exactly the case an extension drop can silently change, so it is covered by the resolution check, not assumed.
5. **One side-effect import** (`import './radixJsdom.js'`) and **167 dynamic / mock forms**: a regex would miss some, so the codemod is AST-based.
6. `vite.config.ts` (two apps) imports `./src/app/devProxy.ts` and `../shared/src/ui/fonts/fontPreload.ts` with `.ts`, because Node evaluates them. `eslint src` does not lint them, so they keep the extension and are outside the rule.

## Design

### D1. The codemod (US1)

A Node script using the TypeScript compiler API walks every `.ts`/`.tsx` under `apps/*/src`, and rewrites the string literal of: static
`import`/`export … from`, side-effect `import`, `import()`, and `vi.mock` / `vi.doMock` / `vi.importActual` / `vi.importMock`, where the
specifier starts `./` or `../` and ends `.js`. It removes the `.js` only. It does not rewrite `/index`, reorder, or touch any other text.
The script and the checker below live in `specs/492-frontend-import-conventions/tools/`.

### D2. The equivalence checker (the characterisation for US1)

Before the codemod, record `{file, specifier, resolvedFile}` for every relative specifier using `ts.resolveModuleName` with the app's real
compiler options. After, record the same. **Pass = every pair's `resolvedFile` is identical.** This is stronger than the suites, since a
module that resolves differently but is never exercised by a test would pass them. Proven by counterfactual: copy a file into a scratch
app, point one specifier at the directory that shadows it, and see the checker report a difference.

Also captured before and after, per app: vitest file and test counts, `tsc --noEmit`, `eslint` exit code, `prettier --check`, `vite build`
(four apps) and the shell loading both remotes in the e2e federation spec.

### D3. Rule 1: relative specifiers carry no extension (US2)

One shared fragment, `eslint.frontend-imports.mjs` at the repository root, exports the config objects; each app's `eslint.config.js`
spreads it. This avoids five copies of the rule. It uses:

- `no-restricted-imports` with `patterns: [{ regex: '^\\.{1,2}/.*\\.(js|jsx|ts|tsx)$', message }]`, which covers static imports and `export … from`.
- Two `no-restricted-syntax` selectors for what that rule does not see: `ImportExpression > Literal` and `CallExpression[callee.object.name='vi'] > Literal:first-child` for the mock family.
- **A flat-config trap, stated here so it is not rediscovered:** a later block that sets `no-restricted-syntax` replaces the earlier one's options for the files it matches. The test block (ADR-0150's rule) already sets it for `*.test.{ts,tsx}`, so the shared selectors are exported as an array and the test block spreads them in; otherwise tests would silently escape the rule. A test file with a `.js` mock path is part of the counterfactual.
- Asset and package specifiers (`./x.css`, `./x.svg?url`, `react`) do not match the regex (no `.js|.ts` ending, not relative).

**What it cannot see** (stated, per ADR-0150's precedent): a specifier built at runtime (`import(\`./${name}.js\`)`), and any file `eslint src`does not lint, which includes the root-level configs. Neither exists in`src` today.

### D4. Rule 2: default exports only where a tool requires them (US3)

`no-restricted-exports` with `restrictDefaultExports: { direct: true, named: true, defaultFrom: true, namedFrom: true, namedFromString: true }`, applied to `src/**`, switched off by a later block for exactly three files: `apps/management-cameras/src/CamerasSurface.tsx`, `apps/management-event-ingestion/src/EventIngestionSurface.tsx`, `apps/management-web/src/app/navigation/mismatchRuntimePlugin.ts`. Each carries the one-line why in the eslint block, not in the file.

### D5. Rule 3: a barrel is a package's public entry (US3)

**The rule, as it will be written:** _A file may re-export from another module (`export * from`, `export { x } from`) only if it is a public
entry of a package, meaning `apps/shared/package.json` names it in `exports`. Inside an application, import from the module that defines
the thing. Why: a re-export file is a second way to reach the same code, it hides which module you depend on, and it makes import cycles
easy and rename moves invisible._

Enforced by two `no-restricted-syntax` selectors (`ExportAllDeclaration`, `ExportNamedDeclaration[source]`) on `src/**`, switched off for
the barrel glob `apps/shared/src/{index,api/index,hooks/index,streaming/index,types/index,ui/index}.ts`. `ui/primitives/index.ts` contains no
re-export (`export {}`), so it passes the rule and is recorded as a placeholder in research.md, not changed. The test block's selector
array carries these too (the trap in D3).

**What it cannot see:** a barrel that is added to `exports` and the glob together; that is a deliberate act in two visible places, and a
reviewer's call. The rule stops the accidental barrel, not the decided one.

### D6. Contributor guidance

`CONTRIBUTING.md` gains a short "Frontend imports and exports" section: no extension on a relative import, named exports, the three
allowed default exports and why, the barrel rule and its reason. No ADR (the decision is a convention enforced by lint, recorded where
contributors read it). If the owner wants one, it is a one-line addition at the gate.

## Project Structure

```text
specs/492-frontend-import-conventions/
├── spec.md  plan.md  research.md  quickstart.md  tasks.md (next)
└── tools/   codemod.mjs  resolve-snapshot.mjs

eslint.frontend-imports.mjs            # new: shared rule fragment
apps/{shared,management-web,management-cameras,management-event-ingestion,kiosk-web}/eslint.config.js   # spread the fragment
apps/*/src/**                          # specifier edits only (about 500 files)
CONTRIBUTING.md                        # the written rule
```

## Sequence and risk

1. Baselines captured, nothing edited (resolve snapshot, vitest counts, tsc, eslint, prettier, builds).
2. Rules first, in a form that **fails on the current tree** (red): the new behaviour is shown failing with 600+ reported violations quoted in the PR, per ADR-0139. Counterfactuals for rules 2 and 3 are constructed violations.
3. Codemod, then the checker and the baselines again, rule 1 now green. Commit per app so each commit builds.
4. **Merge-conflict risk:** a change across ~500 files conflicts with any open frontend PR that touches an import line. Mitigation: do the codemod last, rebase on `develop` immediately before running it, and re-run it (it is idempotent) rather than resolving conflicts by hand. Check open frontend PRs before step 3.
5. CI's `frontend` and the `backend` job's architecture tests (they scan `.tsx`) both run; the full-stack e2e run is the evidence for the shell loading both remotes.
