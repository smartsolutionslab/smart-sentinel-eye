# Research: Frontend import and export conventions

Measured 2026-11-11 on `develop` at `77329946`. Re-measure before the codemod.

## Why `.js`

The suffix is the NodeNext/Node16 convention: TypeScript requires the emitted file's name because Node resolves that output. Here
`noEmit` is on and Vite bundles, and `tsconfig.base.json` sets `moduleResolution: "Bundler"`, which resolves extensionless paths. So the suffix
is inert, and `apps/management-web` already mixes extensionless imports in.

## Counts (lines matching a relative specifier ending `.js`, all forms)

| App                        | Count |
| -------------------------- | ----- |
| shared                     | 240   |
| management-web             | 263   |
| kiosk-web                  | 148   |
| management-cameras         | 28    |
| management-event-ingestion | 5     |

The spec said 677 and the grep here gives 684: a different pattern. The codemod reports its own exact count, and SC-001 is "zero left", not a number.

## Resolution settings

All five apps extend `tsconfig.base.json` (`Bundler`, `allowImportingTsExtensions: false`). Four apps set `allowImportingTsExtensions: true` with
`noEmit`. Nothing to change. `apps/shared` consumers resolve through `package.json` `exports` (about 70 entries, each pointing at a `.ts`/`.tsx`
source file), which this change does not touch.

## Module federation

`exposes` paths (`./CamerasSurface` to `./src/CamerasSurface.tsx`) are configuration strings, not imports, and keep their form. The shell
reads the exposed module's `default`. `runtimePlugins` points at `./src/app/navigation/mismatchRuntimePlugin.ts`, a path the plugin embeds
raw, which is why that one file keeps its extension (vite.config comment).

## Default exports (nine)

| File                                                                                                                 | Why it is default                                 |
| -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| `{kiosk-web,management-web,management-cameras,management-event-ingestion}/vite.config.ts`, `shared/vitest.config.ts` | tool loads by name                                |
| `*/tailwind.config.ts` (four)                                                                                        | tool loads by name                                |
| `management-cameras/src/CamerasSurface.tsx`, `management-event-ingestion/src/EventIngestionSurface.tsx`              | federation contract: shell reads `module.default` |
| `management-web/src/app/navigation/mismatchRuntimePlugin.ts`                                                         | federation runtime-plugin factory                 |

(The configs are outside `eslint src`; only the last three need an allowance.)

## Barrels (seven, all in `apps/shared/src`)

`index.ts` (`export * from './types/index.js'`), `api/index.ts` (placeholder constant), `hooks/index.ts`, `streaming/index.ts`, `types/index.ts`
(types only), `ui/index.ts`, `ui/primitives/index.ts` (`export {}`). Six are `exports` targets. Nothing imports a barrel by relative
path other than `index.ts` and `ui/index.ts` re-exporting. 21 imports use `@smart-sentinel-eye/shared/hooks`.

## Hazards for the codemod

- Six `X.tsx` beside `X/` pairs (`App`, `LayoutGrid`, `OverlayDraftForm`, `CameraViewer`, `OverlayEditor` and `App` in kiosk): file-first resolution must hold. Covered by the resolve-snapshot comparison.
- No real `.js`/`.jsx`/`.mjs` source exists under `src`, so no specifier names a file that is genuinely JavaScript.
- No `X.ts` / `X.tsx` collisions.
- `import.meta.glob`: none.
- One side-effect import (`import './radixJsdom.js'`), and 167 dynamic or mock forms.

## Alternatives considered

- `eslint-plugin-import` (`import/extensions`, `import/no-default-export`): not installed, a new dependency for rules ESLint's built-ins can express. Rejected under ADR-0036 and the locked stack.
- Path aliases and import ordering: out of scope (spec).
- Forbidding barrels outright: `apps/shared`'s public entry points are barrels and consumers rely on them.
