# Tasks 325 — The guess space the lockout outlasts

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2510 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: the dev realm refuses passwords it admitted). Plan §5
separates the red-first assertions from the data updates; SC-1 and AS-3 are green before and
after, unmodified.
**Engineer**: `test-writer` (4a) then `infra-engineer` (4b — realm, test fixtures, docs) with the
four e2e sites done by the same engineer (four one-line value swaps; a separate
`frontend-engineer` pass would cost more coordination than it saves).
**Reviewers**: `infra-reviewer` + `security-reviewer` (identity-provider credential policy).
**Tracking**: feature-level issue #2510 (already on Project #13). No per-task issues.
**New ADR**: none (plan §1).
**Gate question D1** (spec §Decision): twelve distinct seeded values — recommended; fallback in
plan §7.

Format: `[ID] [P?] [Story] description`. Live tasks need a full Aspire boot on a **fresh**
`keycloak-data` volume; one stack per machine.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T001 [P] [US1]** Rewrite `tests/Architecture.Tests/SeededCredentialStrengthTests.cs` per
  plan §5 test 1 (inverted census / derivation / reuse facts, retired-values-refused fact,
  declared-length ≥ 15 and clause-presence fact, `notContainsUsername` + `notEmail` modelled,
  `notUsername` case-insensitive, SC-4 counterfactuals ≥ 15 chars; SC-1 untouched).
- [ ] **T002 [P] [US1]** Create `tests/Architecture.Tests/RetiredSeededPasswordTests.cs` per plan
  §5 test 3 (slash-normalised paths; excludes `bin/`, `obj/`, `node_modules/`, historical
  `specs/*/{spec,plan,tasks,verification}.md`, `scripts/`).
- [ ] **T003 [P] [US1]** Create `tests/Integration.Tests/Identity/PasswordPolicyEnforcementIntegrationTests.cs`
  per plan §5 test 2 (AS-1, AS-2 ×3 + control, AS-3), and add its
  `FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.PasswordPolicyEnforcementIntegrationTests.`
  entry to `tests/Integration.Tests/ci-shards/shard-2.filter`.
- [ ] **T004 [US1]** Run on unchanged production files:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~SeededCredentialStrengthTests|FullyQualifiedName~RetiredSeededPasswordTests"`
  and `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~PasswordPolicyEnforcementIntegrationTests"`.
  **Required**: every new/inverted fact red for the reason plan §5 names; SC-1, SC-4's
  "refuses for that specific clause" shape, and AS-3 green; no compile error, no fixture boot
  failure. Any other outcome → stop and report. Commit
  `test(identity): prove the dev realm admits passwords its lockout cannot outlast`.

Depends: T001, T002, T003 (disjoint files) → T004.

## Phase 4b — implement (infra-engineer; 4a tests are read-only)

- [ ] **T005 [US1]** `src/AppHost/Realms/smart-sentinel-eye-realm.json`: policy string (plan §2
  header) and the twelve credential values (plan §3). Keep the BOM; touch nothing else.
- [ ] **T006 [US1]** Create `tests/Integration.Tests/Fixtures/SeededCredentials.cs` (one
  `public const string` per account, plan §3); re-point `AspireFixture.Auth.cs:11`
  `AdminPassword` to `SeededCredentials.Admin`.
- [ ] **T007 [US1]** Replace the literal in each of the 54 `tests/Integration.Tests` files
  (`git grep -lE 'Admin1234|Operator1234|Wall-[a-z]+-1234' -- tests`) with the matching
  `SeededCredentials` member, splitting any const shared between two usernames. Update the
  policy-string comments and failure messages in the three lockout probe tests (plan §4). No
  assertion changes.
- [ ] **T008 [US1]** `e2e/support/sign-in.ts:16`, `e2e/support/kiosk-session.ts:22`,
  `e2e/kiosk-reconciliation.spec.ts:71` → `operator`'s value; `e2e/support/wall-session.ts:20` →
  `wall-munich`'s. Run `prettier --check` and `tsc --noEmit` on `e2e/`.
- [ ] **T009 [US1]** Fresh volume; re-run T004's commands (all green, test files unmodified);
  full `tests/Integration.Tests` (all shards) and the e2e suite; Release build. Commit T005–T008
  together as `fix(2510): raise the dev realm password policy and reseed distinct credentials`
  — one commit, because a realm and consumers that disagree fail every sign-in (plan §6).
- [ ] **T010 [P] [US1]** `README.md:79`, `:352` (now two different values) plus one sentence
  that a persistent stack must drop `keycloak-data`; the 13 occurrences in the nine
  `specs/*/quickstart.md` (plan §4). Commit `docs(2510): …` or fold into T009's commit.
  `[P]` with T005–T008: disjoint files.

Depends: T004 → T005 → T006 → T007; T005 → T008; T007, T008 → T009. T010 any time after T004.
The T007 file set may be fanned out across sub-agents by directory (`AuditObservability/`,
`CameraCatalog/`, `EventIngestion/`, …) only *after* T006 lands — `SeededCredentials` is the
blocking foundation.

## Phase 5 — verify

- [ ] **T011 [US1]** Spec §Independent e2e procedure on a fresh stack; plan §5 counterfactuals
  (revert one value → tests 1 + 3 red; revert the policy → test 1 red), quoted; record A1 (does
  the import validate the policy?) per plan §8. Latency: N/A.

## Phase 6–7

- [ ] **T012** `/code-review` + `security-review`; file follow-ups for `passwordBlacklist` and the
  master realm's missing `passwordPolicy` (spec §Out of scope). PR to `develop` with
  `Closes #2510`; quote T004's red output.
