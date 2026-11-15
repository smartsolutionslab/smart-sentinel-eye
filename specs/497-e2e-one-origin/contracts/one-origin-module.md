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

| Rule                                                                                                                                                                                   | Scanned files                                                                                                                       |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| Flag `localhost:<port>` for `5173`-`5177` and `5180`, and a **standalone** numeric literal `5173`-`5177`, reporting `file:line`; a line that is only a comment is prose and is skipped | `e2e/**`, `playwright.config.ts`, `scripts/*.{sh,mjs}`, `.github/workflows/*.yml`                                                   |
| Flag the same in non-test application source, for runtime hard-coding                                                                                                                  | `apps/*/src/**` excluding `*.test.*` and `*.spec.*`; `apps/*/vite.config.ts`                                                        |
| Allow `5180` in `e2e/support/origins.ts` only                                                                                                                                          | (the one definition)                                                                                                                |
| **Not scanned, by nature**                                                                                                                                                             | `src/AppHost` (it pins the ports on purpose), `tests/**/*.cs` (gateway fixtures), `## The allow-list (in `scripts/e2e-origins.mjs`) |

```js
// Permanent: a fixed port is correct here, and the reason is the entry. With `match`, only the lines containing it are exempt; without it, the
// whole file. An entry whose file no longer names a port is reported as stale.
export const PERMANENT = [
  {
    file: 'scripts/wait-for-e2e-stack.sh',
    reason: 'readiness names the stack’s ports: the upstream dev servers, and the one origin it gates',
  },
  {
    file: 'apps/kiosk-web/src/app/auth.ts',
    match: "typeof window !== 'undefined'",
    reason: 'the redirect_uri fallback when there is no window',
  },
  { file: 'scripts/e2e-origins.mjs', reason: 'this guard names the ports it forbids' },
  // ... the four apps' vite.config.ts (a dev server's own port) and management-web's auth.ts fallback
];
```

**Rules of the list**: a new `PERMANENT` entry needs a reason and is a reviewed change; an entry whose file no longer names a port is stale and fails the test, so
the list can only be exact.

**History.** During stages 1 and 2 a second list, `MIGRATION`, held the references not yet moved (one entry per file and port, with a count; a new occurrence in
a listed file was a violation, and an entry with fewer occurrences than its count was stale), so the list and the counts could only shrink. **It was deleted in
stage 3, with the code and the five test cases that read it, when it reached zero.** If a later move needs a ratchet again, `git log -- scripts/e2e-origins.mjs`
shows how it was built and tested.

**Known limits of the scanner** (probed in review; none hides an occurrence in today's tree): a bash brace range (`{5174..5175}`), a hyphenated range
(`'5173-5177'`), a port followed by a dot, a JS private field at the start of a line, and a line that starts with `/* … */` and then has code are not
seen. The pattern is deliberately not matched inside an identifier, a UUID, a version or a longer number.
