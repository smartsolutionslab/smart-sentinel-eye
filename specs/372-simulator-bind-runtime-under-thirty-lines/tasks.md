# Tasks 372: The simulator's `BindRuntime` under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2963 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). The existing
`SimulatorRuntimeBindingTests` (20 cases) and the new C1/C2 facts (10 cases) must be seen **passing
on unrefactored code** before T010 starts, and must pass **unmodified** after. An assertion that has
to be edited after T010 is evidence the behaviour moved: block, do not adjust. A C1/C2 fact that
arrives red before any production edit is a test defect, fixed in the test.

**Engineer:** backend-engineer (T010). **Test-writer:** T001. **Security-sensitive:** no (dev-only
host; the client-secret line moves verbatim). Phase 6: `/code-review` only.

**Forbidden in every task:** any production file other than `src/ScenarioSimulator/Program.cs`;
any change to a configuration key, default, exception type or message; any existing fact body in
`SimulatorRuntimeBindingTests.cs`; moving `BindRuntime` out of `Program.cs` or widening visibility;
ADRs, the constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`, `SonarLint.xml`,
csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No `[P]`
here: one test file, one production file, strictly sequential. No foundational task (nothing
touches Shared.Kernel, Shared.Contracts or AppHost).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body, on `8102af6f`: plan §5's S138 grep (1 hit:
  `Program.cs(125,13) … 42 lines`), the S107 count (15), and
  `dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~SimulatorRuntimeBindingTests"`
  (20 passed) and the full project run (242 passed). Captured by the architect on 2026-10-15.

## Phase 2: Characterisation (test-writer; blocks Phase 3)

- [ ] **T001** [US1] `tests/ScenarioSimulator.Tests/SimulatorRuntimeBindingTests.cs` — **add** two
  facts to the existing class, reusing `Bind`/`Missing`/`Complete`; no existing fact edited:
  - C1 (spec §4.2): `[Theory]` of 8 adjacent pairs; remove both keys of setting *n* and *n+1*
    (for Keycloak: `services:keycloak:http:0`, and ensure neither `ConnectionStrings:keycloak` nor
    `services:keycloak:https:0` is present); assert the message equals setting *n*'s, verbatim.
  - C2 (spec §4.2): `[Theory]` of 2 rows: empty `ClientSecret` binds `""` without throwing; empty
    `ConnectionStrings:keycloak` binds `KeycloakUrl == ""` over `services:keycloak:http:0`.
  Run `dotnet test tests/ScenarioSimulator.Tests -c Release --filter "FullyQualifiedName~SimulatorRuntimeBindingTests"`
  and return the verbatim output: **30 passed** on unrefactored code.
  Counterfactual (not committed): in a scratch edit of `Program.cs`, move the `ClientSecret`
  statement above `KeycloakUrl` and confirm exactly the C1 Keycloak/client-secret row goes red (no
  pre-existing fact pins that pair); revert, and confirm `git diff src/` is empty.
  Commit: `test(scenario-simulator): pin BindRuntime reporting order and empty identity values`.

Gate: the orchestrator holds T001's verbatim green output before dispatching T010.

## Phase 3: Refactor (backend-engineer; depends on T001)

- [ ] **T010** [US1] `src/ScenarioSimulator/Program.cs` only — extract `BindCameraEndpoints`,
  `BindSeedingEndpoints`, `BindIdentity` per plan §3, statements moved verbatim and in order
  (invariants I1-I5). Do not touch any test. Run plan §5: 0 S138 hits in `src\ScenarioSimulator\`,
  S107 still 15, no S1541/S134 in `Program.cs`; `SimulatorRuntimeBindingTests` 30 passed
  **unmodified**; full project 252 passed. `dotnet format --verify-no-changes` on the project.
  Commit: `refactor(scenario-simulator): split BindRuntime into endpoint and identity steps`.

## Phase 4: Verify (orchestrator)

- [ ] **T020** Quote in the PR: T000 before-evidence, T001 green output, T010 after-evidence, and
  `git diff --stat` of the T010 commit (Program.cs only). Confirm
  `git diff <T001>..<T010> -- tests/` is empty. Re-check spec number 372 is still free.
  `Phase 5:` the simulator is not booted (dev-only, gated off in CI; the binding is fully exercised
  by invocation) — state that in the verification note.

## Dependencies

T000 -> T001 -> T010 -> T020. Each commit builds and passes on its own (T001 adds green tests
against unchanged code; T010 leaves them green).
