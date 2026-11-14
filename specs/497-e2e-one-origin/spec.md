# Feature Specification: The end-to-end suite on the one origin

**Feature Branch**: `497-e2e-one-origin`

**Created**: 2026-11-14

**Status**: Draft, clarified (Q1 and Q2 answered by the owner, 2026-11-14: option A each); no markers remain

**Issue**: #3247, phase 3 of epic #3248. **Governing decision**: ADR-0192 (accepted), decision 7: the fixed dev ports keep working beside the one
URL, and "nothing cuts over until the end-to-end suite does (epic #3248, phase 3), which then decides whether the ports stay". **Builds on**:
specs 491 (#3253), 494 (#3266), 495 (#3269) and 496 (#3270), which put the console, the kiosk and the wall display at the gateway's address and, last
of all, made signing in there work. **Lane**: supervised (ADR-0037).

**Input**: User description: "Move the end-to-end suite and CI onto the one origin, and guard against hard-coded origins" (#3247).

**Latency budget (§IV): legs touched: none.** This changes where the test suite looks and what it forbids; no product code on the event-to-overlay
path, the media path or the presentation buffer is touched, and the hub, Keycloak and media stay direct (ADR-0192 decision 3).

## Premises checked against the tree and CI (2026-11-14, `origin/develop` at `01f2421f`)

| #   | Claim                                                                                  | Verdict                             | Evidence                                                                                                                                                                                                                                                                                                                                                                |
| --- | -------------------------------------------------------------------------------------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | The suite's projects use three origins                                                 | **True**                            | `playwright.config.ts`: default and `chromium`, `seed` and `cleanup` at `http://localhost:5173`; `kiosk` at `:5174`; `wall` at `:5175` (lines 22, 64, 70, 82, 106). The `seed` and `cleanup` projects drive the **console**, so they move with it.                                                                                                                      |
| 2   | "About 14 files" hard-code an origin                                                   | **Understated**                     | `localhost:517x` or `:5180` appears in **18 files under `e2e/`** (25 lines), in `playwright.config.ts` (5 lines) and in `scripts/wait-for-e2e-stack.sh` (the console `:5173`, the two remotes `:5176`/`:5177`, the kiosks `:5174`/`:5175`). Two of the e2e files are specs 495 and 496's own.                                                                           |
| 3   | The product source is clean of them                                                    | **Mostly**                          | `apps/*/src` has 9 files with 17 lines: unit-test data (`remotes.test.ts`, `signinPaths.test.ts`, …), the remotes' own dev ports in `apps/management-web/vite.config.ts`, and **one runtime fallback**, `auth.ts` (`'http://localhost:5173/'` when there is no `window`). `src/AppHost` pins the ports on purpose. Four `.cs` gateway tests carry origins as test data. |
| 4   | Many specs bypass the project's base URL                                               | **True**                            | 46 specs; **21** open their own `browser.newContext` or persistent context with an explicit absolute origin (a context does not inherit a project's `baseURL`); `page.goto('/')` appears 10 times, in the shared sign-in helpers and specs.                                                                                                                             |
| 5   | A base URL of `http://localhost:5180/kiosk/` would not put `goto('/')` under `/kiosk/` | **True by URL resolution, not run** | A leading `/` replaces the whole path; the kiosks are base-aware (`/kiosk/`, `/wall/`, spec 494), so a helper that assumes the root lands on the **console** at the one origin. Observed nowhere yet; FR-001 makes it an observation.                                                                                                                                   |
| 6   | The suite can sign in at the one origin                                                | **True, observed**                  | `e2e/one-origin-sign-in.spec.ts` (#3270): the console, the kiosk at `/kiosk/` and the wall display at `/wall/` pass in CI, about 2 s each, first attempt, on a fresh Keycloak volume. Before #3270 nothing could (#3267).                                                                                                                                               |
| 7   | CI's e2e lane has the gateway at the one origin                                        | **True**                            | The `EndToEndJob` AppHost lane (`PersistentStack=false`) gives `api-gateway` `Port=5180`, `Ui__Mode="Proxy"`, proxying the console, both remotes, the kiosk and the wall display.                                                                                                                                                                                       |
| 8   | The readiness script never exercises the one origin's UI paths                         | **True**                            | `scripts/wait-for-e2e-stack.sh` warms `:5173`, `:5174`, `:5175` and probes the gateway API. The first request to `:5180/kiosk/` or `/wall/` through the proxy (Vite's first-request transforms) therefore happens **inside a test**; `retries: 2` can hide a slow one.                                                                                                  |
| 9   | The routing regexes are unanchored                                                     | **True, and it has bitten**         | `testMatch`/`testIgnore` on `/(kiosk\|wall)-.*\.spec\.ts/` match anywhere in the path: a spec named `console-wall-…` was silently dropped in spec 495.                                                                                                                                                                                                                  |
| 10  | The picker shows its heading only when a layout is published                           | **True** (spec 496)                 | `PickerPage.tsx`: heading with layouts, "No layouts published yet." without; the `seed` project publishes one for the kiosk projects only.                                                                                                                                                                                                                              |
| 11  | Moving the apps onto one origin changes what they share in the browser                 | **True, and it is new**             | Today each app has its own origin, so its `localStorage`, `sessionStorage` and service state are its own. At `:5180` the console, the kiosk and the wall display **share one storage**; the OIDC entries are keyed by authority and client, but any key that is not (a redirect guard, a cached list) can now cross between apps in one browser context.                |

## User Scenarios & Testing _(mandatory)_

### User Story 1 - A green console suite means the one URL works (Priority: P1)

A developer or CI runs the end-to-end suite's console specs, and they run against the gateway's address, signed in there, through the shell, the
remotes and the API prefix, the way an operator uses the product.

**Why this priority**: The one-origin work was exercised signed out for three specs and broken for sign-in until #3270. As long as the suite runs
on the fixed ports, a green suite says nothing about the URL the product is meant to be used at.

**Independent Test**: Run the `chromium`, `seed` and `cleanup` projects at the one origin on a fresh stack; they pass.

**Acceptance Scenarios**:

1. **Given** the console specs at the one origin, **When** they run, **Then** they pass with no spec reaching for `:5173`.
2. **Given** the `seed` project, **When** it publishes the layouts the kiosk projects need, **Then** it does so through the console at the one origin.

---

### User Story 2 - The kiosk and wall suites run at their paths (Priority: P1)

The `kiosk` and `wall` projects open the kiosk at `/kiosk/` and the wall display at `/wall/` on the one origin, and every helper and spec that opens
a page of its own does too.

**Why this priority**: The kiosk and the wall display are where the product is judged: a wall that cannot sign in, or loads the console's page
because a helper assumed the root, stays blank unattended.

**Independent Test**: Run the `kiosk` and `wall` projects at the one origin on a fresh stack; they pass.

**Acceptance Scenarios**:

1. **Given** the `kiosk` project, **When** a spec signs in and opens a layout, **Then** it does so at `/kiosk/` and reaches the picker and the layout grid.
2. **Given** a spec that opens its own browser context, **When** it targets the wall display, **Then** the URL it uses is the one origin's `/wall/` and not `:5175`.
3. **Given** a helper that assumed the root, **When** it runs against the one origin unchanged, **Then** it fails visibly (this is the red the change starts from).

---

### User Story 3 - A hard-coded origin cannot creep back (Priority: P2)

A spec, a config or a script that hard-codes a `localhost:517x` origin outside a named dev-compatibility path fails a check naming the file and the
line.

**Why this priority**: This is the guard #3247 asks for, and the defect it prevents is exactly how #3267 hid: three specs and a file-reading guard
passed while the one origin was unused.

**Independent Test**: Add a hard-coded `http://localhost:5174` to a scratch copy of any spec; the check fails naming it.

**Acceptance Scenarios**:

1. **Given** the tree after this change, **When** the check runs, **Then** it passes with no hard-coded origin outside the named exceptions.
2. **Given** today's tree, **When** the check runs, **Then** it fails naming every hard-coded origin in the suite's reach (the red).

---

### User Story 4 - The first load through the gateway is warm before a test needs it (Priority: P2)

The readiness step requests the one origin's console, `/kiosk/` and `/wall/` through the gateway, so Vite's first-request work happens before the
first test and not inside it.

**Why this priority**: Spec 496's review: the first proxied module load is otherwise paid inside a 30 s test budget, where `retries: 2` can hide it.

**Independent Test**: The readiness script's output names the three paths and does not report ready until each answers.

**Acceptance Scenarios**:

1. **Given** a booted stack, **When** the readiness script runs, **Then** it requests the three paths through the gateway and fails with a message naming the one that did not answer.

---

### User Story 5 - The fixed ports keep working for developers (Priority: P3)

A developer who opens `:5173`, `:5174` or `:5175` still gets the app, signed in as before.

**Why this priority**: The fixed ports are a dev convenience and a decision for the owner (Q1); the default is that they stay.

**Independent Test**: Open each fixed port and sign in.

**Acceptance Scenarios**:

1. **Given** the change, **When** a developer opens a fixed port, **Then** sign-in works exactly as it does today.

---

### Edge Cases

- **Specs that build their own context** (21 of 46): each carries an explicit absolute origin; every one must move, and a few use a **persistent**
  context (a wall's process-death and lockout specs), whose stored grant now lives under the one origin.
- **Shared browser storage at one origin** (premise 11): a spec that runs the console, a kiosk and a wall display in **one** browser context now has
  one storage for all three. Specs that deliberately use separate contexts are unaffected; a spec that relied on origin isolation inside one context
  must be found and made explicit.
- **Helpers that assume the root** (`goto('/')` in 10 places): at `/kiosk/` and `/wall/` they must be given the app's path, or fail loudly.
- **The rate limiter**: the per-source limiter allows 100 requests a minute in production and 6000 in the dev stack and CI (ADR-0192 decision 8);
  moving every page load behind one address must not 429 a shard. Static and proxied UI assets do not pass the limiter.
- **The `seed` and `cleanup` projects** drive the console; if they move last, the kiosk projects depend on layouts published through a different
  origin than they now run on.
- **A spec whose name contains `kiosk-` or `wall-`** is routed to a kiosk project by the unanchored regexes (premise 9); moving the projects must not
  change which spec runs where, or must make the routing explicit.
- **The guard's own exceptions**: the AppHost pins the ports on purpose, the remotes' dev ports live in `vite.config.ts`, unit tests carry origins as
  data, and the gateway tests use them as fixtures. The guard must name its exceptions, not exempt a directory by guesswork.
- **A persistent developer stack** keeps the realm it was imported with; the one origin's redirect entries exist since #3270, so a stack older than
  that still fails at `:5180` (the docs already say to drop the volume).

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: Before anything moves, the suite MUST be observed failing at the one origin where its helpers still assume the root (ADR-0139): at least
  one kiosk or wall helper run against `:5180` unchanged, the failure quoted. Premise 5 is a prediction until then.
- **FR-002**: The `chromium`, `seed` and `cleanup` projects MUST run against the one origin, and no console spec MUST reference `:5173`.
- **FR-003**: The `kiosk` project MUST run at the one origin's `/kiosk/` and the `wall` project at its `/wall/`; every shared helper and every spec
  that opens a page or a browser context of its own MUST use those paths, not the root and not a fixed port.
- **FR-004**: A check MUST fail, naming the file and the line, when a spec, `playwright.config.ts` or a CI script hard-codes a `localhost:517x`
  origin outside a **named** dev-compatibility path, and it MUST be run in CI. On today's tree it MUST fail (the red); after this change it MUST pass.
- **FR-005**: The check's exceptions MUST be a written list with a reason each (the AppHost's pinned ports, the remotes' dev ports, unit-test data,
  the gateway tests' fixtures, the runtime fallback in `auth.ts`), and adding to the list MUST be a visible, reviewed change.
- **FR-006**: The readiness step MUST request the one origin's console, `/kiosk/` and `/wall/` through the gateway and MUST NOT report ready until
  each answers, naming the one that did not.
- **FR-007**: The change MUST NOT move the hub, Keycloak or media, MUST NOT edit product source other than what the guard's exception for `auth.ts`
  requires, and MUST NOT weaken, delete or skip a spec to reach green.
- **FR-008**: Which spec runs in which project MUST be unchanged by the move, or the routing MUST be made explicit; no spec may be silently dropped.
- **FR-009**: The owner's decision on the fixed ports (Q1, below) MUST be recorded where ADR-0192 decision 7 can be found, on the epic (#3248),
  after the last stage.
- **FR-010**: The suite MUST keep passing on a fresh stack in CI with `retries: 2`, and the change MUST report how many tests needed a retry, so a
  move that makes the suite flaky is seen and not absorbed.

_Answered by the owner, 2026-11-14:_

- **FR-011** (Q1, option A): The fixed ports 5173-5177 MUST stay working for developers, unchanged, after this feature; only the suite stops
  depending on them. The product's cross-origin dev mode, the per-port realm entries and the gateway's CORS entries for the UI ports are not
  touched. _(Options not chosen: B, remove the ports so the one URL is the only way in, a larger change that also removes the cross-origin dev mode
  and the per-port redirect entries; C, keep them but have the guard forbid them.)_
- **FR-012** (Q2, option A): The move MUST be delivered **in three stages, one PR each**: the console with the `seed` and `cleanup` projects, the
  readiness step and the guard first; then the `kiosk` project; then the `wall` project. The guard MUST carry a written allow-list of the files not yet
  moved, which shrinks with each stage and is empty after the last, so a file moved in one stage cannot regress in the next. Each stage MUST pass on a
  fresh stack in CI before the next begins. _(Option not chosen: all at once in one PR, with one 20-minute CI cycle per attempt and a failure in any
  project blocking the rest.)_

### Key Entities _(include if feature involves data)_

- **Project**: a Playwright project (`chromium`, `seed`, `kiosk`, `wall`, `cleanup`) with a base URL and a file-name routing rule.
- **The one origin**: the gateway's address, `http://localhost:5180` in dev and in CI's e2e lane, from which the console, the kiosk (`/kiosk/`) and the
  wall display (`/wall/`) are served (ADR-0192).
- **Dev-compatibility path**: a place where a fixed port is correct and named (the AppHost's pinned ports, the remotes' dev ports, unit-test data),
  as opposed to a place that merely happens to use one.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: After the change, **0** of the suite's specs, its config and its CI scripts reference `localhost:5173`, `:5174` or `:5175` outside the
  named exceptions (today 18 spec files, the config and the readiness script do).
- **SC-002**: The `chromium`, `seed`, `kiosk`, `wall` and `cleanup` projects pass at the one origin on a fresh stack in CI, with **0** specs dropped or
  skipped: the same number of tests run in each project as before the move.
- **SC-003**: The guard fails on today's tree naming every offending file and line, and passes after; a hard-coded origin added to a scratch spec makes
  it fail (observed).
- **SC-004**: The readiness step requests the **3** one-origin paths and fails naming one that does not answer (observed once by pointing it at a
  path that does not).
- **SC-005**: The number of tests that needed a retry in CI is recorded for the PR run and is not higher than on `develop`'s last green run
  (observed: 2 flaky of 157 in #3269's run, 0 of 162 in #3270's).

## Assumptions

- **The fixed ports stay** (Q1, answered A): this phase changes what the suite uses, not what the product serves.
- **Staged delivery** (Q2, answered A): this machine runs out of memory under a full stack, CI is the proof, and each stage is a cheap revert; the
  guard's allow-list is a ratchet that shrinks to empty. One spec, three PRs; every task and every PR names the stage it belongs to.
- **The hub, Keycloak and media stay direct** (ADR-0192 decision 3); the suite's hub and media assertions are unchanged.
- **No ADR is written.** Q1's outcome (the ports stay) is what ADR-0192 decision 7 already says; it is recorded on the epic (#3248) after the last
  stage.
- **Out of scope:** production's origin (#1015), the CSP and security headers on the HTML origin (#3252), the console's hub-failure notice (#3268), the
  `http://localhost:*` wildcard redirect, sign-out at the one origin, and the font-preload warning with a doubled base path.
