# Tasks 428 — The password grant only tests hold

**Colour**: **red** (behaviour-changing). `management-web` stops accepting a password grant
(AS-5), and the harness's password-grant principal moves to `integration-test-persona` (AS-1).
The migration of the call sites is plumbing that serves that change. It is not a separate
refactor (plan §5).
**Engineer**: `test-writer` for 4a, then `infra-engineer` for 4b (realm JSON, test harness, one
e2e file). No backend or frontend engineer is needed, because no `src/` C# and no app code changes.
**Feature issue:** #2809 (already on Project #13). No per-task issues.
**Foundational / blocking:** T001 (consts) blocks T002–T004. The realm client (T010) blocks every
4b task after it. The flag flip (T016) must come **last** in 4b: before it, a site not yet
migrated still works, and after it, that site fails.

Format: `[ID] [P?] [Story] description`. `[P]` means the task owns files disjoint from its siblings
(ADR-0109).

## Phase 4a — red (test-writer; return verbatim output; tests read-only to 4b)

- [ ] T001 [US1] In `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs`, add only
  `public const string PersonaClientId = "integration-test-persona";` and
  `public const string PersonaClientSecret = "dev-only-integration-test-persona-secret";`. Nothing
  consumes them yet. This is the one non-test line 4a writes, so that T003 compiles (plan §5).
- [ ] T002 [P] [US1/US2] New `tests/Architecture.Tests/PasswordGrantClientTests.cs`, with facts
  for I1–I5 (plan §3–§4: G-S, G-R, G-E). Client ids are local consts. Each fact asserts that the
  client exists before it reads any property off it. The e2e path is built from split segments.
- [ ] T003 [P] [US1] In `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs`, add
  `AspireFixture_PersonaClientId_names_a_client_the_import_seeds` and
  `AspireFixture_PersonaClientSecret_is_the_secret_the_import_seeds_for_that_client`, mirroring
  the `HarnessClient*` pair.
- [ ] T004 [P] [US1/US2] New `tests/Integration.Tests/Identity/PersonaClientIntegrationTests.cs`
  (`[Collection(AspireCollection.Name)]`), covering AS-1 (azp, sub, fab groups, scopes set-equal),
  AS-2 (401 `unauthorized_client`), AS-3 (`client_credentials` refused), and AS-5 (`management-web`
  password grant answers 400 `unauthorized_client`). Raw POSTs, not the throwing helpers. Add the
  class to `tests/Integration.Tests/ci-shards/shard-3.filter`.
- [ ] T005 [US1/US2] Run T002–T004 on a fresh Keycloak volume and return the verbatim output.
  **Required red:** G-S/G-E (client or consts absent), G-R (census names `management-web`), the
  mirror facts (no such client), AS-1/2/3 (`invalid_client`), and **AS-5 (observed 200)**. Any
  fact that arrives green stops the lane (ADR-0139).

## Phase 4b — implement (infra-engineer; 4a tests read-only)

- [ ] T010 [US1] Add `integration-test-persona` to `src/AppHost/Realms/smart-sentinel-eye-realm.json`
  exactly as plan §2 gives it (copy `management-web`'s 24 default scopes, keep the description
  ≤255 characters). Commit: `feat(identity): seed a dev-only password-grant client for test personas`.
- [ ] T011 [US1] `AspireFixture.Auth.cs`: rename `ClientId` to `ConsoleClientId`.
  `FetchAccessTokenAsync(user, pw)` posts to `PersonaClientId` with `client_secret`.
  `GetAccessTokenForClientAsync` gains an optional `clientSecret` (trailing, before the
  `CancellationToken`). Update the XML docs. All 111 + 11 persona sites move with this, without
  being edited.
- [ ] T012 [US1] Move the hand sites to `AspireFixture.PersonaClientId` and `PersonaClientSecret`:
  BruteForceLockout (2), FabGroupClaim (2), LockoutSessionSurvival (2, including the
  `refresh_token` POST, spec A2), LockoutThroughputMeasurement (3), PasswordPolicyEnforcement (1),
  ConsoleScopeGrant (3; rename SC-3/SC-4 to `A_console_scoped_token_…`, plan §6),
  EventTypeRegistryAuthorization (1). Fix the drifted `azp=management-web` comments listed in
  plan §6. One commit per file family is fine, but every commit must build.
- [ ] T013 [P] [US1] `tests/Integration.Tests/AuditObservability/RunModeStackAddress.cs`: drop the
  local `ClientId` and use the fixture consts plus `client_secret`. Its token-failure message names
  the stale-volume cause (plan §6).
- [ ] T014 [P] [US1] `e2e/wall-survives-a-lockout.spec.ts`: rename `MANAGEMENT_CLIENT_ID` to
  `PERSONA_CLIENT_ID` and add `PERSONA_CLIENT_SECRET`, sent as `client_secret` in both forms.
  Run `prettier --check`, eslint and `tsc --noEmit` on it (repo memory: eslint clean is not
  prettier clean).
- [ ] T015 [US1] Release build (`TreatWarningsAsErrors`), then the migrated integration suites
  listed in spec step 2 and the e2e pair. Everything is green before T016.
- [ ] T016 [US2] Set `management-web.directAccessGrantsEnabled` to `false`. Commit:
  `fix(identity): the console client no longer accepts a password grant`. Delete the Keycloak
  volume, then re-run T005's set and T015's set. **All green**, and AS-5 now shows 400
  `unauthorized_client` (confirms spec A1).
- [ ] T017 [US1/US2] Observe each counterfactual in plan §4 (G-S ×4, G-R ×2, G-M, G-E) red for
  the stated reason, then revert. Grep `src/` for `integration-test-persona` and confirm the only
  hit is the realm JSON. Record every result as observed, for the PR.

## Phase 5–7

- [ ] T020 Run `/verify`: `curl` the password grant against `management-web` (400) and against the
  persona client with its secret (200, `azp=integration-test-persona`), and sign in through the
  browser at :5173 (AS-6). Write the verification note.
- [ ] T021 `/code-review` and `/security-review` (realm auth change).
- [ ] T022 Open the PR to `develop` with `Closes #2809`. Quote T005's red and T016/T017's results,
  and state the volume-deletion note (plan §6).

## Dependencies

T001 → {T002, T003, T004} [P] → T005 → T010 → T011 → T012 → {T013, T014} [P] → T015 → T016 → T017 → T020–T022.
