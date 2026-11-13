# Implementation Plan: Sign-in at the one origin

**Branch**: `496-one-origin-sign-in` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/496-one-origin-sign-in/spec.md`. **Issue**: #3267 (epic #3248). **Governing ADRs**: ADR-0192
decision 6 (the realm clients accept the new origin) and decision 7 (the fixed ports keep working); ADR-0131 (a realm edit reaches only a fresh
import). **Lane**: supervised (ADR-0037); no ADR is written.

## Summary

Name the one origin, `http://localhost:5180/*`, in the `redirectUris` of the three browser clients, which is all the `"+"` web-origin setting
needs to admit it. Extend the architecture guard that already documents this mechanism so it covers all three clients and both origins. Add one
Playwright spec that signs in at `:5180` as each of the three apps against the real Keycloak, because **a guard that reads the realm file proves the
design was written down, not that it holds** (this defect passed three specs and a file guard). One documentation bullet. **No frontend, gateway,
AppHost or hub change.**

| Change                                                                                                                                    | Where                                                                    |
| ----------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| Add `"http://localhost:5180/*"` to `redirectUris` of `management-web`, `kiosk-web` and `kiosk-wall` (leave `webOrigins` and the wildcard) | `src/AppHost/Realms/smart-sentinel-eye-realm.json` (3 lines)             |
| Cover all three clients and both origins                                                                                                  | `tests/Architecture.Tests/WallClientDeclarationTests.cs`                 |
| Sign in at `:5180` as the console, the kiosk and the wall display; fail on "Sign-in failed"                                               | `e2e/one-origin-sign-in.spec.ts` (new)                                   |
| Say what a persistent stack must do, and that the realm file reaches only a fresh import                                                  | `docs/deployment-frontend-env.md` (one bullet in the one-origin section) |

## Technical Context

**Language/Version**: JSON (realm), C# on .NET 10 (guard), TypeScript (Playwright spec).

**Primary Dependencies**: Keycloak (imports the realm into an empty volume), the existing Playwright projects.

**Storage**: N/A.

**Testing**: xUnit + Shouldly for the declaration guard; Playwright for the real sign-in.

**Target Platform**: the Aspire dev stack and CI's full-stack e2e job, whose AppHost lane (`EndToEndJob`) puts the gateway on `5180` in proxy
mode (read from the lane's snapshot: `Port=5180`, `Ui__Mode="Proxy"`), so the one origin exists there.

**Project Type**: web application composition; identity configuration.

**Performance Goals**: none. **Constraints**: §IV untouched; Keycloak stays direct (ADR-0192 decision 3).

**Scale/Scope**: three clients, one new origin.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design._

| Principle                                 | Verdict     | Note                                                                                                                                                         |
| ----------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| I. On-prem first                          | Passes      | A dev redirect entry; nothing external.                                                                                                                      |
| II. Value objects                         | Not engaged | No domain model is touched.                                                                                                                                  |
| III. Bounded-context isolation            | Passes      | The guard reads a file under `AppHost`; no project reference is added.                                                                                       |
| IV. Latency budget                        | Passes      | **Legs touched: none.** Sign-in precedes any live page.                                                                                                      |
| V. Spec-driven                            | Passes      | Spec 496 → this plan → tasks; every task references #3267.                                                                                                   |
| VI. Aspire is the composition root        | Passes      | No wiring changes; the realm file is already the AppHost's `WithRealmImport` input.                                                                          |
| VII. Observability                        | Not engaged | No leg is added or moved.                                                                                                                                    |
| VIII. Safe by default at trust boundaries | Passes      | The new entry is one **exact** origin; `webOrigins` stays `["+"]` (no wildcard added); the counterfactual (an unlisted origin is still blocked) is observed. |
| Testing (§Testing, ADR-0139)              | Passes      | Behaviour-changing, so **red** (below).                                                                                                                      |

No violations; Complexity Tracking is empty.

## Decisions

1. **One entry per client, `http://localhost:5180/*`.** It matches the console's return (`/`), the kiosk's (`/kiosk/oidc/callback`) and the
   wall display's (`/wall/oidc/callback`), whose paths are `${origin}${base}oidc/callback` in `apps/kiosk-web/src/app/signinPaths.ts`. A
   path-specific entry per app would be exact but three different strings for one fact; the per-port entries already use `/*`.
2. **`webOrigins` stays `["+"]`.** The origin is derived from the explicit entry. This is the mechanism `WallClientDeclarationTests` documents,
   and the minimal change. The wildcard `http://localhost:*` stays (spec Assumptions).
3. **The guard asserts the origin, not the string.** The existing theory checks `uri.Contains("localhost:<port>")`. The three new cases (`5180`
   per client) and the missing console `5173` case reuse that exact assertion and its failure message, so the guard stays one rule with more rows.
   The one-origin port is a named constant in the test beside `KioskClient` and `WallClient`.
4. **The real guard is a browser.** `e2e/one-origin-sign-in.spec.ts` drives the real Keycloak form at `http://localhost:5180/`, `/kiosk/` and
   `/wall/`, and asserts each app's first screen **and** that "Sign-in failed" is absent. It lives in the `chromium` project (the file name
   contains neither `kiosk-` nor `wall-`; the config's patterns are unanchored, which silently dropped a spec in spec 495). It signs in inline with
   explicit absolute URLs and does **not** edit `signInToKiosk` or `signInAsWallDisplay`, so no existing spec's behaviour can move (FR-007).
5. **First-screen assertions that do not need seed data.** The console asserts the _Cameras_ heading. The kiosk and the wall display land on
   the layout picker, which shows its "Pick a layout" heading **only when a layout is published** and says "No layouts published yet." otherwise
   (`PickerPage.tsx`); the `seed` project that publishes one runs for the `kiosk` and `wall` projects, not for this spec. So the spec accepts the
   picker in either rendering. _(Corrected during T007: this decision first said "assert the heading only", which was wrong, and the first run
   failed for the kiosk and the wall display for that reason; see `verification.md`.)_
6. **FR-008 (an unlisted origin is still blocked) is observed with `curl`, not added as a test.** Keycloak answers a token request with
   `Access-Control-Allow-Origin` only for an origin the _client_ allows, so `POST …/token` with `client_id=management-web` and a bogus code, from
   `Origin: http://localhost:5180` versus `http://localhost:5199`, shows the difference without a browser. Recorded in the verification note.
7. **No shared helper changes and no new Playwright project.** Fewer moving parts; #3247 will move the whole suite to the one origin and can
   fold this spec into that move.
8. **Documentation (FR-010).** One bullet in the one-origin section of `docs/deployment-frontend-env.md`: the three clients must name the one
   origin; the realm file reaches only a fresh import (ADR-0131), so a persistent stack drops its `keycloak-data` volume or applies the change
   through the admin API and says which it tested.

## Phase 4a: which colour (ADR-0139 / ADR-0144)

**Behaviour-changing → red**, with the one characterisation case declared so it is not mistaken for a failure of the rule.

| Behaviour                                            | Test                                                                                                                                            | Expected on the current tree                                                                                |
| ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Each client names the one origin (FR-004, FR-005)    | `WallClientDeclarationTests.A_client_names_the_origin_it_is_served_from`, three new rows (`management-web`, `kiosk-web`, `kiosk-wall` × `5180`) | **Red** ×3: the clients do not name `5180`.                                                                 |
| The console names its own port (FR-005, premise 4)   | The same theory, a fourth new row (`management-web` × `5173`)                                                                                   | **Green before and after, by declaration**: characterisation; closes a coverage hole, changes no behaviour. |
| Sign-in works at the one origin (FR-001–003, FR-006) | `e2e/one-origin-sign-in.spec.ts`, three tests                                                                                                   | **Red** ×3 on a fresh stack: "Sign-in failed — Failed to fetch", the token request blocked by CORS.         |
| The fixed ports are unchanged (FR-007)               | The existing `chromium`, `kiosk` and `wall` projects, **unmodified**                                                                            | Green before and after (characterisation).                                                                  |
| An unlisted origin is still refused (FR-008)         | `curl` observation (decision 6)                                                                                                                 | Blocked before and after.                                                                                   |

The red browser run **must be on a fresh Keycloak volume** at this branch's pre-change tree, because the realm is imported only into an empty
volume (ADR-0131): `PersistentStack=false` gives that. The first task records the failure for all three apps; if the kiosk or the wall display
does **not** fail, premise 5 is false and the spec is narrowed before anything else.

## Project Structure

### Documentation (this feature)

```text
specs/496-one-origin-sign-in/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── quickstart.md        # Phase 1
├── contracts/
│   └── one-origin-clients.md
└── tasks.md             # /speckit-tasks, not this command
```

No `data-model.md`: the only data is the realm file's existing client shape.

### Source Code (repository root)

```text
src/AppHost/Realms/smart-sentinel-eye-realm.json        # +1 redirect URI on each of 3 clients
tests/Architecture.Tests/WallClientDeclarationTests.cs  # theory rows + one constant
e2e/one-origin-sign-in.spec.ts                          # new
docs/deployment-frontend-env.md                         # one bullet
```

**Structure Decision**: no new project, folder or abstraction. `apps/**` and `src/ApiGateway` are read, not edited.

## Risks and what would change the plan

- **A second defect behind the CORS one.** Nothing has ever signed in at `:5180`. Once the token request is admitted, the kiosk or the wall
  display might fail on something else (the base path under the gateway's dev proxy, a redirect that lands outside `/kiosk/`, an OIDC `iss`
  mismatch). If the green run does not reach the picker, the cause is **diagnosed and recorded**, and the plan goes back to the spec rather than
  around it; widening this change to fix an unrelated app defect is a separate spec.
- **The wall display's long-lived grant.** `kiosk-wall` requests `offline_access`; the spec's User Story 2 requires it to work at the one origin.
  The browser spec asserts arrival at the picker, not the grant; if the grant fails at `/wall/` that is a finding, not something this spec's
  assertion would catch. `wall-outlives-its-session` covers the grant at `:5175` and stays unmodified.
- **A persistent stack.** A developer whose Keycloak volume predates this change stays broken and looks healthy (spec edge case 1). The
  documentation bullet and the PR body say so; nothing automates a re-import.
- **CI runs the new spec at `:5180` for the first time in CI.** The e2e lane's gateway is in proxy mode, which is what spec 494's
  verification could not walk; `retries: 2` can hide flakiness, so the PR reads the retry summary.
