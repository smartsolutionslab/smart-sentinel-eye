# Verification — spec 491 (#3245)

Work in progress; written up as each task is observed. §IV legs touched: **none**.

## T001 — baseline (before any change)

Branch `491-gateway-single-origin-shell` cut from `develop`, lockfile-true install (`pnpm install --frozen-lockfile`), `jsdom` read from
`node_modules` in each app: 30.1.3. One vitest at a time, vitest's JSON reporter, 0 skipped, 0 todo.

| App                          | Files | Passed | Failed | Noise blocks |
| ---------------------------- | ----- | ------ | ------ | ------------ |
| `management-event-ingestion` | 4     | 27     | 0      | 2            |
| `management-cameras`         | 24    | 151    | 0      | 47           |
| `management-web`             | 102   | 740    | 0      | 846          |

`Architecture.Tests` baseline is captured with T004 (before and after the extraction).

## T002 — admin

- #3245 and epic #3248 are on Project #13 (status Todo).
- Content-Security-Policy and security-headers follow-up filed as **#3252**, on Project #13, linked from #3248.
- #3245 commented: the CORS list does not shrink in this phase (ADR-0192 decisions 6 and 7).
- Port 5180 for the dev address: no `.cs`, `.ts`, `.tsx`, `.json`, `.yml`, `.md`, `.mjs` or `.props` file names it (build output and
  `node_modules` excluded), and nothing listened on it on 2026-11-11.
