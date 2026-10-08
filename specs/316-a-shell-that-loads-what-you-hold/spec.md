# Spec 316: A shell that loads what you hold

**Issue:** supersedes [#1008](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1008),
*feat(operator-mfe): shell host + first feature remote (thin slice)*. #1008 is under-scoped
against ADR-0168: it has no claims-driven navigation. #1007 (the composition spike) is **closed**,
superseded by ADR-0168 rather than run. The feature-level issue this spec is tracked against is
created at Phase 3 and replaces #1008 on Project #13.
**Branch:** `feat/316-operator-mfe-shell-and-first-remote` (cut from `develop` @ `ba37ca5a`; carries
`0a8d6638`, ADR-0168 plus ADR-0107's status line).
**Created:** 2026-10-08 · **Lane:** supervised (ADR-0037). This is human-initiated, and it touches two
open decisions (§Clarifications).
**Status:** Clarified; both answered by the user 2026-10-08 (§5). No markers remain.

**Governing ADRs, not re-litigated here:**
- **ADR-0168**: Vite Module Federation (`@module-federation/vite`); the shell owns routing, auth,
  design system, the RTK contract and the nav manifest; one remote per bounded context;
  navigation computed from `sse.*` scopes; `kiosk-web` unaffected; migrate incrementally.
- **ADR-0107**: re-architect `management-web` as shell plus remotes; Camera Catalog is the suggested
  first slice; no big-bang rewrite.

**Also binding:** ADR-0074 (each app has its own `package.json`, Vite config, build and Aspire JS
resource), ADR-0075 (one Redux store per app), ADR-0077/0078 (Radix + Tailwind tokens), ADR-0080
(`react-oidc-context`), ADR-0106 (gateway, REST only, see Q2), ADR-0114/0159 (fab groups),
ADR-0036 (smallest change), ADR-0139 (red vs characterisation), ADR-0109 (`[P]` disjoint files).
**Constitution:** §VI (Aspire is the composition root), §VIII (authorization is the endpoint
scope check), §IX row *Authorization: scopes + fab groups*. The scope vocabulary is
`src/ServiceDefaults/Authorization/Scope.cs` (`sse.<resource>.<verb>`). This spec adds no scope.
**New ADR:** none; both Q1 and Q2 resolved to the no-ADR option. Per-role scope grants (Q1 option B)
and gateway/edge serving (Q2 option B) each still owe one later, if and when they're taken up.

**Latency budget (§IV):** **N/A to all six legs.** The event→overlay path is `kiosk-web`, and
nothing in this slice changes it (FR-018). **A different budget is affected and is cited here:**
spec 002 FR-013, *click → first decoded frame ≤ 3 s p95*. It is measured by
`e2e/click-to-first-frame.spec.ts` on the **camera detail page, which this slice moves into the
first remote**. Lazy-federating that page adds a remote fetch in front of the click. See FR-019 and
SC-6. The spec file itself says this is not a §IV leg, and §VII's dashboard rule does not attach to
it.

---

## 1. The story, and what it means for this slice

> "I want a big micro frontend client for all operator/management/maintenance and want start all
> modules separately like now. Navigation would be loaded by rights/roles/claims and a
> kiosk/display wall client [stays separate]." (user, 2026-10-08)

| Phrase | In this repository | In this slice |
|---|---|---|
| "one big micro frontend client for operator/management/maintenance" | One shell. The personas differ by the scopes their token carries, not by app (ADR-0074 already put admin and operator in one SPA). | The shell is **`management-web`, evolved in place** (ADR-0107: "re-architect `management-web`"; ADR-0168 §4: the manifest "replaces today's unconditional route list in `router.tsx`"). It is not a third app beside it. |
| "start all modules separately" | ADR-0168 §3: a remote's build and deploy never require rebuilding the shell or another remote. | **One** remote, with its own package, Vite config, build and Aspire resource (US3). The other six feature folders stay in the shell for now (§6). |
| "navigation loaded by rights/roles/claims" | ADR-0168 §4: the `sse.*` scopes the session's token carries. | Every nav entry is gated, in-shell and remote alike (US2, FR-010). **Roles do not drive navigation:** see Q1. |
| "kiosk/display wall client stays separate" | ADR-0107, ADR-0168 §5. | Not touched at all (FR-018). |

### 1.1 What is already true on this tree (read 2026-10-08, not assumed)

| Fact | Where | Consequence for the slice |
|---|---|---|
| `router.tsx` declares 12 routes under one `ShellLayout` layout route, with no scope check. `/` renders `CamerasPage` directly so bookmarks still land on cameras. | `apps/management-web/src/app/router.tsx` | Routes become the manifest's job. The `/` behaviour must survive (FR-012). |
| `ShellLayout`'s `DESTINATIONS` (7 entries) is the **single source for both the nav bar and the command palette** (spec 266 SC-6). | `ShellLayout.tsx` | The palette must be filtered from the same list as the nav (FR-011), or SC-6 regresses. |
| Each surface has `errorElement: <SurfaceCrash />`, a child-route error element, so the nav survives a crashed surface (spec 011 FR-016). | `router.tsx`, `ShellLayout.tsx` | A remote that fails to load is the same kind of failure, and is contained the same way (FR-007). |
| `auth.ts` asks only for `openid`. The 21 `sse.*` scopes are **default client scopes** of the `management-web` client, and each has `include.in.token.scope: "true"`. | `auth.ts`; `src/AppHost/Realms/smart-sentinel-eye-realm.json:40-60,126-169` | The granted scopes come back in the token response's `scope`, so `oidc-client-ts` exposes them as `User.scope`. The shell reads that and **does not decode the access token** (FR-009). That is the same discipline `useAssignedFabs.ts` records for the fab claim. |
| **Every console session holds every scope.** Scopes are granted per client, not per realm role (spec 200, "Scopes in this realm are granted per client, not per realm role"). Per-role least privilege was #2487. It is **closed COMPLETED with no comment and no linked change**, and the realm still lists all 21 for `management-web`. | spec 200 §Problem; realm file; `gh issue view 2487` | Claims-driven nav is **correct but currently invisible** to any real console user. **Q1.** |
| `apps/shared` already is the shared package for the design system (`ui/`), every RTK Query API slice (`api/*.api.ts`), the realtime client (`realtime/`), streaming (`WhepClient`) and observability. | `apps/shared/package.json` exports | Most of #1008's "extract shared packages" list is **already done**. What remains is in the next three rows. |
| `apps/shared/src/api/gateway.ts` holds **module-level mutable state**: `accessTokenProvider`, `sessionRenewer`, `onSessionExpired`, `renewalInFlight`. The shell's `AuthGate` sets them during render. | `gateway.ts:46-83`; `App.tsx` | If the remote gets its own copy of this module, its token provider returns `undefined` and **every camera request 401s**. One instance is load-bearing (FR-005). |
| The store statically mounts 8 API slices, `resetApiCaches` clears all of them when the OIDC subject changes (spec 303), and a listener middleware serves `useRevocationFallback` (spec 314). | `app/store.ts` | The remote must dispatch into **the shell's store and the same slice instances** (FR-004). If it does not, a new operator could see the previous operator's cached cameras, which reopens spec 303 FR-001. |
| `features/cameras` production code reaches outside its folder in **one** place: `RegisterCameraDialog.tsx` imports `../../app/useAssignedFabs`. `CameraDetailPage.tsx` uses `useAuth`. Every `../../app/store.js` import is in a test file. | grep of `features/cameras/*.tsx` | `useAssignedFabs` must move to `apps/shared` (FR-006). The test files need a store harness built from shared slices. That is a Plan detail. |
| `management-web` is an Aspire `AddJavaScriptApp` on fixed port 5173 with `isProxied: false`, `WithNpm(install: false)` and gateway/Keycloak env. Kiosk instances use 5174/5175. The pnpm workspace is `apps/*`, so it does not pick up nested folders. | `src/AppHost/AppHost.cs:727-772`; `pnpm-workspace.yaml` | The remote is a new **top-level** `apps/<name>` package and a fourth `AddJavaScriptApp` (US3). |
| ADR-0106's gateway is **"REST/HTTP only"**. Neither SPA is served through it today. Both are Vite dev servers on their own endpoints, and only their REST calls go through the gateway. | ADR-0106 §Decision; `AppHost.cs:690-735` | "Served behind the gateway/static edge" (ADR-0107, ADR-0168) has no static edge to stand behind. **Q2.** |

### 1.2 First remote: Camera Catalog, confirmed

ADR-0107 suggests it. Having read `features/cameras` (7 production files, 15 test files), it is kept
because it is **the candidate that exercises every shared contract ADR-0168 accepts as risk**, and
the first slice is where ADR-0168 says that risk gets "its first real exercise":

| Shared contract | Cameras exercises it | Audit (the thinnest alternative) |
|---|---|---|
| RTK store + slice identity | `camerasApi` + `streamsApi`, reads and writes | `auditApi`, read only |
| Auth session (`useAuth`, gateway token) | yes, `CameraDetailPage` | token only |
| Fab claim (`useAssignedFabs`) | yes, `RegisterCameraDialog` | no |
| Router params / nested route | `cameras/:cameraIdentifier` | no |
| Design-system composites | Dialog, ConfirmDialog, Popover, FormField, Badge, CameraViewer, RetryBanner | DataTable |
| `useRevocationFallback` (spec 314 listener) | yes | no |
| An existing e2e budget | click-to-first-frame (spec 002 FR-013) | none |

Audit would make a thinner slice that proves almost nothing about the risk ADR-0168 accepted.
Cameras is also `/`'s current default, so FR-012 is exercised for free. **The cost is that
cameras carries the one budgeted interaction (FR-019).** That is the reason it is a better
proof, not a reason to avoid it.

---

## 2. User scenarios and testing

Seeded accounts: `operator` / `Operator1234` (role `user`, `/fabs/munich`) and `admin` /
`Admin1234` (roles `user`, `admin`, `/fabs/munich`), per spec 200. Both currently receive all 21
console scopes (§1.1).

### User Story 1: the shell federates the cameras remote, end to end (Priority: P1)

An operator signs in to the console as today. The Cameras surfaces (list, register, rename,
re-address, retire, detail with live viewer and stream health) behave exactly as they do now,
but their code is built and served by a separate remote. The shell loads that code at runtime and
shares one auth session, one store, one router and one design system with it.

**Why P1:** without this nothing else is observable. It is also the slice that first exercises
ADR-0168's accepted shared-dependency risk.

**Phase-4a colour (declared at Phase 3, recommended here): characterisation.** For the user the
Cameras surfaces do not change. `e2e/cameras.spec.ts`, `e2e/camera-detail.spec.ts`,
`e2e/click-to-first-frame.spec.ts` and `e2e/command-palette.spec.ts` are captured green before the
move and must pass **unmodified** after it. The moved Vitest suites likewise, apart from import paths
and harness. An assertion that has to be edited is a block, not an adjustment (ADR-0139). New
behaviour inside US1 (the load-failure panel, FR-007) is red.

**Independent test:** boot the stack (`aspire run`). Sign in at the shell as `operator`. Go to
Cameras and check, in the browser network panel, that the camera list code comes from the remote's
own origin. Register a camera, open its detail page and watch the first frame decode, all through
the shell. Run the four e2e specs above unmodified, green.

**Acceptance scenarios**

```gherkin
Feature: The cameras surfaces are a federated remote

  Background:
    Given the stack is running with the shell and the cameras remote as separate resources
    And "operator" is signed in to the shell

  Scenario: Happy path: the list renders from the remote through the shell
    When the operator navigates to "/cameras"
    Then the shell fetches the cameras remote's entry from the remote's own origin
    And the camera list renders inside the shell's layout, with the shell's navigation above it
    And exactly one React, one react-redux store and one react-oidc-context session exist in the page

  Scenario: Happy path: deep link to a detail page
    When the operator opens "/cameras/<an existing camera identifier>" in a fresh tab
    Then sign-in restores the deep link (spec 011 FR-013)
    And the camera detail page renders from the remote, and its live viewer decodes a first frame

  Scenario: Happy path: the remote's writes use the shell's session and store
    When the operator registers a camera from the remote's dialog
    Then the request reaches the gateway carrying the shell session's bearer token
    And the shell's store holds the resulting cache entry; no second store exists

  Scenario: Conflict: a stale write is refused exactly as today
    Given the camera was renamed by another session after this page loaded
    When the operator submits a rename from the remote
    Then the 409 is surfaced as it is today (ADR-0043/0113), unchanged by federation

  Scenario: Bad request: invalid input is rejected as today
    When the operator submits the register dialog with an invalid RTSP address
    Then the remote shows the same field-level validation it shows today, and nothing is sent

  Scenario: Auth: a changed operator never sees the previous operator's cameras
    Given the cameras remote has loaded and cached a camera list for "operator"
    When the OIDC subject changes to "admin" in the same tab
    Then the shell's cache reset (spec 303) clears the remote's cached camera data as well

  Scenario: Auth: an expired session escalates through the shell
    Given the session cannot be renewed
    When the remote issues its next request
    Then the shell shows its "Session expired" screen (spec 011 FR-014), the same path as today

  Scenario: The remote is down, and the shell is not
    Given the cameras remote resource is stopped
    When the operator navigates to "/cameras"
    Then the shell renders a contained "this surface could not be loaded" panel with a retry action
    And the shell's navigation stays visible and every in-shell surface still works
    And a resilience event is logged for the failed load
```

### User Story 2: navigation shows only what the token holds (Priority: P2)

The shell builds its nav bar and command palette from nav manifests. An entry appears only if the
signed-in session's token carries **every** scope it requires. A remote the operator is not
entitled to is **not fetched, not federated and not shown** (ADR-0168 §4). That is not a CSS hide.
Navigation visibility is a UX affordance, **not a trust boundary**. The endpoint `RequireScope`
check stays the authority (§VIII; ADR-0168 §Consequences).

**Why P2:** it depends on US1's manifest being loadable, and today it has no visible effect for any
real console user (Q1). It is still the requirement #1008 lacked.

**Phase-4a colour: red.** The behaviour changes: navigation that is unconditional today becomes
conditional.

**Independent test:** with component tests, render the shell's navigation against a session whose
`User.scope` is (a) all 21 console scopes, (b) the same minus `sse.cameras.read`, (c) only
`sse.audit.read`, (d) empty. Check which entries the nav and the palette show, and that in (b), (c)
and (d) the cameras remote entry is never requested. End to end, `operator` sees all seven
entries. The end-to-end proof for a narrower token needs a scope set narrower than any real
account currently holds (Q1, option A) — mechanism (a test-only override of the granted-scopes
input, not a realm change) is chosen in Plan.

**Acceptance scenarios**

```gherkin
Feature: Navigation is computed from the session's scopes

  Scenario: Happy path: a full-scope operator sees every surface
    Given "operator" is signed in with all 21 console scopes
    Then the nav bar and the command palette both list exactly the seven surfaces in today's order

  Scenario: An unentitled remote is neither shown nor fetched
    Given a session whose granted scopes do not include "sse.cameras.read"
    Then neither the nav bar nor the command palette offers "Cameras"
    And no request is made for the cameras remote's entry or modules

  Scenario: Typing the address of an unentitled surface
    Given a session whose granted scopes do not include "sse.cameras.read"
    When the user navigates directly to "/cameras/<identifier>"
    Then the shell renders a "not available to your account" panel inside its layout
    And the cameras remote is not fetched

  Scenario: In-shell surfaces are gated by the same rule
    Given a session holding only "sse.audit.read"
    Then the nav lists only "Audit", and "/" shows the Audit surface

  Scenario: Bad request: a malformed or unknown-version manifest fails closed
    Given the cameras remote publishes a manifest that fails schema validation
    Or declares a schema version the shell does not understand
    Then the shell shows no entry for that remote, logs a resilience event naming the remote
    And every other entry renders normally

  Scenario: Auth: the nav follows the token, not the first render
    Given the nav has rendered for a session
    When the token is renewed silently with a different granted scope set, or the subject changes
    Then the nav and palette are recomputed from the new scopes without a reload

  Scenario: Auth: nav visibility is not authorization
    Given a session without "sse.cameras.write" that can nonetheless see Cameras (it holds "sse.cameras.read")
    When it attempts a register
    Then the API answers 403 exactly as today, and the remote surfaces it as it does today

  Scenario: No entitled surface at all
    Given a signed-in session holding none of the scopes any entry requires
    Then the shell renders its layout with an empty navigation and a "no surfaces are available to your account" message
```

### User Story 3: the remote starts, builds and deploys on its own (Priority: P3)

"Start all modules separately", made operational. The cameras remote is its own pnpm workspace
package with its own `package.json`, Vite config, build output and **Aspire JS resource**. That
mirrors how `management-web` and `kiosk-web` are separate resources today (ADR-0074). It can be
stopped, restarted and rebuilt without touching the shell or any other app.

**Why P3:** US1 already needs the separate package to exist. US3 is the operational guarantee on
top: separate lifecycle, separate build, and no shell rebuild for a remote change.

**Phase-4a colour: red** for anything with a test (for example an AppHost resource-graph test
asserting the new resource and its references). Configuration with no test is verified at Phase 5.

**Independent test:** in the Aspire dashboard, see the shell and the cameras remote as two
resources under the gateway. Stop the remote: the shell keeps serving in-shell surfaces (US1 last
scenario). Start it again: Cameras works after a reload with no shell restart. Change a label in the
remote, rebuild only the remote (`pnpm --filter <remote> build`), reload: the change appears, and
the shell's build output is byte-identical to before.

**Acceptance scenarios**

```gherkin
Feature: The cameras remote has its own lifecycle

  Scenario: Two resources, not one
    When the AppHost starts in run mode
    Then the dashboard lists the shell and the cameras remote as separate JavaScript resources
    And the cameras remote has its own fixed endpoint, distinct from 5173, 5174 and 5175

  Scenario: Independent restart
    Given the stack is running and Cameras has loaded
    When the cameras remote resource is restarted and the operator reloads
    Then Cameras works again and the shell resource was never restarted

  Scenario: Independent build
    When only the cameras remote is rebuilt after a change confined to it
    Then the shell's build output is unchanged and the change is visible after reload

  Scenario: Bad configuration fails loudly, not silently
    Given a production build of the shell with no cameras-remote location configured
    Then the build fails with a message naming the missing setting
    (the same rule auth.ts applies to VITE_KEYCLOAK_URL, spec 011 FR-010)

  Scenario: Workspace gates include the remote
    When the root "pnpm build", "pnpm lint", "pnpm typecheck" and "pnpm test" run
    Then the cameras remote is built, linted, type-checked and tested by each, with no per-app list to edit
```

### Edge cases

- **Remote reachable, shared-dependency version mismatch.** ADR-0168's accepted risk, in
  practice: the remote was built against a React / RTK / router / OIDC / `@smart-sentinel-eye/shared`
  version the shell does not share. It must fail **at the remote's load, contained** (US1 "remote
  is down" panel), not with a blank shell or a duplicated store. The governance mechanism is Plan
  scope (§6), but the observable result is FR-008.
- **Remote's manifest loads, its entry does not.** The entry stays visible (the operator is
  entitled). Navigating shows the load-failure panel, and retry re-attempts the load.
- **Token renewal narrows scopes while the user is on a now-unentitled surface.** The nav entry
  disappears, the current route shows the "not available" panel on the next navigation, and API
  calls 403 as today. No forced redirect mid-form, the same rule the command palette's dialog guard
  follows (spec 266).
- **`/` with cameras unentitled.** `/` shows the first entitled entry in manifest order, or the
  empty-nav message.
- **The remote is served from a different origin than the shell.** The remote's code runs in the
  shell's page and origin, so its REST calls originate from the shell's origin, already allowed by
  the gateway's CORS policy (#1003). Only the *module fetch* is cross-origin, and it must be
  permitted by the remote's server. Plan detail.
- **Multi-fab operator.** Fab membership does not gate navigation (FR-009 note). The register
  dialog's fab picker keeps working from the moved `useAssignedFabs`.

---

## 3. Requirements

### Composition (US1)

- **FR-001** The console's shell is `apps/management-web`, evolved in place. It owns the
  `AuthProvider` (ADR-0080), the single Redux store (ADR-0075), the data router, `ShellLayout`
  (nav, command palette, crash containment) and the design-system stylesheet and fonts. It
  federates remotes with `@module-federation/vite` (ADR-0168 §1).
- **FR-002** The contents of `apps/management-web/src/features/cameras` move into a new top-level
  workspace package, the **cameras remote**. The remote owns every route under `/cameras`
  (`/cameras`, `/cameras/:cameraIdentifier`). The shell holds no cameras page code afterwards.
- **FR-003** The shell lazy-loads the cameras remote on first navigation to a route it owns, and
  never at sign-in or on other routes.
- **FR-004** The remote has **no store of its own**. It dispatches into the shell's store, and the
  `camerasApi` / `streamsApi` slice instances it uses are the ones that store mounts, so
  `resetApiCaches` (spec 303) and the `useRevocationFallback` listener (spec 314) cover it unchanged.
- **FR-005** At runtime there is exactly **one instance** of each of these modules, whose identity
  is load-bearing: `react`, `react-dom`, `react-router-dom`, `react-redux`, `@reduxjs/toolkit`,
  `react-oidc-context`, and `@smart-sentinel-eye/shared`. The last one holds `gateway.ts`'s token
  provider and every API slice, so a second copy 401s every request or splits the cache. How this
  is configured is Plan scope. That it holds is a requirement, and a test must be able to fail on it.
- **FR-006** Remote code reaches shell-owned behaviour only through `@smart-sentinel-eye/shared` or
  the shared libraries above, never by a relative import into the shell. `useAssignedFabs` moves
  to `apps/shared` for this reason. No other `features/cameras` production import crosses the
  folder (§1.1).
- **FR-007** A remote that fails to load (unreachable, script error, or failed module init) is
  contained the way a crashed surface is today (`SurfaceCrash`, spec 011 FR-016): the panel renders
  inside the shell's outlet, the nav stays usable, a retry action re-attempts the load, and
  `logResilienceEvent` records it with the remote's name.
- **FR-008** A shared-dependency mismatch (FR-005 not satisfiable at load) is a contained load
  failure under FR-007. It is never a blank shell and never a second instance.

### Navigation (US2)

- **FR-009** The shell reads the session's granted scopes from the OIDC `User.scope` value (the
  token response's `scope`). It **does not decode the access token**. Fab group membership
  (`groups` / `sse-groups`) is **not** a navigation criterion: it scopes *data* (ADR-0114,
  ADR-0159), not which surfaces exist.
- **FR-010** **Every** navigation entry carries a nav-manifest entry with its required scopes, in
  the shell and from remotes. An entry is visible iff the granted scopes include **all** of its
  `requiredScopes`. The seven entries' requirements are each surface's read scope, taken from the
  endpoints that serve it:

  | Entry | Path | `requiredScopes` | Source |
  |---|---|---|---|
  | Cameras (remote) | `/cameras` | `sse.cameras.read` | `CameraEndpoints.cs:105` (reads group) |
  | Layouts | `/layouts` | `sse.layouts.read` | `LayoutEndpoints.cs:58,70` |
  | Walls | `/walls` | `sse.layouts.read` | `WallEndpoints.cs:41,53` |
  | Overlays | `/overlays` | `sse.overlays.read` | `Scope.Sse.Overlays.Read` |
  | Rules | `/rules` | `sse.rules.read` | `RulesEndpoints.cs:78` |
  | System variables | `/system-variables` | `sse.variables.read` | `Scope.Sse.Variables.Read` |
  | Audit | `/audit` | `sse.audit.read` | `AuditEndpoints.cs:29` |

  *Why in-shell entries too:* `DESTINATIONS` is one list that feeds both the nav and the palette.
  Gating only the remote's entry would give the shell two navigation rules, one per origin of the
  code, and the next remote migration would silently change an entry's visibility. Without this
  FR, US2 would also be undemonstrable except on Cameras.
- **FR-011** The command palette's items are derived from the **same filtered list** as the nav
  bar (preserves spec 266 SC-6).
- **FR-012** `/` renders the first visible entry in manifest order, without a redirect (preserving
  `router.tsx`'s "no `<Navigate>` flash" reasoning). For a full-scope session that is Cameras, as today.
- **FR-013** A route whose entry is not visible renders a "not available to your account" panel
  inside the shell layout, and for a remote route does **not** fetch the remote.
- **FR-014** The visible set is recomputed when the granted scopes or the OIDC subject change,
  without a reload.
- **FR-015 Nav-manifest contract.** ADR-0168 left the schema to this level. It has to be
  **readable without loading the remote's code**, because an unentitled remote must not be fetched
  (ADR-0168 §4). So it is data, not a module export:
  - Each remote **publishes** a static JSON document, `nav-manifest.json`, at a fixed path beside
    its federation entry. A module export would force fetching the remote's entry before the shell
    could decide not to fetch it, and a shell-side registry would force a shell rebuild to change a
    remote's label, against US3.
  - The shell's **configuration** names each remote's location: a base URL, from which the
    manifest URL and the entry URL derive. It does not name the remote's routes, labels or scopes.
  - In-shell entries use the **same entry type**, declared in shell code.
  - Shape (version 1; the exact field names may be refined in Plan without changing meaning):

    ```jsonc
    {
      "schemaVersion": 1,
      "remote": "cameras",                 // federation name; unique
      "basePath": "/cameras",              // the remote owns this path and everything under it
      "entries": [
        {
          "path": "/cameras",              // within basePath
          "label": "Cameras",
          "icon": "camera",                // design-system icon key; optional
          "order": 10,                     // shell sorts by this; ties keep config order
          "requiredScopes": ["sse.cameras.read"]   // all-of; non-empty; each matches ^sse\.[a-z]+(\.[a-z]+)+$
        }
      ]
    }
    ```
  - The shell validates each manifest against a schema (zod, already a shared dependency) and
    **fails closed**: an invalid document, an unknown `schemaVersion`, an empty `requiredScopes`, or a
    `basePath` that collides with another entry's yields **no entry** for that remote, plus a resilience
    event. An empty `requiredScopes` is refused rather than read as "visible to everyone", so a
    forgotten field cannot publish a surface to every account.
  - The manifest asserts nothing the server trusts. It decides visibility only.
- **FR-016** Adding the gate adds no scope to `Scope.cs` or the realm, and does not change
  `auth.ts`'s `scope: 'openid'` (asking for a scope the client does not hold fails sign-in
  outright with `invalid_scope`, per spec 200 and spec 041).

### Independent lifecycle (US3)

- **FR-017** The cameras remote has its own `package.json` (a workspace package under `apps/*`, so
  `pnpm-workspace.yaml`'s `apps/*` and the root `./apps/**` filters include it with no list to
  edit), its own Vite config, its own `build`, `lint`, `typecheck` and `test` scripts, and its own
  Aspire `AddJavaScriptApp` resource. That resource mirrors `management-web`'s wiring (fixed port,
  `isProxied: false`, `WithNpm(install: false)`, parented to the gateway, run mode only, not in the
  integration fixture). The shell receives the remote's location from the AppHost, never
  hand-wired (§VI). A production shell build with that location unset fails, naming the setting
  (mirrors spec 011 FR-010).
- **FR-018** `apps/kiosk-web`, the `kiosk-web` and `kiosk-wall` resources, and every §IV leg are
  untouched. No file under `apps/kiosk-web` changes.
- **FR-019** The click-to-first-frame budget (spec 002 FR-013, ≤ 3 s p95) still holds through the
  federated detail page. `e2e/click-to-first-frame.spec.ts` passes unmodified, and its figure is
  recorded before and after the move in the PR. If the measured cost of a cold remote fetch
  endangers it, the Plan must say how the detail route's chunk is preloaded. That decision is not
  pre-empted here.

### Key entities (frontend-only; no domain model changes)

- **Remote**: a separately built and deployed bundle for one bounded context. It has a federation
  name, a base URL, a published nav manifest and exposed route modules.
- **Nav manifest / nav entry**: FR-015. Data, versioned, validated, fail-closed.
- **Granted scopes**: the set parsed from `User.scope`. The only input to visibility.
- **Visible entry set**: f(manifests, granted scopes) → ordered entries. It feeds the nav bar, the
  palette, `/`, and route gating.

---

## 4. Success criteria

- **SC-1** Signed in as `operator`, every Cameras interaction available today works through the
  shell, and the remote's code is observed arriving from the remote's own origin.
  `cameras.spec.ts`, `camera-detail.spec.ts` and `command-palette.spec.ts` pass **unmodified**.
- **SC-2** With the cameras remote stopped, the shell still signs in and serves the six in-shell
  surfaces, and `/cameras` shows the contained load-failure panel.
- **SC-3** A test that would fail if a second React, store or `@smart-sentinel-eye/shared` instance
  appeared at runtime exists and passes (FR-005), proven by counterfactual at Phase 4.
- **SC-4** For the four scope sets in US2's independent test, the nav and palette show exactly the
  expected entries, and in every set lacking `sse.cameras.read` the remote is never requested.
- **SC-5** Rebuilding only the remote leaves the shell's build output unchanged, and the change is
  visible after reload without restarting the shell resource.
- **SC-6** Click-to-first-frame p95 through the federated detail page stays ≤ 3 s, with before
  and after figures quoted in the PR (each run twice; see project memory on first-run noise).
- **SC-7** `git diff --stat develop -- apps/kiosk-web` is empty.

---

## 5. Clarifications

### Q1 — decided: ship the scope gate now (user, 2026-10-08)

You asked for navigation "loaded by **rights/roles**/claims". ADR-0168 §4 fixes the input as the
token's `sse.*` scopes. In this realm **scopes are granted per client, not per role**: every account
signing in through `management-web`, `operator` and `admin` alike, receives all 21 (spec 200,
realm file `:142-168`). #2487, "per-role least privilege for the operator console", is closed
COMPLETED, but no change shipped with it and the realm is unchanged. So as built, US2 changes what no
real user sees. It is correct, and its effect stays latent until some account's token is narrower.

| Option | What it means | Cost |
|---|---|---|
| **A (recommended)** | Ship the scope-gated nav as specified. Prove it with component tests over narrowed `User.scope` values, plus an e2e that narrows the scope set the browser receives (mechanism chosen in Plan). Per-role scope grants stay a separate, ADR-owed decision. Reopen #2487 or file anew, since its closure did not deliver it. | None beyond this spec. Visible effect deferred. |
| **B** | Also introduce role→scope differentiation in this spec (for example per-role clients or `clientScopeMappings`). | Needs an ADR (#2487 says so). It changes what every operator may do, and roughly doubles the slice. It is no longer a thin slice. |
| **C** | Gate navigation on realm roles as well as, or instead of, scopes. | Contradicts ADR-0168 §4, which this spec must not do. It would need an ADR-0168 amendment, and the UI would diverge from what the API enforces. |

**Decided: option A.** Ship the scope-gated nav as specified, proven with narrowed-`User.scope`
component tests plus an e2e that narrows the scope set the browser receives. Per-role scope grants
are a separate, ADR-owed decision — reopen #2487 or file anew, since its closure did not deliver it.
Not part of this spec.

### Q2 — decided: own Aspire endpoint, defer the edge (user, 2026-10-08)

ADR-0107 and ADR-0168 both say remotes are served behind ADR-0106's gateway/static edge. ADR-0106
scopes the gateway to **REST/HTTP only**. No static edge exists, neither SPA is served through the
gateway today (both are Vite dev servers on their own Aspire endpoints, `AppHost.cs:727-772`), and
there is no production deployment for a static edge to live in (deploy/helm holds one Mosquitto
chart).

| Option | What it means | Cost |
|---|---|---|
| **A (recommended)** | In this slice the remote is served from its **own Aspire endpoint**, exactly as `management-web` is today. Serving SPAs and remotes from an edge is deferred to the spec that creates a production deployment, which then amends ADR-0106. | ADR-0168's sentence stays unmet for now, recorded as deferred in the PR and on the feature issue rather than silently. |
| **B** | Add static or remote-asset routes to the YARP gateway in this slice. | Extends ADR-0106 beyond "REST only", so it needs an ADR amendment. The gateway becomes an SPOF on the UI path in dev. And it is still the only frontend asset behind the gateway, which would make the shell and its remote inconsistent with each other. |

**Decided: option A.** The remote is served from its own Aspire endpoint in this slice, exactly as
`management-web` is today. Serving SPAs and remotes from an edge is deferred to the spec that
creates a production deployment, which then amends ADR-0106 — recorded as deferred, not silently
dropped.

### Decided here, not left open (each could have been a question; each was answerable from the tree)

- **First remote:** Camera Catalog (§1.2).
- **Shell location:** `management-web` in place, not a new app (§1 table).
- **In-shell entries gated too:** yes (FR-010 rationale).
- **Scope semantics:** all-of. Empty means refused, not public (FR-015).
- **Scope source:** `User.scope`, not access-token decoding (FR-009).
- **Fab membership in nav:** no (FR-009).
- **Manifest form:** published JSON, not a module export and not a shell registry (FR-015).

---

## 6. Out of scope

- **Migrating any other feature folder** (`layouts`, `walls`, `overlays`, `rules`,
  `systemVariables`, `audit`) into a remote. ADR-0168 §6 says one at a time. They stay in the shell,
  gated by FR-010. `features/ArchiveConfirmation.tsx` and `features/chainView.ts` (shared by
  in-shell features) stay put. Whether they later move to `apps/shared` is decided with the remote
  that first needs them.
- **Any change to `kiosk-web`** (ADR-0107, ADR-0168 §5; FR-018).
- **Full design of shared-dependency version governance.** ADR-0168 requires governance to be
  *explicit from the first remote*. This spec therefore makes the **observable requirement**
  binding now (FR-005 singletons by name, FR-008 contained failure, SC-3 a test that can fail). The
  **mechanism** (the Module Federation `shared` config: singleton flags, `requiredVersion` ranges,
  eager vs lazy, how `@smart-sentinel-eye/shared`'s `workspace:*` version is expressed) is a
  Plan-phase decision. *Why not specify it here:* the config is how the plugin is used, which is
  exactly what Plan exists to choose against the pinned plugin version (project memory: "a plan's
  SDK claim needs checking against the pinned version"). The behaviour it must produce is already
  fixed above, so leaving the mechanism open does not leave the risk open.
- **A release discipline for `@smart-sentinel-eye/shared`** beyond the workspace (publishing,
  semver). It is not needed while every remote builds from this monorepo. ADR-0107 names it as
  eventually needed.
- **Per-role scope grants** (Q1 option B), and **gateway or edge serving of frontend assets** (Q2 option B).
- **Cross-remote state or remote-to-remote navigation contracts.** With one remote, there is no
  second party.
- **Production deployment** (container images, Helm) for the shell or the remote. No frontend is
  deployed that way today (CLAUDE.md, `deploy/helm`).
- **Realtime client sharing**: the cameras surfaces do not use the SignalR hub. It is already in
  `apps/shared/realtime` for the remote that first needs it.

---

## 7. Assumptions, and what Plan must verify

- **A-1** Keycloak returns the default client scopes in the token response's `scope`, so
  `User.scope` carries them. This is inferred from `include.in.token.scope: "true"` on each
  `sse.*` client scope. **Plan verifies it against a booted realm** (project memory: guards that
  read the design artefact prove the design was written down, not that it holds).
- **A-2** `@module-federation/vite` supports a remote under the Vite dev server, so Aspire's `dev`
  script works for the remote as for the apps. If the pinned version needs build + preview for
  remotes in dev, Plan says so, and US3's resource runs that instead. US3's guarantees do not change.
- **A-3** The cameras Vitest suites can run in the remote's own package against a test store built
  from shared slices, without the shell. They must not need modified assertions (US1 colour).
- **A-4** Spec number 316: `origin/develop` tops out at 315, and no remote branch or open PR claims
  316 (checked 2026-10-08). Re-check before the PR.
- **A-5** Phase 3 may split this spec's feature issue. US1 (characterisation) and US2 (red) are
  different colours, and CLAUDE.md says a refactor that is also a behaviour change is two issues.
  This spec keeps them together because US2 cannot be demonstrated without US1's manifest loading
  path, but the tasks must keep the two colours in separate tasks and commits.
