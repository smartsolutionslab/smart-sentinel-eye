# Tasks 367: The identity and automation endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2932 (scope narrowed to
the seven Api methods; ServiceDefaults moves to the follow-ups of spec §8). The issue is
feature-level, so no per-task issues are created.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving).
- T002-T008 must be seen **passing on the unrefactored code (`3b10a210`)** before any production
  task starts.
- They must pass **byte-unmodified** after every refactor commit. An assertion that has to be edited
  after a refactor commit is evidence that the behaviour moved: block, do not adjust.
- A characterisation test that arrives red before any production edit is a test defect, fixed in
  the test.

**Engineer:** backend-engineer (T010-T016). **Test-writer:** T002-T008. **Security-sensitive:**
T006, T007, T012-T016. Phase 6 adds `/security-review`.

**Commits.** One commit per task, Conventional Commits, no `Co-Authored-By` (ADR-0086), never
`--no-verify`. Each commit must build **and pass** on its own. Each refactor commit leaves
`tests/Architecture.Tests` green, and the plan §5 build is run after it.

**Forbidden in every task:**
- any file outside the four Api files of spec §1 rows 1-7, the new test files and this spec directory;
- any existing test file, and any guard;
- suppressions, thresholds, `.editorconfig`, `SonarLint.xml`;
- `src/ServiceDefaults/**`, Application/Domain/Infrastructure, migrations, `Shared.Contracts`, AppHost;
- a new `*Endpoints.cs` file or `RulesEndpoints` partial;
- renaming or re-signing `BuildAction` / `Build…` (bound by reflection), `ResolveWriteFabAsync`, or any handler.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks in its phase. On this one branch the
commits still land one after another (one branch, one index).

---

## Phase 1: Baseline and scope (orchestrator; no source files)

- [ ] **T000** Record verbatim for the PR body: the plan §5 build output on `3b10a210` (7 S138 +
  10 S107) and the `dotnet test tests/Architecture.Tests -c Release` summary line. Re-check the
  spec number (367) against `origin/develop`, every remote head and open worktrees.
- [ ] **T001** File the spec §8 follow-ups and add each to Project #13 (`gh project item-add 13
  --owner smartsolutionslab --url <url>`; verify with `--limit 2000`):
  - **A** "ADR-0084 S138: ServiceDefaults request parsing and store/health reads" —
    `TryReadUpsertPrecondition`, `IdempotencyHeaders.TryRead`, `OutboxBacklogHealthCheck.CheckHealthAsync`,
    `IdempotencyStore.BeginAsync`; body = spec §8 A table. Labels `tech-debt`, `agent:ready`.
  - **B** "ADR-0084 S138: ServiceDefaults bearer authentication composition" —
    `AddBearerAuthentication`, `AddRevocationCheck`; body = spec §8 B table; security-sensitive.
    Labels `tech-debt`, `agent:ready`.
  - **C** "ADR-0084 S138 + S107: WolverineDefaults.AddWolverineForContext needs a net and an
    options decision" — body = spec §8 C. Label `tech-debt` only (**not** `agent:ready`: a
    maintainer decides the S107 options record first).
  Comment on #2932 linking A, B, C and stating the narrowed scope.

## Phase 2: Characterisation (test-writer; blocks every later phase)

First apply spec J2: `git ls-tree origin/develop tests/Architecture.Tests/RouteTableRendering.cs
tests/Architecture.Tests/InProcessEndpointDriver.cs`. Both present → rebase onto `origin/develop`
and reuse them. Otherwise private copies, no shared names.

- [ ] **T002** [US1] C0: `tests/Architecture.Tests/IdentityEndpointHost.cs` (plan §4).
  Commit `test(identity): an in-process host for the device, kiosk and rotation endpoints`.
- [ ] **T003** [P] [US1] C0': `tests/Architecture.Tests/AutomationEndpointHost.cs` (plan §4).
  Commit `test(automation): an in-process host for the rule endpoints`.
- [ ] **T004** [P] [US1] C1: `tests/Architecture.Tests/IdentityRouteTableCharacterisationTests.cs`,
  8 routes, literal captured on `3b10a210`. After T002.
  Commit `test(identity): pin the device, kiosk and rotation route table`.
- [ ] **T005** [P] [US1] C2: `tests/Architecture.Tests/AutomationRouteTableCharacterisationTests.cs`,
  6 routes. After T003.
  Commit `test(automation): pin the rule route table`.
- [ ] **T006** [P] [US1] **(security)** C3: `tests/Architecture.Tests/DeviceAndKioskEndpointCharacterisationTests.cs`,
  spec §4.2 rows Register and Enroll, including both replay cases and the keyed-refusal release.
  After T002.
  Commit `test(identity): pin device registration and kiosk enrolment order, scope and replay`.
- [ ] **T007** [P] [US1] **(security)** C4: `tests/Architecture.Tests/WebhookRotationEndpointCharacterisationTests.cs`,
  spec §4.2 row Rotate: guard before precondition, the three precondition refusals, both
  fingerprints, the stale-If-Match replay with Keycloak create/rotate `Times.Never`. After T002.
  Commit `test(identity): pin webhook rotation order, fingerprint and replay`.
- [ ] **T008** [P] [US1] C5: `tests/Architecture.Tests/RuleCreateEndpointCharacterisationTests.cs`,
  spec §4.2 row Create, including the four parse-order pairs. After T003.
  Commit `test(automation): pin rule creation order, parse order, scope and replay`.
- [ ] **T009** Gate: `dotnet test tests/Architecture.Tests -c Release` on the T002-T008 tip with
  production still at `3b10a210`. Quote the verbatim pass output for the PR body. List any dropped
  case with its spec §4.3 CI observer.

## Phase 3: Refactor (backend-engineer; after T009)

Groups own disjoint files, so D, A, B, C are `[P]` with respect to each other. Within a group the
tasks are sequential (same file). Landing order on the branch: D, A, B, C.

### Group D: `src/Automation/Api/RulesEndpoints.cs`

- [ ] **T010** [P] [US1] `Create` via `ParseRule` + `BoundaryParse` (after the fab), `CreateScope`,
  hoisted `createRuleCommand`, `CreateAsync` (plan §3.4).
  Commit `refactor(automation): split the rule create endpoint`.
- [ ] **T011** [US1] `MapRulesEndpoints` → one `Map…(RouteGroupBuilder group|reads)` helper per
  route, same order, chains byte-identical, all in `RulesEndpoints.cs` (plan §3.4).
  Commit `refactor(automation): map each rule route in its own method`.

### Group A: `src/Identity/Api/DevicesEndpoints.cs` (security)

- [ ] **T012** [P] [US1] **(security)** `Register` via `BoundaryParse`, `RegisterScope`,
  `ReplayDeviceAsync`; the key read first, the command still built inside the create lambda (plan §3.1).
  Commit `refactor(identity): split the device registration endpoint`.
- [ ] **T013** [US1] `MapDevicesEndpoints` → `MapRegisterDevice(group)`, `MapDisableDevice(group)`,
  `MapListDevices(reads)` (plan §3.1).
  Commit `refactor(identity): map each device route in its own method`.

### Group B: `src/Identity/Api/KiosksEndpoints.cs` (security)

- [ ] **T014** [P] [US1] **(security)** `Enroll` via `BoundaryParse`, `EnrollScope`,
  `ReplayKioskAsync` (plan §3.2).
  Commit `refactor(identity): split the kiosk enrolment endpoint`.
- [ ] **T015** [US1] `MapKiosksEndpoints` → `MapEnrollKiosk(group)`, `MapDisableKiosk(group)`,
  `MapListKiosks(reads)` (plan §3.2).
  Commit `refactor(identity): map each kiosk route in its own method`.

### Group C: `src/Identity/Api/WebhookRotationEndpoints.cs` (security; last)

- [ ] **T016** [US1] **(security)** `Rotate` via `BoundaryParse`, `RotateFingerprint`, `RotateScope`,
  `ReplayRotationAsync`; key → fab → guard → `TryReadUpsertPrecondition` inline → dispatch; every
  existing comment kept in place (plan §3.3, including the eager-fingerprint note for the PR body).
  Commit `refactor(identity): split the webhook rotation endpoint`.

## Phase 4: Verify (orchestrator)

- [ ] **T020** Plan §5 build: 0 S138 in `Identity.Api` and `Automation.Api` hand-written files, the
  same 10 S107 by file and method, no new warning. Quote verbatim.
- [ ] **T021** `dotnet test tests/Architecture.Tests -c Release` green. `git diff origin/develop --stat`
  shows only the 4 Api files, the 7 new test files and this spec directory; no existing test touched.
- [ ] **T022** PR to `develop` (`--base develop`). The body: quotes T009's pre-change green run and
  T020's build; names CI's integration shards as the spec §4.3 observer; states latency N/A (spec §7);
  states the narrowed scope with links to T001's issues A, B, C; carries the plan §3.3
  eager-fingerprint note. Phase 6 runs `/code-review` and `/security-review` (T006, T007, T012-T016).

## Dependencies

```
T000 → T001
T000 → T002 → {T004, T006, T007}
T000 → T003 → {T005, T008}
{T004..T008} → T009
T009 → {T010 → T011} ∥ {T012 → T013} ∥ {T014 → T015} ∥ {T016}
     → land D, A, B, C in that order → T020 → T021 → T022
T001 must be done before T022.
```

T002 and T003 own disjoint files and are `[P]`; each host blocks only its own context's tests. The
refactor groups own disjoint files; if fanned out to worktrees, each is rebased onto the previous
group's tip before landing. Group C goes last so the most delicate security diff is reviewed against
a settled branch.
