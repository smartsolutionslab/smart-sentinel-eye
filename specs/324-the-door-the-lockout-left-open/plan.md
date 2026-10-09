# Plan 324 — The door the lockout left open

**Spec**: [spec.md](spec.md) · **Issue**: #2508 · **Phase**: 2 (Plan)

## 1. Mechanism — decided: a `master` realm import file

Two candidates were named on the issue. Both were investigated against the pinned image.

**Chosen: `src/AppHost/Realms/master-realm.json`.** `WithRealmImport("../AppHost/Realms")` mounts
the whole directory and Keycloak imports every `*-realm.json` in it. Proven live on 26.6.4
(spec §Context): when the import set contains `master`, Keycloak's bootstrap does not create it and
imports the file instead, then creates the temporary admin into it. This is the existing
declarative mechanism spec 207 used — no new owner, no new lifecycle, no readiness logic. Constraint
it brings: the file must carry Keycloak's bootstrap-only master settings (§2), or the admin account
breaks.

**Rejected: a startup Admin API call** (`PUT /admin/realms/master`). It would need an owner that
does not exist today — an AppHost `ResourceReady` hook mutating an external system, or a dedicated
worker — plus wait/retry for Keycloak readiness. That is a new pattern ("where startup-time
infrastructure hardening lives") and would require an ADR, which the autonomous lane may not write.
Its one advantage — reaching already-persistent dev volumes — does not apply to CI, the fixture, or
any production deployment (none exists). Not needed.

**Partial-import API** (`POST /admin/realms/{realm}/partialImport`) was also considered: it imports
users/clients/roles/groups/IdPs only, not realm-header fields such as `bruteForceProtected`, and is
an Admin API call anyway. Not viable.

## 2. The file — `src/AppHost/Realms/master-realm.json`

Context: AppHost (infrastructure composition), no bounded-context code. No entities, value objects,
events or contracts. UTF-8 with BOM to match `smart-sentinel-eye-realm.json`.

Contents, and nothing else:

| Field | Value | Why |
|---|---|---|
| `realm` | `"master"` | target |
| `enabled` | `true` | |
| `displayName` | `"Keycloak"` | bootstrap parity |
| `displayNameHtml` | `"<div class=\"kc-logo-text\"><span>Keycloak</span></div>"` | bootstrap parity |
| `accessTokenLifespan` | `60` | bootstrap parity — import default is 300, a 5x longer admin token |
| `bruteForceProtected` | `true` | **the decision** |
| `permanentLockout` | `false` | match main realm |
| `failureFactor` | `10` | match main realm |
| `waitIncrementSeconds` | `60` | match main realm |
| `maxFailureWaitSeconds` | `900` | match main realm |
| `maxDeltaTimeSeconds` | `43200` | match main realm |
| `quickLoginCheckMilliSeconds` | `1000` | match main realm |
| `minimumQuickLoginWaitSeconds` | `60` | match main realm |
| `components` | user-profile provider, below | bootstrap parity — **load-bearing** |

The user-profile component reproduces the profile Keycloak 26.6.4's bootstrap gives `master`
(no attribute `required`). Captured verbatim from `GET /admin/realms/master/users/profile` on a
bootstrap 26.6.4 container on 2026-10-09; embed it as the single string element of
`kc.user.profile.config`:

```json
"components": {
  "org.keycloak.userprofile.UserProfileProvider": [
    { "providerId": "declarative-user-profile",
      "config": { "kc.user.profile.config": [ "<the JSON below, as one escaped string>" ] } }
  ]
}
```

```json
{"attributes":[{"name":"username","displayName":"${username}","validations":{"length":{"min":3,"max":255},"username-prohibited-characters":{},"up-username-not-idn-homograph":{}},"permissions":{"view":["admin","user"],"edit":["admin","user"]},"multivalued":false},{"name":"email","displayName":"${email}","validations":{"email":{},"length":{"max":255}},"permissions":{"view":["admin","user"],"edit":["admin","user"]},"multivalued":false},{"name":"firstName","displayName":"${firstName}","validations":{"length":{"max":255},"person-name-prohibited-characters":{}},"permissions":{"view":["admin","user"],"edit":["admin","user"]},"multivalued":false},{"name":"lastName","displayName":"${lastName}","validations":{"length":{"max":255},"person-name-prohibited-characters":{}},"permissions":{"view":["admin","user"],"edit":["admin","user"]},"multivalued":false}],"groups":[{"name":"user-metadata","displayHeader":"User metadata","displayDescription":"Attributes, which refer to user metadata"}]}
```

Without this component the admin grant fails `resolve_required_actions` (observed). Do **not** add
`clients`, `roles`, `users` or `requiredActions` — Keycloak's import fills defaults for those and the
probe showed the resulting client list identical to bootstrap; listing them would replace, not add.

No client `description` is added (memory: a >255-char description kills the realm import).

## 3. `src/AppHost/AppHost.cs` — comment only

The comment at lines 156-160 names only `smart-sentinel-eye-realm.json`. Update it to say the
directory holds two imports, that `master-realm.json` replaces Keycloak's bootstrap `master` and
must keep the bootstrap-parity fields (why, in one sentence), and that the persistent-volume
caveat applies to it too. **No code change** to the `AddKeycloak` chain.

## 4. Boundary rules

No project references change. No Shared.Contracts change. Architecture tests that read
`smart-sentinel-eye-realm.json` address it by filename (`RealmIdentityTests.cs:189`,
`RealmImportMirrorTests.cs:36`) and are unaffected by a second file.

## 5. Tests (phase 4a, red)

New class `tests/Integration.Tests/Identity/MasterRealmBruteForceIntegrationTests.cs`,
`[Collection(AspireCollection.Name)]`, primary-ctor `AspireFixture aspire`. New class rather than
extending `BruteForceLockoutIntegrationTests` (already 560 lines, ADR-0084 advisory 300). Mirror
that file's helper shapes (master admin client, throwaway probe user, lock loop, never interpolate a
token-bearing body into a failure message) as private helpers here — do not widen `RealmProbe` or
make the other class's helpers shared.

1. `The_master_realm_is_brute_force_protected_with_the_application_realms_settings` — **red.**
   Master-admin `GET admin/realms/master` and `admin/realms/smart-sentinel-eye`; assert master
   `bruteForceProtected == true`, then each of the seven fields equal across the two (read both
   from the running server — no hard-coded 10/60/900). Expected red: `bruteForceProtected` false.
2. `A_master_realm_account_is_refused_its_correct_password_after_too_many_wrong_ones` — **red.**
   Create `lockout-probe-<guid:N>` in **master** via `POST admin/realms/master/users` + reset
   password (temporary false); `failureFactor + 1` wrong grants to `realms/master/...token` with
   `admin-cli`; then the correct password → assert non-success, `error == "invalid_grant"`, no
   `access_token`. Delete the user in `finally`. Read `failureFactor` from master; if master's
   representation omits it, fail as an escalation (same rule as the sibling file). Expected red:
   the final grant returns 200. **Never** lock `admin`.
3. `The_bootstrap_admin_still_authenticates_and_master_keeps_its_bootstrap_token_lifespan` —
   **guard, green before and after, unmodified.** Admin grant 200 with an `access_token`;
   `admin/realms/master` → `accessTokenLifespan == 60`. Fails if the import file is missing the
   user-profile component or the lifespan.

Add the class to `tests/Integration.Tests/ci-shards/shard-2.filter` (beside
`BruteForceLockoutIntegrationTests`) — a missing entry fails CI deterministically.

Red is observed on `develop` code: tests 1 and 2 red for the stated reason, test 3 green, no
compile errors, sibling Identity tests green.

## 6. Verification (phase 5)

Fresh boot (drop `keycloak-data` if a persistent stack exists — needs the user, memory). As the
bootstrap admin: `GET /admin/realms/master` shows the brute-force block; a throwaway master user
locks after wrong guesses and is refused its correct password; the admin grant still works. Quote
responses in the PR with tokens redacted. Latency: N/A.

## 7. Risks

- Bootstrap parity is copied from 26.6.4. Mitigated by the pin and test 3.
- Persistent dev volumes stay unprotected until dropped (spec §Edge cases). Accepted.
