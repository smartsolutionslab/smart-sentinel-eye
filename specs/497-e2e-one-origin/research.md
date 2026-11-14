# Research: The end-to-end suite on the one origin

Every unknown in the plan's Technical Context was resolved by reading the tree and CI; none needed an outside source.

## R1 — Where do the hard-coded origins actually live, and do they partition by project?

**Decision**: stage by the origin a reference points at, not by project.
**Rationale**: `grep` over `e2e/` and `playwright.config.ts` finds 25 lines in 18 spec and support files plus 5 in the config. They do **not**
partition by project: `kiosk-*` specs open the console at `:5173` (`operatorContext`, `adminContext`), and `wall-*` specs open the console at `:5173`
and the kiosk at `:5174` (`wall-changes-its-scene`, `rule-switches-a-wall`). By origin: `:5173` in 10 spec lines over 9 files plus the config's default,
`seed` and `cleanup`; `:5174` in 8 lines over 7 files plus the `kiosk` project; `:5175` in 7 lines over 4 files plus the `wall` project; `:5180` in one
spec (496's). **Alternatives considered**: per-project stages as the owner worded Q2 (rejected: each PR would be half-moved and "no `:5173` anywhere"
could not be asserted); one PR (rejected by the owner).

## R2 — What does a path-bearing `baseURL` do to `goto('/')`?

**Decision**: keep `baseURL` at the origin root and use explicit path constants.
**Rationale**: URL resolution replaces the whole path for a leading `/`, so `new URL('/', 'http://localhost:5180/kiosk/')` is
`http://localhost:5180/`, which at the one origin is the console. There are 10 `goto('/')` sites: 5 console (`sign-in.ts`, `interaction-states`,
`operator-shell-federation`, `self-hosted-fonts`, `kiosk-reconciliation`'s `admin`) which stay correct, and 5 kiosk or wall
(`support/kiosk-session.ts`, `kiosk-comes-back`, `kiosk-touch-target`, `wall-outlives-its-session`, and the wall helper's default `url`) which must become
`KIOSK_PATH` or `WALL_PATH`. The kiosks themselves are base-aware since spec 494 (`/kiosk/`, `/wall/`), so no app change is needed.

## R3 — Is a `localhost:517x` search enough for the guard?

**Decision**: no; also flag bare port numbers.
**Rationale**: `kiosk-identity-refused.spec.ts:56` is `expect(new URL(page.url()).port, …).toBe('5174')`, a hard-coded origin with no `localhost`.
A scan for `localhost:517x` reports 25 lines and misses it. The scanner flags a standalone `5173`-`5177` literal in the scanned files and the
one-origin `5180` anywhere except `origins.ts`.

## R4 — What is the repo's idiom for a guard, and where does it run?

**Decision**: `scripts/<name>.mjs` plus `scripts/<name>.test.mjs`, run by `pnpm test:guards`.
**Rationale**: `scripts/` already holds a dozen such pairs (`singleton-versions`, `node-types-alignment`, `e2e-assertion-inventory`,
`scrub-playwright-artifacts`, …); `package.json` runs `node --test "scripts/**/*.test.mjs" "e2e/support/**/*.test.mjs"`, and the frontend CI job runs it,
so the guard needs no new CI step. The existing "e2e shard coverage guard" is a CI job that diffs `playwright test --list` against the shards; a
scan of source files fits the `scripts/` idiom better than a job.

## R5 — Does the e2e lane give the one origin to every project?

**Decision**: yes.
**Rationale**: the `EndToEndJob` composition snapshot has `api-gateway` on `Port=5180`, `Ui__Mode="Proxy"`, proxying the console, both remotes,
the kiosk and the wall display; the lane boots with `PersistentStack=false`, so a fresh Keycloak imports the realm that names `:5180` (#3270). CI run
43509251327 passed the three sign-in tests there in about 2 s each, on the first attempt.

## R6 — What does the readiness script wait on, and what must stay?

**Decision**: keep the upstream waits as named constants; add a one-origin warm-up.
**Rationale**: the script waits on `:5173` (the console's dev server), `:5176` and `:5177` (the two remotes) and `:5174`, `:5175` (the kiosks), then
probes the gateway API; it never requests the one origin's UI paths, so Vite's first-request transforms run inside a test, where `retries: 2` can hide
them (spec 496's review). The upstream waits are legitimately about the **upstream** dev servers the gateway proxies, so they stay as named
exceptions; the warm-up is the new, separately testable part.

## R7 — Which specs derive their URLs from a request and so may not care?

**Decision**: no change; watch them in stage 1's CI run.
**Rationale**: eleven specs read `new URL((await request).url()).origin` and call REST on it (`overlays`, `overlay-colour`, `kiosk-latency-contract`,
…). At `:5173` that is the dev server's proxy (`VITE_API_GATEWAY_URL=/api`, spec 491); at `:5180` it is the gateway, which keeps the bare
`/<context>/...` routes beside `/api` (ADR-0192 decision 2). Not run at `:5180` yet.

## R8 — Is anything in the suite sensitive to three apps sharing one browser storage?

**Decision**: audit by reading, per stage; fix by giving the second app its own context.
**Rationale**: at `:5180` the console, the kiosk and the wall display share `localStorage` and `sessionStorage`; the OIDC entries are keyed by authority
and client id (`oidc.user:<authority>:<client>`), so they do not collide, but any key that is not (a redirect guard in `sessionStorage`, a cached
list) can. The specs open separate contexts for separate apps (`browser.newContext`, 21 of 46 specs), so none should share storage, but that is a property of
the code, not of anything checked. No automatic search finds "two apps in one context"; the stage's read-through is the check.
