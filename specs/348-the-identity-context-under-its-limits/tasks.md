# Tasks 348: The identity context under its limits

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2890 (feature-level;
no per-task issues will be created).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T007 must be
seen **passing on the unrefactored code** before any Phase 3 task starts, and must pass
**unmodified** after. An assertion that has to be edited after a refactor commit is evidence the
behaviour moved: block, do not adjust. A characterisation test that arrives red before any
production edit is a test defect and is fixed in the test.

**Engineer:** backend-engineer. **Test-writer:** T001-T007. **Phase 6:** code-review **and**
security-reviewer (plan §7).

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each
commit must build **and pass** on its own: test commits add green tests against unchanged
production code; each refactor commit leaves every suite of plan §5 green and runs the §5 grep.

**Forbidden in every task:** any file under `Api/` or `Migrations/`; any existing test file or
fake; both `Log.cs`; any Domain file; `Shared.Contracts`; `AddKeycloakAdminClient`,
`ConfigureHttpClient`, `ConfigureMessageHandling`; suppressions, thresholds, `.editorconfig`,
`SonarLint.xml`, guards.

Legend: `[P]` = owns disjoint files from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 grep (16 lines on `ed702aa5`) and the
  four `dotnet test` runs (Domain 54, Application 157, Infrastructure 63, Architecture).

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [US1] C1, `tests/Identity.Application.Tests/Fakes/RecordingIdentityPorts.cs`
  (shared ordered call log over the existing fakes, by delegation). Blocks T002-T004.
  Commit `test(identity): add an ordered call log over the handler ports`.
- [ ] **T002** [P] [US1] C2, `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`.
  Commit `test(identity): pin webhook rotation call order and exception filters`.
- [ ] **T003** [P] [US1] C3, `tests/Identity.Application.Tests/Commands/ClientRegistrationOrderCharacterisationTests.cs`.
  Commit `test(identity): pin kiosk enrolment and device registration order and text`.
- [ ] **T004** [P] [US1] C4, `tests/Identity.Application.Tests/KeycloakAdmin/OrphanedClientSweepTallyCharacterisationTests.cs`.
  Commit `test(identity): pin orphan sweep unreachable order and logs`.
- [ ] **T005** [P] [US1] C5, `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KeycloakRequestSequenceCharacterisationTests.cs`.
  Commit `test(identity): pin the Keycloak admin request sequences`.
- [ ] **T006** [P] [US1] C6, `tests/Architecture.Tests/Persistence/IdentityRegistrationCharacterisationTests.cs`.
  Commit `test(architecture): pin identity infrastructure registrations`.
- [ ] **T007** [P] [US1] C7, `tests/Identity.Infrastructure.Tests/IdentityModelCharacterisationTests.cs`.
  Commit `test(identity): pin the EF model's logical shape`.

Each: run the owning project's `dotnet test -c Release --filter "FullyQualifiedName~<Class>"` and
return the verbatim output, all green on unrefactored code, plus one stated counterfactual per
file (a production mutation, reverted, that turned it red).

Gate: the orchestrator holds seven verbatim green outputs.

## Phase 3: Refactor (backend-engineer; depends on all of Phase 2)

- [ ] **T010** [P] [US1] `Application/Commands/Handlers/EnrollKioskCommandHandler.cs`: plan §3.1. Hit 1.
  Commit `refactor(identity): extract kiosk enrolment validation and representation`.
- [ ] **T011** [P] [US1] `Application/Commands/Handlers/RegisterDeviceCommandHandler.cs`: plan §3.2. Hit 2.
  Commit `refactor(identity): extract device registration validation and representation`.
- [ ] **T012** [US1] `Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`, step 1:
  plan §3.3. Hit 6. Commit `refactor(identity): extract the failed race-disable acknowledgement`.
- [ ] **T013** [US1] same file, step 2: plan §3.4. Hits 4, 5. Depends on T012.
  Commit `refactor(identity): split webhook rotation HandleAsync by phase`.
- [ ] **T014** [P] [US1] `Application/KeycloakAdmin/OrphanedClientSweep.cs`: plan §3.5. Hits 7, 8.
  Commit `refactor(identity): carry the orphan sweep tally as one object`.
- [ ] **T015** [P] [US1] `Infrastructure/IdentityInfrastructureModule.cs`: plan §3.6. Hit 11.
  Commit `refactor(identity): group infrastructure registrations`.
- [ ] **T016** [US1] `Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs`: plan §3.7 step 1. Hit 13.
  Commit `refactor(identity): extract the existing-client refusal from CreateClientAsync`.
- [ ] **T017** [US1] same file: plan §3.7 step 2. Hit 14. Depends on T016.
  Commit `refactor(identity): extract sub-group paging`.
- [ ] **T018** [US1] same class: plan §3.7 step 3, `partial` split into
  `HttpKeycloakAdminClient.cs` + `.Realm.cs` + `.Sweeps.cs` + `.WireRows.cs` (moves only). Hit 12.
  Depends on T017. Commit `refactor(identity): split the Keycloak admin client by what it holds`.
- [ ] **T019** [P] [US1] `Infrastructure/Persistence/Configurations/RegisteredClientConfiguration.cs`:
  plan §3.8. Hit 16. Commit `refactor(identity): split registered client EF configuration`.

Per task: run plan §5's grep (the task's hits gone, no new hit) and the test suites of the touched
layer plus `Architecture.Tests` (T015-T019: also `Identity.Infrastructure.Tests`).

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] After evidence, verbatim: the §5 grep (exactly the 4 "Leave" rows), all four
  test runs, `dotnet build -c Release` (0 errors), `dotnet format --verify-no-changes` on
  Application and Infrastructure, coverage (Domain >= 90 %, Application >= 80 %). Confirm
  `git diff <T007 commit>..HEAD -- tests/` is empty and
  `git diff origin/develop..HEAD --name-only | grep -E '/Api/|/Migrations/|/Domain/|Log\.cs$'` is empty.
- [ ] **T021** Phase 6: code-review and security-reviewer (plan §7 checklist), findings resolved.
- [ ] **T022** PR to `develop` (`--base develop`). Body: T000 and T020 outputs, the 4 left hits
  each with its reason (spec §2), Phase 4a colour "characterisation" with T001-T007 outputs, the
  J1-J3 judgement calls, the three oddities left unchanged (spec §6), latency "N/A",
  `Closes #2890`. Re-check spec number 348 is still free. Integration.Tests observed in CI.

## Dependency graph

```
T000
T001 ──> T002, T003, T004 ; T005, T006, T007 (all [P]) ──>
  T010, T011, T012 ──> T013 ; T014, T015, T016 ──> T017 ──> T018 ; T019 ──> T020 ──> T021 ──> T022
```

Phase 3 tasks own disjoint files except T012 -> T013 (same file) and T016 -> T017 -> T018 (same
class). Nothing here is foundational to another context: no `Shared.Kernel`, `Shared.Contracts`,
`AppHost` or Aspire resource change.
