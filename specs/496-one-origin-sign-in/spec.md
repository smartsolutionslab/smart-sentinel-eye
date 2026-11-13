# Feature Specification: Sign-in at the one origin

**Feature Branch**: `496-one-origin-sign-in`

**Created**: 2026-11-13

**Status**: Draft; no `[NEEDS CLARIFICATION]` markers (every open choice has a stated default in the Assumptions)

**Issue**: #3267, a follow-up of epic #3248. **Governing decision**: ADR-0192 (accepted), decision 6 (the realm clients accept the new origin) and
decision 7 (the fixed dev ports keep working beside the one URL). **Builds on**: specs 491 (#3253), 494 (#3266) and 495 (#3269), which put the
console, the kiosk and the wall display at the gateway's address and **never signed in there**. **Lane**: supervised (ADR-0037).

**Input**: User description: "Make sign-in work at the one origin (the gateway's address, `http://localhost:5180` in dev) for all three browser
apps: the operator console, the kiosk (`/kiosk/`) and the wall display (`/wall/`)."

**Latency budget (§IV): legs touched: none.** Sign-in happens before any page is live; it is not on the event-to-overlay path, the media path
or the presentation buffer, and no leg gains a hop. Keycloak stays direct (ADR-0192 decision 3).

## Premises checked against the tree and the running stack (2026-11-13, `origin/develop` at `ea26b549`)

| #   | Claim                                                                                       | Verdict                           | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                 |
| --- | ------------------------------------------------------------------------------------------- | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1   | Signing in at `:5180` fails; at `:5173` it works                                            | **True, observed** (console only) | Spec 495 verification: after the Keycloak form the app returns to `http://localhost:5180/?code=…` and shows "Sign-in failed — Failed to fetch". A Chromium capture logs `POST …/protocol/openid-connect/token` from origin `http://localhost:5180` blocked by CORS (no `Access-Control-Allow-Origin`). The same flow at `:5173` lands on _Cameras_. **The kiosk and the wall display were not observed**; see premise 5. |
| 2   | The three browser clients name only their own port                                          | **True**                          | `src/AppHost/Realms/smart-sentinel-eye-realm.json`: `management-web` `redirectUris` `["http://localhost:5173/*","http://localhost:*"]`, `kiosk-web` `…5174…`, `kiosk-wall` `…5175…`; all three `webOrigins: ["+"]`. No client names `:5180`.                                                                                                                                                                             |
| 3   | `"+"` derives allowed origins from the _explicit_ redirect URIs, and a wildcard yields none | **True, already documented here** | `WallClientDeclarationTests.A_client_names_the_origin_it_is_served_from`: "a client whose redirect URIs never name its own port has no allowed origin … the token exchange is then blocked by CORS … A wildcard entry was present and did not save it." The wall client failed exactly this way once before.                                                                                                             |
| 4   | Nothing guards the one origin                                                               | **True**                          | That test covers `kiosk-web` (`5174`) and `kiosk-wall` (`5175`) only: **`management-web` is not covered even for its own port**, and `5180` is covered for nobody.                                                                                                                                                                                                                                                       |
| 5   | The kiosk and the wall display fail the same way                                            | **Predicted, not observed**       | They carry the same `webOrigins` and the same shape of `redirectUris` (premise 2). Their redirect is base-aware since spec 494 (`/kiosk/oidc/callback`, `/wall/oidc/callback`), which a `http://localhost:5180/*` entry would match. FR-001 makes it an observation for all three.                                                                                                                                       |
| 6   | The realm lives in one place                                                                | **True**                          | One file, `smart-sentinel-eye-realm.json`, imported by the AppHost (`WithRealmImport`) and read by the architecture tests (`RealmIdentityTests`, `KioskScopeParityTests`, `WallClientDeclarationTests`, …). No second copy; `master-realm.json` holds no browser client.                                                                                                                                                 |
| 7   | Sign-out has its own list that could have the same gap                                      | **Not applicable as configured**  | None of the three clients sets `post.logout.redirect.uris` and the apps' auth modules call no sign-out redirect today, so sign-out falls back to the redirect URIs. Edge case, not a requirement; see the edge cases.                                                                                                                                                                                                    |

## User Scenarios & Testing _(mandatory)_

### User Story 1 - An operator signs in at the one URL (Priority: P1)

An operator opens the gateway's address, presses _Sign in_, authenticates, and lands on the console, signed in, with every page working.

**Why this priority**: It is the owner's goal. One URL for the console, the kiosk and the wall display is worth nothing if nobody can sign in at it,
and today nobody can; the one-origin work (#3253, #3266, #3269) has only ever been exercised signed out.

**Independent Test**: Open `http://localhost:5180/`, sign in as the seeded operator, and see _Cameras_. Repeat at `http://localhost:5173/`.

**Acceptance Scenarios**:

1. **Given** the gateway's address, **When** the operator signs in, **Then** the console renders signed in; no "Sign-in failed" appears.
2. **Given** the console's own port, **When** the operator signs in, **Then** it still works exactly as before.

---

### User Story 2 - A kiosk and a wall display sign in at their paths (Priority: P1)

A kiosk at `/kiosk/` and a wall display at `/wall/` sign in at the gateway's address and show their screens. The wall display asks for a grant that
outlives the session ceiling; that must work at the one origin too.

**Why this priority**: They are two of the three apps the one URL exists for, and the wall display runs unattended: a wall that cannot sign in
stays blank.

**Independent Test**: Open `http://localhost:5180/kiosk/` and `/wall/`, sign in as the seeded kiosk and wall accounts, and reach each app's first
screen.

**Acceptance Scenarios**:

1. **Given** `/kiosk/` on the one origin, **When** the kiosk account signs in, **Then** the wall list renders; the browser returns to
   `/kiosk/oidc/callback` and then to the page it came from, under `/kiosk/`.
2. **Given** `/wall/` on the one origin, **When** the wall account signs in, **Then** the app starts in wall mode and holds its long-lived grant.

---

### User Story 3 - A change that drops the one origin is caught before merge (Priority: P2)

A future edit to the realm, the gateway or an app that breaks sign-in at the one origin fails a check, not a person.

**Why this priority**: This defect survived three merged specs because nothing signed in at that origin and the existing guard covered neither
that origin nor the console. A fix with no guard is the same defect waiting.

**Independent Test**: Remove the one origin from any of the three clients in a scratch copy of the realm; a check fails naming the client.

**Acceptance Scenarios**:

1. **Given** a realm where one of the three clients does not name the one origin, **When** the checks run, **Then** a failure names that client and the
   origin.
2. **Given** the full stack, **When** the suite signs in at the one origin as each of the three clients, **Then** all three reach their first screen.

---

### User Story 4 - The fixed ports keep working (Priority: P3)

Signing in at `:5173`, `:5174` and `:5175` is unchanged (ADR-0192 decision 7).

**Why this priority**: The end-to-end suite and every developer's habit still use them until #3247 moves the suite over.

**Independent Test**: The existing `chromium`, `kiosk` and `wall` Playwright projects pass unmodified.

**Acceptance Scenarios**:

1. **Given** the realm change, **When** the existing sign-in specs run at the fixed ports, **Then** they pass.

---

### Edge Cases

- **A persistent dev stack keeps its old realm.** Keycloak imports the realm file only into an empty data volume, so an existing stack stays broken
  and looks healthy until the volume is deleted. CI boots fresh and is not affected; a developer's persistent stack is.
- **`127.0.0.1` is a different origin from `localhost`.** The one origin is the address the gateway is bookmarked at (`localhost`); the spec does
  not widen the list to loopback spellings.
- **An origin that is not listed is still refused.** Adding the one origin must not be done by widening to a wildcard on the web-origins side; an
  unlisted origin's token request must still be blocked.
- **The redirect after sign-in differs per app.** The console returns to `/`; the kiosk and the wall display return to their base path's
  callback. One entry per client must cover its own app's return.
- **Sign-out** (premise 7): if an app later calls an end-session redirect, it falls back to the redirect URIs; it is not exercised today and not
  made a requirement here.
- **The wildcard `http://localhost:*` stays** (Assumptions); it is the reason the failure looked like a passing configuration.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The failure MUST be observed before it is fixed, **for all three apps** (ADR-0139): a sign-in at the one origin as the console, as
  the kiosk and as the wall display, each recorded failing on the pre-change tree with the blocked token request quoted. Premise 5 is a prediction
  until then.
- **FR-002**: At the one origin, the operator console MUST complete sign-in and render signed in (User Story 1).
- **FR-003**: At the one origin, the kiosk (`/kiosk/`) and the wall display (`/wall/`) MUST each complete sign-in and reach their first screen,
  the wall display holding its long-lived grant (User Story 2).
- **FR-004**: Each of the three clients MUST name the one origin explicitly, so that the browser's token request from that origin is admitted; the
  web-origin setting MUST NOT be widened to admit origins that are not listed.
- **FR-005**: A check MUST fail, naming the client and the origin, when any of the three clients does not name the one origin **or its own
  port**. The existing guard covers two clients and no one-origin entry; it MUST cover all three clients and both origins (premise 4).
- **FR-006**: A check MUST sign in at the one origin as each of the three clients against the real identity provider, so the next change to this
  path is caught by a browser and not by a person (User Story 3, scenario 2).
- **FR-007**: Sign-in at the fixed ports `:5173`, `:5174` and `:5175` MUST be unchanged, and the existing end-to-end projects MUST pass unmodified
  (ADR-0192 decision 7).
- **FR-008**: An origin that is not listed MUST still have its token request blocked, observed once (the counterfactual that the change did not
  simply open the realm).
- **FR-009**: The change MUST NOT touch the layout hub, the gateway, Keycloak's placement behind or beside the gateway, the realm's scopes,
  audiences or grants, or any client other than the three named.
- **FR-010**: The operational consequence MUST be written where a developer will read it: an existing persistent stack needs its Keycloak data
  volume deleted to pick up the change (`docs/deployment-frontend-env.md` or the runbook that already says so for realm edits).

### Key Entities _(include if feature involves data)_

- **Browser client**: a Keycloak client for a single-page app (`management-web`, `kiosk-web`, `kiosk-wall`): public, PKCE, with a list of redirect
  URIs and a list of web origins. The web-origin list is `["+"]`, meaning "the origins of the explicit redirect URIs".
- **The one origin**: the gateway's address, from which all three apps are served (ADR-0192); in dev, `http://localhost:5180`.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: On a running stack, signing in at the one origin succeeds for **3 of 3** apps (console, kiosk, wall display), where it succeeds for
  0 of 3 today (1 observed, 2 predicted).
- **SC-002**: Signing in at each of the three fixed ports still succeeds (3 of 3), with the existing end-to-end projects unmodified.
- **SC-003**: Removing the one origin from any single client makes a check fail within the normal test run, naming that client (3 of 3 clients
  covered; 2 of 3 are covered for their own port today, 0 of 3 for the one origin).
- **SC-004**: A token request from an origin that is not in any client's list is still blocked (observed, once).
- **SC-005**: The sign-in at the one origin is performed by an automated browser check on every full CI run, not only by hand.

## Assumptions

- **The one origin is added next to the existing entries, not instead of them** (ADR-0192 decision 7 keeps the fixed ports working). The
  per-port entries are the reason the existing sign-ins work and stay.
- **`webOrigins` stays `["+"]` and the origin is named in `redirectUris`.** That is the mechanism the repo already documents and guards, the
  smallest change, and it keeps the list exact. Replacing `"+"` with an explicit web-origin list is a larger change and not needed.
- **The wildcard `http://localhost:*` is not removed.** It is a broad dev convenience on a public client and arguably should go, but removing it
  could break any other local port (a remote, a preview, a non-fixed Vite port) and is a separate decision; it is recorded here, not made.
- **Production's origin is out of scope** (#1015): there is no production deployment, so the production redirect URIs and web origins are not
  designed here.
- **Persistent dev stacks are the developer's to reset.** The spec records the volume-deletion consequence (FR-010); it does not automate a
  re-import.
- **Out of scope:** the layout hub (spec 495), the gateway's routes, Keycloak behind the gateway, the e2e suite moving onto the one origin
  (#3247), the per-app sign-out flow, and any other client in the realm.
- **No ADR is written.** ADR-0192 decision 6 already says the new origin is added to the realm clients' `redirectUris` / `webOrigins`; this
  implements that decision and finds it had not been done.
