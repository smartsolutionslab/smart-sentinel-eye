# Quickstart: verifying the import conventions

Run per app from the repository root (`<app>` is one of `shared`, `management-web`, `management-cameras`, `management-event-ingestion`, `kiosk-web`):

```sh
pnpm --filter <app> exec tsc --noEmit
pnpm --filter <app> lint            # by exit code
pnpm --filter <app> exec prettier --check .
pnpm --filter <app> test            # file and test counts must equal the baseline
pnpm --filter <app> build           # not shared
node specs/492-frontend-import-conventions/tools/resolve-snapshot.mjs > after.json   # then diff against the baseline snapshot
```

Counterfactuals: add `import x from './y.js'`, `export default function F() {}` in a non-allowed file, and `export * from './z'` in a
non-entry file; lint must fail on each and pass once removed. One vitest at a time.
