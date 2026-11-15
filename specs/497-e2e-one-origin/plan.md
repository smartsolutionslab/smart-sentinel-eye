# Implementation Plan: The end-to-end suite on the one origin

**Branch**: `497-e2e-one-origin` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/497-e2e-one-origin/spec.md`. **Issue**: #3247 (epic #3248). **Governing ADR**: ADR-0192 decision 7.
**Owner's answers**: the fixed ports stay (Q1 A); delivered in stages, one PR each (Q2 A). **Lane**: supervised (ADR-0037); no ADR is written.

## Summary

Define the one origin **once**, in a small module the suite imports (`e2e/support/origins.ts`), and move every reference to a fixed UI port onto it,
in three stages. Add a **guard** that fails the frontend CI job when a spec, the Playwright config or a CI script hard-codes a fixed UI port, with
a written allow-list that is a ratchet: it shrinks with each stage and is empty of migration entries after the last. Add a testable **warm-up
script** so the first proxied load of the console, `/kiosk/` and `/wall/` happens before a test, not inside one. No product source changes, no
change to which spec runs in which project.

| Stage | Moves every reference **to**                         | Files (references)                                                                                                                                                                                                                                                                                                                                                                                                          | Also in this PR                                                                                                                   |
| ----- | ---------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| 1     | the **console**: `:5173` (and the `:5180` literal)   | `playwright.config.ts` (default, `seed`, `cleanup`), `console-live-scene-switch`, `rule-switches-a-wall`, `wall-changes-its-scene`, `kiosk-overlay-tile-view-matches-the-wall`, `kiosk-paints-a-reordered-overlay`, `kiosk-reconciliation`, `kiosk-shows-a-label-over-video`, `kiosk-shows-shapes-over-video`, `kiosk-shows-two-labels-over-video`, `one-origin-sign-in` (10 lines in 9 files, the config's 3, one literal) | `origins.ts`; the guard and its test; the warm-up script and its test; the readiness step calls it; allow-list for stages 2 and 3 |
| 2     | the **kiosk**: `:5174`, `goto('/')` on a kiosk       | `playwright.config.ts` (`kiosk`), `kiosk-comes-back`, `kiosk-identity-herd`, `kiosk-touch-target`, `kiosk-identity-refused` (a bare `'5174'` **port assertion**), `rule-switches-a-wall`, `spanning-wall`, `wall-changes-its-scene` (×2), `support/kiosk-session.ts`                                                                                                                                                        | allow-list loses the kiosk entries                                                                                                |
| 3     | the **wall display**: `:5175`, `goto('/')` on a wall | `playwright.config.ts` (`wall`), `wall-outlives-its-session` (base and `goto('/')`), `wall-survives-a-lockout`, `wall-survives-a-process-death`, `wall-withdrawal` (×4), `support/wall-session.ts` (default `url`)                                                                                                                                                                                                          | allow-list loses the wall entries and its migration section is deleted; the epic gets the Q1 outcome                              |

## Technical Context

**Language/Version**: TypeScript (Playwright, Node 22), Node ESM for the guard, bash for the readiness script.

**Primary Dependencies**: `@playwright/test`, `node:test`; no new package.

**Storage**: N/A. **Testing**: `node --test` guards (run by `pnpm test:guards` in the frontend CI job); the Playwright suite as the proof of the move.

**Target Platform**: CI's full-stack e2e job (`EndToEndJob` lane: gateway `Port=5180`, `Ui__Mode="Proxy"`, fresh Keycloak) and the dev stack.

**Project Type**: test infrastructure.

**Performance Goals**: none; SC-005 records retries. **Constraints**: no spec weakened, deleted or re-routed; §IV untouched; **local runs are small by
necessity** (a full stack on this machine has twice exhausted memory), so **CI is the proof of every stage** and local runs are single specs on one worker.

**Scale/Scope**: about 20 files over three PRs, one new support module, one guard, one warm-up script.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design._

| Principle                                 | Verdict     | Note                                                                                                                                            |
| ----------------------------------------- | ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| I. On-prem first                          | Passes      | No new dependency.                                                                                                                              |
| II. Value objects                         | Not engaged | No domain model.                                                                                                                                |
| III. Bounded-context isolation            | Passes      | No project reference changes.                                                                                                                   |
| IV. Latency budget                        | Passes      | **Legs touched: none.** No product code moves; the hub, Keycloak and media stay direct.                                                         |
| V. Spec-driven                            | Passes      | Spec 497 → this plan → tasks; every task and PR names its stage and #3247.                                                                      |
| VI. Aspire is the composition root        | Passes      | `src/AppHost` is untouched (its pinned ports are an exception, not an edit).                                                                    |
| VII. Observability                        | Not engaged | No leg is added or moved.                                                                                                                       |
| VIII. Safe by default at trust boundaries | Passes      | No credential, origin list or scope changes; the realm and the gateway CORS list are untouched (Q1 A).                                          |
| Testing (§Testing, ADR-0139)              | Passes      | The guard and the warm-up script are new behaviour, **red first**. The suite move is a behaviour-preserving move: **characterisation** (below). |

No violations; Complexity Tracking is empty.

## Decisions

1. **Stage by the origin a reference points at, not by project.** _This refines the owner's Q2 wording ("console with `seed`/`cleanup`, then the
   `kiosk` project, then the `wall` project"), and the substance, console then kiosk then wall, is kept._ The references cut across projects: `kiosk-*`
   specs open the **console** at `:5173` in a second context, and `wall-*` specs open the console at `:5173` and the kiosk at `:5174`. A per-project
   stage would leave each PR half-moved and the ratchet unable to say "no `:5173` anywhere". Per-origin stages give each PR one clean claim: after
   stage 1 nothing in the suite names `:5173`, after 2 nothing names `:5174`, after 3 nothing names `:5175`.
2. **The base URL stays the origin root; the kiosk and wall paths are explicit constants.** A `baseURL` with a path (`http://localhost:5180/kiosk/`)
   makes `goto('/')` resolve to the **root** (a leading `/` replaces the path), which lands on the console, and is exactly the trap spec 497 names.
   `origins.ts` exports `ONE_ORIGIN`, `KIOSK_PATH = '/kiosk/'` and `WALL_PATH = '/wall/'`; helpers and specs `goto(KIOSK_PATH)` or `goto(WALL_PATH)`.
   After stage 3 every project's base URL is the same value and the projects differ only by routing and dependencies (left as they are, FR-008).
3. **One definition, no environment knob.** `ONE_ORIGIN` is a constant (`http://localhost:5180`), not read from the environment: nothing needs a
   second value today, and a knob would be generality for a need that does not exist (CLAUDE.md). `playwright.config.ts` imports it.
4. **The guard is a `node:test` check over the real tree**, `scripts/e2e-origins.mjs` plus `scripts/e2e-origins.test.mjs`, picked up by the existing
   `pnpm test:guards` glob and so run by the frontend CI job; no new CI step. The test (a) proves the scanner on **fixtures**, including the
   counterfactual (a scratch file with a hard-coded origin is reported, an allow-listed one is not), and (b) scans the **real** tree and fails
   naming each `file:line`. It scans `e2e/**`, `playwright.config.ts`, `scripts/*.sh` and `.github/workflows/*.yml`, and, for runtime hard-coding,
   non-test application source (`apps/*/src/**` excluding `*.test.*`, and `vite.config.ts`).
5. **The guard catches bare port numbers, not only `localhost:517x`.** `kiosk-identity-refused.spec.ts:56` asserts `new URL(page.url()).port` is
   `'5174'`; a search for `localhost:517x` misses it. The scanner flags `localhost:<port>` and a standalone `5173`-`5177` literal in the scanned
   files; the one-origin value `5180` is flagged anywhere except `origins.ts` (so specs 495 and 496's literals move too).
6. **The allow-list has two sections, and only one may shrink to nothing.** _Permanent exceptions_, each with a reason: the readiness script's
   waits on the **upstream** dev servers the gateway proxies (waiting on the upstream is how readiness is known); `apps/management-web/vite.config.ts`
   (the remotes' own dev ports); the runtime fallback in `apps/management-web/src/app/auth.ts`. _Migration entries_ (`file`, `port`, `stage`): the
   ratchet. A migration entry whose occurrence **no longer exists** fails the test ("remove it"), so the list can only shrink and a moved file cannot be
   left on it. Not scanned, by nature, and said so in the module: `src/AppHost` (it pins the ports on purpose), `tests/**/*.cs` (gateway fixtures)
   and `*.test.*` unit tests (origins as data).
7. **The port assertion becomes an origin assertion with the same meaning** (stage 2). `kiosk-identity-refused`'s check is "a refused screen is not
   redirected to the identity provider", so it asserts `new URL(page.url()).origin` equals `ONE_ORIGIN`. This edits an assertion; it is
   called out in the PR because an edited assertion is evidence behaviour may have moved: here the property asserted (not on the provider's
   origin) is unchanged and is stronger than a port comparison, and the stage's red run shows the old form failing at the one origin.
8. **The warm-up is a section of the readiness script, tested the way that script already is.** `wait-for-e2e-stack.sh` gains a section, beside the
   `:5176` and `:5177` ones, that requests the one origin's `/`, `/kiosk/` and `/wall/` through the gateway (bounded retries) and fails with a message
   **naming the path that did not answer**. `scripts/wait-for-e2e-stack.test.mjs` already tests that script with a **stub `curl`** (no servers) and has
   the matching cases for `:5176` and `:5177`; the new case is a `curl` stub that fails only on `/wall/`: red first (no section), green after, with no
   stack needed. _(Corrected during T001: this decision first said a separate `warm-one-origin.sh` with its own stub-server test; the existing test
   idiom is simpler and adds no file.)_ The new section must keep the existing cases green, which means the existing `curl` stubs must answer the
   one-origin URLs, and that is checked.
9. **Which spec runs where is unchanged and checked cheaply.** `playwright test --list --project=<each>` needs no stack, so the verification note
   records the per-project test list before and after each stage; the diff must be empty (FR-008, SC-002), which also catches the unanchored-regex
   trap.
10. **Origin-derived URLs need no change.** Eleven specs derive the gateway's origin from a page request (`new URL(req.url()).origin`) and call REST
    on it; at `:5173` that origin is the dev server's own proxy, at `:5180` it is the gateway, which keeps the bare `/<context>/...` routes (ADR-0192
    decision 2). This is a risk to watch in the stage-1 CI run, not a change to make.

## Phase 4a: which colour (ADR-0139 / ADR-0144)

| Behaviour                                                                            | Colour                                                                                                                                       | Evidence                                                                                                                                                                                                                       |
| ------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| The guard flags hard-coded origins (FR-004, FR-005, SC-003)                          | **Red first.** New behaviour: the scanner test fails first (no module), then the **real-tree** test fails on today's tree naming every line  | Stage 1: quote the real-tree failure listing the offending `file:line` (31 lines in `e2e/` and the config today: 25 + 5 + the bare `'5174'`; plus the readiness script's); then green with the allow-list                      |
| The scanner's counterfactual (SC-003)                                                | **Red first**, fixtures                                                                                                                      | A fixture with `http://localhost:5174` is reported; a bare `toBe('5174')` is reported; an allow-listed entry is not; a stale migration entry fails                                                                             |
| The warm-up names the missing path (FR-006, SC-004)                                  | **Red first**, stub `curl`                                                                                                                   | No script: red. With a stub answering 404 for `/wall/`: fails naming `/wall/`; with all three answering: passes                                                                                                                |
| The suite runs at the one origin (FR-002, FR-003, SC-002)                            | **Characterisation** (a behaviour-preserving move), observed green on `develop` in CI **before**, green **after** with assertions unmodified | CI baseline of the last green `develop` run; the stage's CI run; `--list` per project identical; the one edited assertion (decision 7) is declared                                                                             |
| A kiosk or wall helper assuming the root fails at the one origin (FR-001, premise 5) | **Red first**                                                                                                                                | Cheap and deterministic: `new URL('/', 'http://localhost:5180/kiosk/')` resolves to the root (quoted, no stack). At stage 2, one kiosk spec run against the one origin with the helper unchanged on a booted stack, one worker |
| The fixed ports still work for developers (FR-011)                                   | **Characterisation**                                                                                                                         | Untouched by construction (no product source, realm or AppHost change); `git diff --stat` shows no file outside `e2e/`, `scripts/`, `playwright.config.ts`, `specs/`                                                           |

A red that arrives green is a phase-4 failure: stop and report. The **sign-in** behaviour needs no new proof: it is `one-origin-sign-in.spec.ts`'s,
green in CI since #3270.

## Project Structure

### Documentation (this feature)

```text
specs/497-e2e-one-origin/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── quickstart.md        # Phase 1
├── contracts/
│   └── one-origin-module.md
└── tasks.md             # /speckit-tasks, not this command
```

No `data-model.md`: the only entities are the spec's Project and the one origin, both in the contract.

### Source Code (repository root)

```text
e2e/support/origins.ts                 # new (stage 1): ONE_ORIGIN, KIOSK_PATH, WALL_PATH
scripts/e2e-origins.mjs                # new (stage 1): scanner + allow-list
scripts/e2e-origins.test.mjs           # new (stage 1): fixtures, real-tree, ratchet
scripts/wait-for-e2e-stack.test.mjs    # stage 1: a case for the warm-up, with a stub curl
scripts/wait-for-e2e-stack.sh          # stage 1: call the warm-up; upstream waits become named constants
playwright.config.ts                   # one base URL per stage
e2e/*.spec.ts, e2e/support/*.ts        # the references above, by stage
```

**Structure Decision**: no product source, no AppHost, realm, gateway or `apps/**` edit. The guard's scan of `apps/**` is read-only.

## Risks and what would change the plan

- **Stage 1 is not as small as it looks in CI.** Moving the console project to `:5180` runs every console spec through the gateway and the proxied
  remotes for the first time; the only prior signed-in evidence is three sign-in tests. A failure there is a **finding about the product's one URL**
  (a remote's base path, a bare route, the rate limiter), not something to route around in the suite; it is diagnosed and recorded, and if it needs
  product code, it becomes its own spec and this stage waits.
- **One browser, three apps, one storage** (spec premise 11). Specs use separate contexts for separate apps, so none should share storage, but that is
  a property of 46 files nobody has audited for it. Each stage includes a read-through of its specs for a context that opens two apps; a hit is fixed
  by giving the second app its own context.
- **Local verification is small by design.** A full stack exhausted this machine's memory twice, so each stage is proved by CI, with local runs
  limited to the guard, the warm-up test, `--list` and one or two specs on one worker; the PR says so rather than implying more.
- **`retries: 2` can absorb a move that makes the suite flaky.** Each stage's PR reads the retry summary and compares it with `develop`'s last run
  (SC-005).
- **A persistent developer stack** that predates #3270 still fails sign-in at `:5180`; the docs already say to drop its volume.
