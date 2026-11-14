# Contract: the one-origin module and the guard's allow-list

There is no HTTP surface. The contract is a small TypeScript module the suite imports, the scanner's rules, and the allow-list's schema.

## `e2e/support/origins.ts`

```ts
export const ONE_ORIGIN = 'http://localhost:5180'; // the gateway's address (ADR-0192); the only place the suite names it
export const KIOSK_PATH = '/kiosk/'; // the kiosk app's base path on the one origin (spec 494)
export const WALL_PATH = '/wall/'; // the wall display's base path
```

- `playwright.config.ts` and every spec and helper import these; nothing else in the suite spells a UI origin or a UI port.
- **`baseURL` is `ONE_ORIGIN`, never `ONE_ORIGIN + KIOSK_PATH`**: a base with a path makes `goto('/')` resolve to the root (the console).
- A page of the kiosk or the wall display is opened with `goto(KIOSK_PATH)` or `goto(WALL_PATH)`; a page of the console with its own route (`goto('/cameras')`).
- No environment override: nothing needs a second value.

## The scanner (`scripts/e2e-origins.mjs`)

| Rule                                                                                                                            | Scanned files                                                                                                                                |
| ------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| Flag `localhost:<port>` for `5173`-`5177` and `5180`, and a **standalone** numeric literal `5173`-`5177`, reporting `file:line` | `e2e/**`, `playwright.config.ts`, `scripts/*.sh`, `.github/workflows/*.yml`                                                                  |
| Flag the same in non-test application source, for runtime hard-coding                                                           | `apps/*/src/**` excluding `*.test.*` and `*.spec.*`; `apps/*/vite.config.ts`                                                                 |
| Allow `5180` in `e2e/support/origins.ts` only                                                                                   | (the one definition)                                                                                                                         |
| **Not scanned, by nature**                                                                                                      | `src/AppHost` (it pins the ports on purpose), `tests/**/*.cs` (gateway fixtures), `*.test.*` unit tests (origins as data), `docs/`, `specs/` |

## The allow-list (in `scripts/e2e-origins.mjs`)

```js
// Permanent: a fixed port is correct here, and the reason is the entry.
export const PERMANENT = [
  { file: 'scripts/wait-for-e2e-stack.sh', reason: 'readiness waits on the upstream dev servers the gateway proxies' },
  { file: 'apps/management-web/vite.config.ts', reason: 'the remotes’ own dev ports' },
  { file: 'apps/management-web/src/app/auth.ts', reason: 'runtime fallback when there is no window (not a browser)' },
];

// The ratchet: not yet moved. An entry whose occurrence no longer exists FAILS the test ("remove it"), so the list only shrinks.
export const MIGRATION = [
  { file: 'e2e/kiosk-comes-back.spec.ts', port: '5174', stage: 2 },
  // ... one entry per remaining occurrence; empty after stage 3
];
```

**Rules of the list**: a new `PERMANENT` entry needs a reason and is a reviewed change; a `MIGRATION` entry exists only for a stage that has
not run, carries that stage, and is deleted by it; after stage 3 the `MIGRATION` array is empty and the code that reads it is removed.
