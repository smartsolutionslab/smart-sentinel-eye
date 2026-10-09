# Spec 324 — The door the lockout left open

**Issue:** [#2508](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2508)
— *The Keycloak master realm has no brute-force protection and holds the bootstrap admin-cli
account*. Found during spec 207's phase 6 security review (issue #2285,
`specs/207-a-guess-that-runs-out/spec.md` §Out of scope). Picked up by the autonomous lane
(ADR-0144); #2508 is the feature-level tracking issue. No new issue, no per-task issues.

**Spec number.** Checked 2026-10-09 against `origin/develop` (highest: 323), every local and
remote branch (`git ls-tree --name-only <ref> specs/`), every open PR's head branch, and every
`D:\Github\sse-*` worktree. Nothing claims 324. Re-check before the PR is opened.

**ADRs referenced:**

- **ADR-0000 rows 007–008** — Keycloak (OIDC) is the identity provider.
- **ADR-0159** — one realm per deployment. `master` is Keycloak's built-in administration realm,
  not an application realm; configuring it does not add a second application realm and does not
  touch ADR-0159's fab-as-group model.
- **ADR-0103** — integration tests run against the real Aspire stack via `AspireFixture`.
- **ADR-0036** — smallest change: one new realm-import file, one comment update.
- **ADR-0139 / ADR-0144** — behaviour-changing, so phase 4a is **red**.

**No new ADR.** The mechanism (plan §1) extends the existing declarative `WithRealmImport` path
already used for `smart-sentinel-eye` (spec 207). It introduces no new owner, no startup task, no
AppHost lifecycle hook. The rejected alternative (a startup Admin API call) *would* have needed an
ADR — plan §1 records why.

## Decision (recorded, not reopened)

User comment on #2508, **2026-10-08**:

> enable brute-force protection on the Keycloak master realm to match the main realm — the
> bootstrap admin-cli account is a high-value target even if rarely used day-to-day.

"Match the main realm" is read as: every brute-force field on `master` equals its value in
`smart-sentinel-eye-realm.json` (`bruteForceProtected: true`, `permanentLockout: false`,
`failureFactor: 10`, `waitIncrementSeconds: 60`, `maxFailureWaitSeconds: 900`,
`maxDeltaTimeSeconds: 43200`, `quickLoginCheckMilliSeconds: 1000`,
`minimumQuickLoginWaitSeconds: 60`). Nothing else about `master` is meant to change.

## Context — premise verified live (2026-10-09)

Probed against a throwaway `quay.io/keycloak/keycloak:26.6.4` container (the pinned image,
`AppHost.cs:152`), booted the way Aspire boots it (`start-dev --import-realm`,
`KC_BOOTSTRAP_ADMIN_*`, the real `smart-sentinel-eye-realm.json` mounted at
`/opt/keycloak/data/import`):

- **Premise holds.** `GET /admin/realms/master` → `"bruteForceProtected":false`,
  `"failureFactor":30`. The bootstrap `admin`/`admin-cli` password grant accepts unlimited attempts.
- **A `master-realm.json` in the import directory is honoured** — log:
  `Realm 'master' imported` (before `smart-sentinel-eye`), then
  `KC-SERVICES0077: Created temporary admin user with username admin`. Keycloak's bootstrap skips
  creating `master` when the import set contains it, and imports it instead.
- **A naive partial file breaks the admin account.** With only
  `{realm, enabled, bruteForce*}`, the correct admin password is refused:
  `error="resolve_required_actions", reason="Account is not fully set up"`. Cause: Keycloak's
  bootstrap creates `master` with a user profile in which no attribute is required; an *imported*
  realm gets the standard new-realm profile (email/firstName/lastName required), and
  `VERIFY_PROFILE` then blocks the password grant for the attribute-less temporary admin. The
  integration suite's own master-admin reads (`BruteForceLockoutIntegrationTests`
  `MasterRealmAdminClientAsync`) would have broken.
- **With bootstrap parity it works.** Adding the bootstrap user-profile component (plan §2) → admin
  grant 200, admin reads `smart-sentinel-eye` 200, master client list identical to bootstrap.
  Remaining realm-representation diff vs bootstrap: `accessTokenLifespan` 60 (bootstrap) vs 300
  (import default) and `displayName`/`displayNameHtml` — both carried in the file (plan §2) so the
  only intended difference is the brute-force block.

## User Scenarios & Testing

### User Story 1 — Password guessing against the bootstrap admin is throttled (P1)

As the operator of a deployment, I want the Keycloak `master` realm to lock an account after
repeated wrong passwords exactly as the application realm does, so that the server's most powerful
credential cannot be guessed at unlimited rate.

**Why this priority:** it is the whole feature; a token from `master`'s admin can rewrite every
realm, including turning spec 207's protection back off.

**Independent test:** boot the stack fresh; read both realms' brute-force fields as the bootstrap
admin and compare; lock a throwaway `master` account and observe its correct password refused.

**Acceptance scenarios**

```gherkin
Feature: master realm brute-force protection

  Background:
    Given the stack was booted against a fresh keycloak-data volume

  Scenario: master's brute-force settings match the application realm (happy)
    When the bootstrap admin reads admin/realms/master and admin/realms/smart-sentinel-eye
    Then master.bruteForceProtected is true
    And each of permanentLockout, failureFactor, waitIncrementSeconds, maxFailureWaitSeconds,
        maxDeltaTimeSeconds, quickLoginCheckMilliSeconds, minimumQuickLoginWaitSeconds
        on master equals the same field on smart-sentinel-eye

  Scenario: a master account is locked after too many wrong passwords (conflict)
    Given a throwaway account "lockout-probe-<guid>" exists in the master realm
    When it makes failureFactor + 1 password grants to admin-cli with a wrong password
    And it then makes one grant with its correct password
    Then that grant is refused with error "invalid_grant"
    And no access token is returned

  Scenario: a wrong password is refused without a token (bad request)
    Given a throwaway account exists in the master realm
    When it makes one password grant with a wrong password
    Then the response is 401 "invalid_grant" and carries no access token

  Scenario: the bootstrap admin still authenticates and master keeps bootstrap token lifetime (auth / guard)
    When the bootstrap admin makes a password grant to admin-cli with the configured KeycloakPassword
    Then it receives an access token
    And admin/realms/master reports accessTokenLifespan 60
```

The last scenario is a **guard, green before and after** — it is what catches the naive-partial-file
failure found above. It must never lock the `admin` account itself: the shared `AspireFixture`
depends on it.

### Edge cases

- **Persistent dev volumes.** `WithRealmImport` runs only against a fresh `keycloak-data` volume
  (`IGNORE_EXISTING`; `AppHost.cs:156-160` comment). A developer box with an existing volume keeps
  an unprotected `master` until the volume is dropped — the same caveat that already applies to
  every realm-file edit. CI and `AspireFixture` boot fresh. Documented, not engineered around.
- **Keycloak image bump.** The bootstrap-parity fields are copied from 26.6.4. The pin
  (spec 195, `AppHost.cs:139-153`) means a bump is a deliberate diff, and the guard scenario fails
  loudly if a new version's bootstrap differs in a way that breaks the admin grant.
- **Admin lockout as denial of service.** An attacker can now temporarily lock `admin` with wrong
  guesses (≤ 900 s, `permanentLockout: false`). Accepted by the decision ("match the main realm");
  it is the same trade spec 207 accepted for every application user.

## Requirements

- **FR-001** On a fresh boot, `master.bruteForceProtected` is `true`.
- **FR-002** `master`'s seven other brute-force fields equal `smart-sentinel-eye`'s.
- **FR-003** The bootstrap `admin`/`admin-cli` password grant with the configured password keeps
  succeeding.
- **FR-004** `master`'s other observable settings stay at Keycloak's bootstrap values
  (`accessTokenLifespan` 60, display names, user profile with no required attributes, client list).

## Success criteria

- **SC-1** The happy and conflict scenarios fail on `develop` and pass after the change.
- **SC-2** The guard scenario passes on `develop` and passes unmodified after the change.
- **SC-3** Every existing `Identity` integration test that uses the master admin stays green.

## Locked tech choices

Keycloak 26.6.4 (pinned), Aspire `WithRealmImport` (existing), realm-representation JSON,
xUnit + Shouldly integration tests on `AspireFixture` (ADR-0052, ADR-0103).

## Latency budget impact

**N/A.** Identity-provider configuration on the administrative login path; no leg of the
event→overlay path (constitution §IV) is touched.

## Out of scope

- Production Keycloak hardening beyond this flag (TLS, admin-console exposure, rotating the
  bootstrap admin to a permanent one) — the k8s publisher has never run (CLAUDE.md, audit.md:373).
- Re-importing `master` onto existing persistent volumes.
