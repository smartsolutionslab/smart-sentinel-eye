# Tasks 369: The bearer authentication composition under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2952 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001-T004 must be seen
**passing on unrefactored production code (`128df804`)** before T020 starts. After every refactor
commit they must pass **byte-unmodified**. An assertion that has to be edited after a refactor is
evidence the behaviour moved: block, do not adjust. A characterisation that is red before any
production edit is a test defect, and the fix goes in the test.

**Engineer:** backend-engineer (T020-T021). **Test-writer:** T001-T004. **Security-sensitive:**
every task, so Phase 6 runs `/security-review`.

**Forbidden in every task:** any production file other than `src/ServiceDefaults/AuthenticationDefaults.cs`;
any existing test file; `AddMetadataTransportCheck`, `RequiresHttpsMetadata`, `RefuseIfRevoked`,
`IssuedAtOf`, the constants; new suppressions, thresholds, `.editorconfig`, `SonarLint.xml`, csproj
files; methods outside spec §1 rows 1-2.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). On one
branch the commits still land one after another.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: `dotnet build src/ServiceDefaults -c Release
  --no-incremental` on `128df804` (7 S138 + 4 S107, rows 1-2 at 46 and 32), and the `dotnet test
  tests/ServiceDefaults.Tests -c Release` summary line.

## Phase 2: Characterisation (test-writer; no foundational task, all four are disjoint)

- [ ] **T001** [P] [US1] C1 `tests/ServiceDefaults.Tests/KeycloakAddressCharacterisationTests.cs`
  (spec §4.1).
- [ ] **T002** [P] [US1] C2 `tests/ServiceDefaults.Tests/BearerOptionsCharacterisationTests.cs`
  (spec §4.2).
- [ ] **T003** [P] [US1] C3 `tests/ServiceDefaults.Tests/BearerAuthenticationRegistrationCharacterisationTests.cs`
  (spec §4.3; keyed-safe render, plan §4; run twice, J2).
  Commit T001-T003: `test(service-defaults): pin the bearer composition's address, options and registrations`.
- [ ] **T004** [P] [US1] C4 `tests/ServiceDefaults.Tests/Revocation/RevocationCompositionCharacterisationTests.cs`
  (spec §4.4). Commit `test(service-defaults): pin the revocation composition's precedence, clients and health check`.
- [ ] **T010** Gate: T001-T004 green on production `128df804`, Release. Quote verbatim. Run plan
  §5.2's counterfactuals, quote each red, and revert them (`git status` clean in `src/`).

## Phase 3: G1 (backend-engineer; after T010)

- [ ] **T020** [US1] `AddBearerAuthentication` → `KeycloakBaseUrl`, `AddJwtBearerScheme`,
  `AddRevocationHook`, `AddExceptionHandlers` (plan §3 G1), contiguous runs, comments verbatim.
  Commit `refactor(service-defaults): split the bearer authentication composition under thirty lines`.

## Phase 4: G2 (backend-engineer; after T020)

- [ ] **T021** [US1] `AddRevocationCheck` → `AddRevocationListOptions`, `AddRevocationListClients`
  (plan §3 G2), the pragma pair and every comment verbatim.
  Commit `refactor(service-defaults): split the revocation check composition under thirty lines`.

## Phase 5: Verify (orchestrator)

- [ ] **T030** After each refactor commit: `dotnet test tests/ServiceDefaults.Tests -c Release`
  green and `git diff <T010-sha> -- tests/` empty.
- [ ] **T031** Plan §5.4 build: rows 1-2 gone, no new ADR-0084 warning, pragma count 1.
  `dotnet format --verify-no-changes`. `tests/Architecture.Tests` green.
- [ ] **T032** Each commit builds and passes on its own (`git rebase -x` over the branch).
- [ ] **T033** CI integration shards green (spec §4.5) before merge.

## Dependencies

T000 → T001/T002/T003/T004 (parallel) → T010 → T020 → T021 → T030-T033.
Nothing here is foundational for other work. Disjoint from spec 368 (PR #2956); rebase after it
merges.
