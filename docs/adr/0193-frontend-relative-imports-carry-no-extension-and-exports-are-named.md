# ADR-0193: Frontend relative imports carry no extension; exports are named; only a public entry re-exports

**Status:** Proposed (written at the owner's request after #3257 merged; the rules themselves have been in force since #3260)
**Date:** 2026-11-12
**Supersedes:** —
**Superseded by:** —
**Relates to:** ADR-0074 (two React apps), ADR-0150 (the precedent for an eslint rule with a stated blind spot), ADR-0036 (smallest change), ADR-0107 / ADR-0168 (federated remotes)

## Context

The five frontend apps (`shared`, `management-web`, `management-cameras`, `management-event-ingestion`, `kiosk-web`) wrote relative imports as `./Button.js`
in about 500 files, while `management-web` also mixed in extensionless ones. The suffix names a file TypeScript would emit. Nothing here
emits: `tsconfig.base.json` sets `noEmit` and `moduleResolution: "Bundler"`, and Vite bundles. It was a NodeNext habit with no effect, and it
made two styles coexist.

Three facts found while specifying (spec 492) bound the decision:

1. **There was no single default export.** There are nine: six tool configs outside `src`, the two federated surfaces the shell reads as
   `module.default`, and the federation runtime plugin, which the plugin calls as a factory. All are tool-required.
2. **`apps/shared` publishes through an `exports` map** whose targets are barrels (`src/index.ts` and five subpath indexes). Apps import by
   subpath and never by a relative path into `shared`.
3. **A flat-config trap.** A later ESLint block that sets `no-restricted-syntax` or `no-restricted-imports` replaces the earlier options for
   the files it matches. Three existing blocks did exactly that (the ADR-0150 test block, shared's composites block, and the
   cameras/event-ingestion "no import from management-web" block), so a rule added once at the top would have silently missed them.

## Decision

1. **A relative import has no extension.** `./Button`, not `./Button.js`. This holds for static imports, `export … from`, side-effect
   imports, `import()` and the `vi.mock` family. Config files Node evaluates directly (`vite.config.ts`) keep the extension they need and
   are outside `src`. Asset and package specifiers are untouched.
2. **Exports are named.** A default export is allowed only where a tool requires one. Today that is three files:
   `management-cameras/src/CamerasSurface.tsx`, `management-event-ingestion/src/EventIngestionSurface.tsx` and
   `management-web/src/app/navigation/mismatchRuntimePlugin.ts`.
3. **A re-export is allowed only at a package's public entry.** `export * from` and `export { x } from` are permitted in the `apps/shared`
   files its `exports` map publishes and that re-export on purpose: the six barrels, and `ui/composites/CameraViewer.tsx`, which four other
   apps import and mock by that path. Elsewhere, import from the module that defines the thing.
4. **All three are enforced by ESLint built-ins**, from one fragment at the repository root (`eslint.frontend-imports.mjs`) that each app's
   config spreads: `no-restricted-imports` (regex), `no-restricted-syntax` selectors, and `no-restricted-exports`. No new dependency. The
   selectors are exported as an array and spread into every block that sets `no-restricted-syntax` or `no-restricted-imports` for the same
   files. Adding a deliberate barrel or default export means editing that fragment, where a reviewer sees it.
5. **What the rules cannot see**, stated as ADR-0150 does: a specifier built at runtime, and any file `eslint src` does not lint.

## Consequences

**Easier:** one import style across all apps; a new `.js` suffix, an accidental barrel or a stray default export fails lint instead of
review; a re-export no longer hides which module a file depends on.

**Harder:** a new deliberate barrel or tool-required default export needs a change to the shared fragment. Any new ESLint block that sets
`no-restricted-syntax` or `no-restricted-imports` must spread the fragment's arrays, or it silently drops the rules (the trap above).
Guards that read the real configs (`scripts/settle-rule.test.mjs`) count the fragment's selectors rather than a literal.

**Constrained:** `CameraViewer` stays a re-exporting facade for as long as other apps import and mock it by that path. Removing it is a
separate, behaviour-preserving change that edits those mocks.

**Not changed:** behaviour. 685 relative specifiers lost their suffix; a resolution snapshot (every specifier through `ts.resolveModuleName`
with each app's real options) is identical before and after (732 and 732, 0 differing), and suite counts are unchanged in all five apps.

## Alternatives Considered

- **Keep `.js` everywhere, enforce it.** Matches NodeNext, but nothing here emits, so the suffix would be convention with no consumer, and
  one existing style (`management-web`) was already extensionless.
- **Path aliases (`@/…`).** A larger change with its own resolution risk across Vite, vitest and Module Federation; out of scope (ADR-0036).
- **Ban barrels outright.** `apps/shared` publishes through an `exports` map whose targets are barrels, so a ban would break the package
  contract.
- **A plugin (`eslint-plugin-import`, `no-restricted-exports` wrappers).** A new dependency for what built-ins already express.
- **Ban default exports with no exceptions.** Not possible: the shell reads `module.default` of each surface, and the federation plugin
  calls the runtime plugin as a default-exported factory.

## Implementation Notes

Delivered in #3260 (spec 492, issue #3257). Documented for contributors in `CONTRIBUTING.md`, "Imports and exports".
