# Feature Specification: The client nobody signs in with

**Feature Branch**: `chore/2488-retire-smart-sentinel-eye-web` (cut from `origin/develop` at `aa08ce16`)

**Created**: 2026-09-28

**Status**: Draft (Phase 1 gate)

**Input**: Issue [#2488](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2488):
*smart-sentinel-eye-web has no consumer left in the repository but is still a live password-grant
surface*. Follow-up to #2279 / spec 200 (§"Out of scope", "Retiring `smart-sentinel-eye-web`
altogether"). Related: #2486 (spec 265, bundle withdrawn), #2487.

**Spec number.** 286. `origin/develop` tops out at 285 on 2026-09-28; 284 is unclaimed on every
branch and worktree checked but is skipped because a parked agent worktree (`chore/2642`) may
still claim a number in that range. **Re-check before opening the PR** (memory: *spec number:
origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0080 (management-app browser auth; the
auth-code flow the console now runs on `management-web`), `0000-initial-decisions.md` rows
007-008 (Keycloak), ADR-0159 (one realm per deployment), ADR-0114 (fab guard, which bounds the
live surface today), ADR-0103 (Aspire-hosted tests, no Testcontainers), ADR-0139 and
constitution §Testing (red for new behaviour, characterisation for preserved behaviour),
ADR-0144 (autonomous lane, phase-4a colour), ADR-0037 (phases), ADR-0087 (each commit builds on
its own), ADR-0036 (smallest change), ADR-0109 (contention files).

**No ADR gap.** No ADR names `smart-sentinel-eye-web`. ADR-0080's code sketch names
`smart-sentinel-eye-management`, a client that has never existed in the realm; its *decision*
(react-oidc-context, auth code + PKCE for the management app) is carried by `management-web`
and is untouched here. Spec 200 declined this retirement as "deleting a client referenced by
ADR-0080 and five specs is a decision, not a fix" — **that premise was checked and does not
hold**: `grep -c smart-sentinel-eye-web docs/adr/0080-browser-auth.md` is 0, and the specs that
name it are historical records of their own work, not decisions that bind the realm. The
decision to remove the client is the issue's own request, made by the human who filed and
labelled it.

**Out of scope, deliberately, and not dropped:** ADR-0080's stale code sketch
(`client_id="smart-sentinel-eye-management"`, `scope="openid profile sse.management"`). The
issue raises it; amending an ADR is a human decision and the autonomous lane may not make it
(ADR-0144). It is left for a human and is named in the PR body so it is not lost.

---

## What was measured (2026-09-28, tree `aa08ce16`)

| Fact | Source |
|---|---|
| The client is declared once: public, standard flow **and** password grant, `webOrigins: ["+"]`, redirects `localhost:5173/*`, `5174/*`, `localhost:*`; default scopes `sse-identity`, `sse-audience`, `sse-groups`, `sse.audit.read` | `src/AppHost/Realms/smart-sentinel-eye-realm.json:124-147` |
| It is the only realm file; nothing mirrors it (`deploy/helm` holds Mosquitto only) | `git ls-files \| grep realm.*json` |
| `management-web`'s description reads "Replaces smart-sentinel-eye-web in spec 009." | realm `:151` |
| **No client code signs in with it.** `management-web/src/app/auth.ts` uses `client_id: 'management-web'`; `AspireFixture.ClientId` is `management-web` | `apps/management-web/src/app/auth.ts:23`; `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:12` |
| **Three test facts mint from it**, not two as the issue says: SC-1 and SC-2 in `ConsoleScopeGrantIntegrationTests`, and `A_caller_without_sse_layouts_write_is_refused_403` in `TileSpanIntegrationTests` | `tests/Integration.Tests/Identity/ConsoleScopeGrantIntegrationTests.cs:54,81`; `tests/Integration.Tests/LayoutComposition/TileSpanIntegrationTests.cs:414` |
| Every other mention is a comment or an inert string: `auth.ts:5`, `auth.test.ts:15`, `e2e/management-identity.spec.ts:14-15`, `VariableReadScopeIntegrationTests.cs:18`, `BrowserKioskPrincipalTests.cs:28` (an `azp` literal in a unit test, compared as a string, never minted), `.claude/agents/frontend-engineer.md:12` | `git grep` over `src apps tests e2e .github deploy scripts docs .claude` |
| An established pattern exists for "a token that lacks scope X": **plant a throwaway public password-grant client with exactly the default scopes the test needs, delete it in `finally`** | `EventTypeRegistryAuthorizationIntegrationTests.cs:206-233,299-322`; `RealmProbe.DeleteAsync` (`RealmProbe.cs:149`) |
| Keycloak applies *default* client scopes to every token; the `scope` parameter selects only among *optional* ones. So no existing client can be narrowed by request into "holds audit read, not variables read" | `EventTypeRegistryAuthorizationIntegrationTests.cs:263-296` (asserted on the running stack) |
| After this change, no realm client holding the password grant lacks `sse.variables.read`: `management-web` holds it; `kiosk-web`/`kiosk-wall` have no password grant; the service accounts use `client_credentials` | realm `:148-340` |
| Realm edits reach Keycloak only on a fresh data volume (`WithRealmImport` runs once) | `src/AppHost/AppHost.cs:154-160` |

## Why SC-1 and SC-2 can be reworked without inventing anything

SC-1's own doc comment says what it guards: a token holding **neither** `sse.variables.read`
**nor** the bundle is refused 403 on `GET /system-variables`, and since spec 265 withdrew the
grandfather clause that 403 would hold even if the bundle came back. SC-2 is its control: the
*same* token reads `/audit` (200), ruling out a broken mint, a rejected issuer or audience, or a
stack refusing everyone. Neither fact is about *which client* holds that scope set — the client
was the only realm object that happened to hold it.

A planted client with the retired client's exact default scopes (`sse-identity`,
`sse-audience`, `sse-groups`, `sse.audit.read`), public, password grant enabled, signed into by
the same `operator` user, produces the same token shape. SC-1's and SC-2's assertions on the
**API's** answer are then unchanged. This is the issue's own suggested subject ("a client that
holds neither the bundle nor `sse.variables.read`") and the repo's own existing mechanism.

**One assertion changes meaning and must be said so.** SC-1 also asserts the token does not
carry `sse.management`. On a planted client whose scopes the test itself chose, that assertion
checks its own input (memory: *an assertion must not check its own input*). It is kept as a
**probe-shape control** (as `EventTypeRegistryAuthorizationIntegrationTests` keeps the same
check), and the runtime question "does a real client re-acquire the bundle?" is answered, as it
already is, by SC-4 for `management-web` (runtime) and `LegacyBundleGrantTests` for every realm
client (static). `LegacyBundleGrantTests`' doc comment names SC-1 as "what asks the running
system"; it is updated to name SC-4.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An unused client can no longer mint a token (Priority: P1)

Anyone holding a realm account's username and password today can mint an audit-reading token
from `smart-sentinel-eye-web` with no client secret. Nothing in the system uses that client.
After this change the realm no longer defines it, so the token endpoint refuses it — while every
guarantee its three test uses were proving still holds, proved through a client the test owns.

**Why this priority**: it is the only story. The surface cannot be half-removed.

**Independent Test**: on a fresh Keycloak volume, post a password grant for
`smart-sentinel-eye-web` with the `operator` credentials to the realm's token endpoint and
observe the refusal; post the same credentials for `management-web` and observe a token. Then
run `ConsoleScopeGrantIntegrationTests` and `TileSpanIntegrationTests` green.

**Acceptance Scenarios**:

```gherkin
Feature: smart-sentinel-eye-web is retired

  Scenario: The retired client is refused at the token endpoint (auth — new behaviour)
    Given a Keycloak booted from the checked-in realm on a fresh volume
    When the operator requests a password grant with client_id "smart-sentinel-eye-web"
    Then Keycloak answers 401 with error "invalid_client"
    And no access token is issued

  Scenario: The refusal is about the client, not the credentials (control)
    Given the same Keycloak
    When the operator requests a password grant with client_id "management-web" and the same password
    Then Keycloak answers 200 with an access token

  Scenario: The realm file no longer declares the client (static)
    Given src/AppHost/Realms/smart-sentinel-eye-realm.json
    Then no entry in "clients" has clientId "smart-sentinel-eye-web"

  Scenario: A token holding neither the bundle nor sse.variables.read is refused (SC-1, preserved)
    Given a planted public client granting exactly sse-identity, sse-audience, sse-groups, sse.audit.read
    And a token minted from it for the operator
    When the token calls GET /system-variables
    Then the answer is 403

  Scenario: The same token still reads audit (SC-2, preserved control)
    Given the planted client and token of SC-1
    When the token calls GET /audit?pageSize=1
    Then the answer is 200

  Scenario: A caller without sse.layouts.write cannot create a layout (TileSpan pin, preserved)
    Given a planted client granting no sse.layouts.* scope
    When a token minted from it for admin POSTs /layouts with a valid body
    Then the answer is 403

  Scenario: No caller at all is 401, not 403 (SC-8, unchanged — bad request boundary)
    When an anonymous caller calls GET /system-variables
    Then the answer is 401
```

The conflict case has no analogue here: nothing is versioned or concurrently written. The
"bad-request" boundary is SC-8's 401 (no caller) against SC-1's 403 (wrong scope), unchanged.

### Edge Cases

- **A persisted developer Keycloak volume keeps the client.** `WithRealmImport` runs once; a
  developer whose volume predates this change still has `smart-sentinel-eye-web` until they
  delete the volume (the AppHost comment at `:157-160` already says so). CI boots fresh.
  Recorded, not fixed — there is no production deployment (ADR-0118) and no realm migration
  mechanism to fix it with.
- **Planted clients left behind.** A test that throws before `finally` or a failed delete leaves
  a probe client in the realm. `RealmProbe.DeleteAsync` already fails loudly on a non-success
  delete; each planted client id is unique (`Guid.CreateVersion7()`), so a residue cannot
  collide with a later run.
- **The e2e comment's hypothetical regression** (`auth.ts` reverting to
  `smart-sentinel-eye-web`) now fails sign-in outright instead of producing a bundle-carrying
  token — a louder failure than the one the comment describes. The comment is left as is.
- **The refusal's exact wording.** Keycloak's answer to an unknown `client_id` on the password
  grant is expected to be `401 {"error":"invalid_client"}`. Phase 4a observes it on the running
  stack; if the running Keycloak answers differently, the fact pins what is **observed** (status
  and `error` code), never "not 200" — a wrong password (`invalid_grant`) must not satisfy it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The realm file MUST NOT declare a client with `clientId`
  `smart-sentinel-eye-web`. The entry is **deleted**, not set to `enabled: false` (plan §D1).
- **FR-002**: A password grant for `smart-sentinel-eye-web` against a freshly imported realm MUST
  be refused with the observed `invalid_client` refusal, while the same credentials succeed for
  `management-web` in the same test.
- **FR-003**: SC-1 and SC-2 MUST keep asserting the same API answers (403 on
  `/system-variables`, 200 on `/audit`) for a token of the same scope shape, minted from a
  client the test plants and deletes.
- **FR-004**: `TileSpanIntegrationTests.A_caller_without_sse_layouts_write_is_refused_403` MUST
  keep asserting 403, minted from a planted client that grants no `sse.layouts.*` scope.
- **FR-005**: SC-3, SC-4 and SC-8 MUST NOT change.
- **FR-006**: No other realm client changes. In particular `management-web`'s
  `directAccessGrantsEnabled` stays (spec 200 §Out of scope; #2285 owns it) and its description
  string is left as is.
- **FR-007**: Comments and the `BrowserKioskPrincipalTests` `azp` literal that mention the client
  historically are left untouched; only the three rewritten facts' comments and the one
  `LegacyBundleGrantTests` sentence that points at SC-1 change.

### Key Entities

- **Realm client `smart-sentinel-eye-web`**: removed.
- **Probe client** (test-only, per fact): public, password grant, the default scopes the fact
  needs, unique id, deleted in `finally`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-A**: `git grep -n '"clientId": "smart-sentinel-eye-web"' src` returns nothing.
- **SC-B**: The new runtime fact is observed **red** before the realm edit (Keycloak mints a
  token) and green after, with the quoted failure in the PR body.
- **SC-C**: The new static fact is observed **red** before the realm edit and green after.
- **SC-D**: The reworked SC-1, SC-2 and TileSpan facts are observed **green against the unedited
  realm** and pass **unmodified** after the realm edit.
- **SC-E**: `git grep -n smart-sentinel-eye-web -- src tests` returns only the new negative facts,
  the `BrowserKioskPrincipalTests` literal, and comments.

## Latency (constitution §IV)

**N/A.** Realm configuration and test fixtures only. No leg of the event→overlay path is
touched: token issuance happens at sign-in, not per event, and no client on the path
(`management-web`, `kiosk-web`, `kiosk-wall`) changes.

## Assumptions

- The `operator` user can sign into a newly planted public client and receives the same
  `/fabs/<id>` groups as through the retired client — the planted client carries `sse-groups`,
  and `EventTypeRegistryAuthorizationIntegrationTests` already signs a seeded user into a planted
  client the same way.
- Keycloak's refusal of an unknown client is `401 invalid_client` (to be observed at 4a; see Edge
  Cases).
