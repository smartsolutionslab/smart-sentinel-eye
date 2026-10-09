# Tasks 327 — The admin token that needs no password

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2511 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing). The harness's admin principal changes from the `admin`
user via `management-web` ROPC to the `integration-test-admin` service account. A new realm
client and a new fixture capability (`GetAdminAccessTokenAsync`) appear. Plan §5 T-B fact 2 is
the declared characterisation half: it is green before and after and stays unmodified. The 393
migrated sites are a regression net and keep every assertion unmodified.
**Engineer**: `test-writer` for 4a and the 4b guards, then `infra-engineer` for 4b (realm JSON,
fixture, call-site sweep). No `src/` code outside the realm import, so no backend or frontend
engineer is needed.
**Reviewers**: `infra-reviewer` + `security-reviewer` (a new confidential principal in the
identity provider).
**Tracking**: feature-level issue #2511, already on Project #13 (In Progress). No per-task issues.
**New ADR**: none (plan §1).
**Gate question D1** (plan §7): `Closes #2511` with the follow-up filed is recommended; the
alternative is `Refs #2511`.

Format: `[ID] [P?] [Story] description`. Live tasks need a full Aspire boot on a **fresh**
`keycloak-data` volume, with one stack per machine.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T001 [P] [US1]** Create `tests/Architecture.Tests/IntegrationTestAdminClientTests.cs`
  per plan §5 T-A, facts 1–6. The client id is a local const. Groups are compared against the
  `admin` user read from the file. Facts 2–5 must fail with an "absent" reason, never pass
  vacuously.
- [ ] **T002 [P] [US1]** Create `tests/Integration.Tests/Identity/AdminHarnessTokenIntegrationTests.cs`
  per plan §5 T-B, facts 1–4 (control-first shape in 3 and 4). Use only existing fixture members.
  Add its `FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.AdminHarnessTokenIntegrationTests.`
  entry to the shortest `tests/Integration.Tests/ci-shards/shard-N.filter`.
- [ ] **T003 [US1]** Run, on unchanged production files:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~IntegrationTestAdminClientTests"`
  and `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AdminHarnessTokenIntegrationTests"`.
  **Required:** T-A 1–6 red (absent), T-B 1 red (`azp` is `management-web`), T-B 3/4 red (the
  control `client_credentials` gets `invalid_client`), T-B 2 green. No compile error, no boot
  failure. Anything else: stop and report. Commit
  `test(identity): prove the admin harness still mints its token through the console's password grant`.

Depends: T001, T002 (disjoint files) → T003.

## Phase 4b — implement (infra-engineer; the 4a tests are read-only)

- [ ] **T004 [US1]** `src/AppHost/Realms/smart-sentinel-eye-realm.json`: add the
  `integration-test-admin` client after `identity-admin` and its service-account user after
  `service-account-identity-admin` (plan §2). Copy `defaultClientScopes` verbatim from
  `management-web`. Description ≤ 255 characters (count it). Keep the encoding and BOM. Touch
  nothing else.
- [ ] **T005 [US1]** `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs` per plan §3:
  `HarnessClientId` / `HarnessClientSecret` consts, `GetAdminAccessTokenAsync`,
  `CreateAdminClientAsync` re-pointed, the shared `RequestTokenAsync` extraction with a
  grant-naming error message, and a doc comment on `ClientId`. New code adds no `ConfigureAwait`
  (ADR-0049). The file's existing `.ConfigureAwait(false)` calls are left alone, because sweeping
  them would be a drive-by change.
- [ ] **T006 [US1]** Migrate the 19 sites in plan §4 row 2 to `aspire.GetAdminAccessTokenAsync()`.
  Recount first with a multiline search. Reword `TokenAudienceIntegrationTests` fact 1's
  `customMessage` only (plan §4 row 3). **No assertion edits anywhere.**
  Recommended: commit T004–T006 together as
  `fix(2511): mint the integration harness's admin token by client_credentials` (plan §6, the
  folded option), so no commit carries two admin identities.
- [ ] **T007 [P] [US1]** (test-writer, after T005) Append plan §5 T-C (two mirror facts) to
  `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs` and T-D
  (`Every_admin_accessor_presents_the_same_subject`) to
  `AdminHarnessTokenIntegrationTests.cs`. Observe each red by its counterfactual (plan §5),
  quote the output, revert. Commit
  `test(identity): guard the harness client's secret and single admin identity`.
  It is `[P]` with T006 because the files are disjoint.
- [ ] **T008 [US1]** Delete the `keycloak-data` volume. Re-run T003's commands: all green, 4a
  files unmodified. Run the T007 facts. Run `StaleIdempotencyReservationIntegrationTests`. Run
  the **full** `tests/Integration.Tests` suite (all four shards) and `tests/Architecture.Tests`.
  Run the Release build (`TreatWarningsAsErrors`). Any red migrated site: apply spec A1 (move it
  to the persona path with a stated reason, or stop) and **never** edit its assertion.

Depends: T003 → T004 → T005 → T006 → T008; T005 → T007 → T008.
T001 ∥ T002. T006 ∥ T007 (disjoint files). Nothing here can fan out further: T004+T005 are
the foundation every admin site rides on.

## Phase 5 — verify

- [ ] **T009 [US1]** Run spec §Independent e2e procedure steps 1–5 on a fresh stack and record the
  decoded token claims. Quote the counterfactual outputs (T007). Record A1/A2/A3 as observed.
  Latency: N/A.

## Phase 6–7

- [ ] **T010** Run `/code-review` + `security-review` (new confidential principal: check its
  scope/group/role bounds against T-A). File the follow-up issue per plan §7 (`agent:blocked`,
  Project #13, cross-referencing #2285, #2488 and #2511). Open the PR to `develop` per gate D1
  (`Closes #2511` recommended) and quote T003's red output and T007's counterfactuals.
