# Tasks 365: The event-ingestion endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2930. The issue is
feature-level, so no per-task issues are created.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving).
- T001-T005 must be seen **passing on the unrefactored code (`c5cde739`)** before any production
  task starts.
- They must pass **byte-unmodified** after every refactor commit. An assertion that has to be edited
  after a refactor commit is evidence that the behaviour moved: block, do not adjust.
- A characterisation test that arrives red before any production edit is a test defect, and it is
  fixed in the test.

**Engineer:** backend-engineer (T010-T019). **Test-writer:** T001-T005. **Security-sensitive:**
T004 and T018-T019. Phase 6 adds `/security-review`.

**Commits.** One commit per task, using Conventional Commits, with no `Co-Authored-By` (ADR-0086)
and never `--no-verify`. Each commit must build **and pass** on its own. Test commits add green
tests against unchanged production code. Each refactor commit leaves `tests/Architecture.Tests`
green, and the plan §5 build is run after it.

**Forbidden in every task:**
- any file outside `src/EventIngestion/Api/`'s six files of spec §1, the new test files, the
  `Architecture.Tests` csproj and this spec directory;
- any existing test file, and any guard;
- suppressions, thresholds, `.editorconfig` and `SonarLint.xml`;
- migrations and `Shared.Contracts`;
- a new `*Endpoints.cs` file, or a fourth `EventsEndpoints` partial file;
- any change to `AuthenticateWebhookAsync`'s signature;
- an `Idempotency-Key` on `POST /webhook-integrations`.

Legend: `[P]` = the task owns files disjoint from the other `[P]` tasks in its phase. On this one
branch the commits still land one after another (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body:
  - the plan §5 build output on `c5cde739` (12 S138 + 14 S107);
  - the `dotnet test tests/Architecture.Tests -c Release` summary line.

  Re-check the spec number (365) against `origin/develop` and every remote head.

## Phase 2: Characterisation (test-writer; blocks every later phase)

T001 owns the csproj and the host that the other tests use, so it goes first.

- [ ] **T001** [US1] Add `<PackageReference Include="Moq" />` after `Shouldly` in
  `tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj`, and create
  `tests/Architecture.Tests/EventIngestionEndpointHost.cs` (plan §4 C0).
  Commit `test(event-ingestion): an in-process host for the event-ingestion endpoints`.
- [ ] **T002** [P] [US1] C1: `tests/Architecture.Tests/EventIngestionRouteTableCharacterisationTests.cs`,
  the route-table baseline of all 15 routes, captured on `c5cde739` as a raw-string literal.
  Commit `test(event-ingestion): pin the event-ingestion route table`.
- [ ] **T003** [P] [US1] C2: `tests/Architecture.Tests/EventSourceAndTypeEndpointCharacterisationTests.cs`,
  spec §4.2 rows Declare, Change and Register (event types).
  Commit `test(event-ingestion): pin source-mode and event-type command refusals, order and dispatch`.
- [ ] **T004** [P] [US1] **(security)** C4: `tests/Architecture.Tests/WebhookEndpointCharacterisationTests.cs`,
  spec §4.2 rows IngestWebhook + AuthenticateWebhookAsync and Register (webhook). This includes:
  - every row of the spec §2 401 outline, with its `Times.Never`;
  - the log-once window;
  - both token modes.

  Commit `test(event-ingestion): pin webhook authentication order and refusals`.
- [ ] **T005** [P] [US1] C3: `tests/Architecture.Tests/EventIngestEndpointCharacterisationTests.cs`,
  spec §4.2 rows IngestManual and ListEvents.
  Commit `test(event-ingestion): pin manual ingest and event listing refusals, order and dispatch`.
- [ ] **T006** Gate: run `dotnet test tests/Architecture.Tests -c Release` on the T001-T005 tip,
  with production code still at `c5cde739`. Quote the verbatim pass output for the PR body. List
  any case dropped as infeasible, together with the spec §4.3 CI test that observes it instead.

## Phase 3: Refactor (backend-engineer; after T006)

The groups own disjoint files, so A-D are `[P]` with respect to each other. Within a group the
tasks are sequential (same file). E runs last, so the security diff is reviewed against a settled
branch.

### Group A: `src/EventIngestion/Api/EventSourcesEndpoints.cs`

- [ ] **T010** [P] [US1] `Declare` via `BoundaryParse` + `DeclareScope`; `Change` via
  `ChangeOutcome` + a hoisted command, with the `If-Match` read and the inline 400 catch kept
  (plan §3.1).
  Commit `refactor(event-ingestion): split the source-mode declare and change endpoints`.
- [ ] **T011** [US1] `MapEventSourcesEndpoints` → one `Map…(RouteGroupBuilder group)` helper per
  mapping, in the same order, with each chain byte-identical (plan §3.1, §3.5).
  Commit `refactor(event-ingestion): map each event-source route in its own method`.

### Group B: `src/EventIngestion/Api/EventTypesEndpoints.cs`

- [ ] **T012** [P] [US1] `Register` via `BoundaryParse` (before the key) + `RegisterScope` (plan §3.2).
  Commit `refactor(event-ingestion): split the event-type register endpoint`.
- [ ] **T013** [US1] `MapEventTypesEndpoints` → per-mapping helpers (plan §3.2).
  Commit `refactor(event-ingestion): map each event-type route in its own method`.

### Group C: `src/EventIngestion/Api/WebhookIntegrationsEndpoints.cs`

- [ ] **T014** [P] [US1] `Register` via `BoundaryParse` with the (name, default kind) tuple. No
  idempotency (plan §3.3).
  Commit `refactor(event-ingestion): split the webhook-integration register endpoint`.
- [ ] **T015** [US1] `MapWebhookIntegrationsEndpoints` → per-mapping helpers on `group` (plan §3.3).
  Commit `refactor(event-ingestion): map each webhook-integration route in its own method`.

### Group D: `src/EventIngestion/Api/EventsEndpoints.cs`, `EventsEndpoints.Reads.cs`

- [ ] **T016** [P] [US1] `ListEvents` via `ParseListFilters` + `BoundaryParse`, parsed before the
  fab (plan §3.4).
  Commit `refactor(event-ingestion): parse the event-list filters in one step`.
- [ ] **T017** [US1] `MapEventsEndpoints` → per-mapping helpers on `writes` / `reads`, kept in
  `EventsEndpoints.cs` (plan §3.4).
  Commit `refactor(event-ingestion): map each events route in its own method`.

### Group E: `src/EventIngestion/Api/EventsEndpoints.Writes.cs` (security; after A-D)

- [ ] **T018** [US1] **(security)** `IngestManual`: drop the alias locals, add
  `AdmitManualCallerAsync` (revoked → fab → storage), `ManualEnvelope` and `ManualScope`. The key
  read and the envelope's inline 400 catch stay in the body (plan §3.4).
  Commit `refactor(event-ingestion): split the manual ingest endpoint`.
- [ ] **T019** [US1] **(security)** `IngestWebhook` via `WebhookEnvelope`, with its inline 400
  kept. `AuthenticateWebhookAsync` via `FindIntegrationAsync` + `IsRevokedLoggingOnce`, with the
  signature and the order prefix → find → revoked → own fab → token unchanged (plan §3.4).
  Commit `refactor(event-ingestion): split webhook ingest and its authentication`.

## Phase 4: Verify (orchestrator)

- [ ] **T020** Plan §5 build: 0 S138 in `EventIngestion.Api`'s hand-written files, the same 14 S107
  by file and method, and no new warning. Quote the output verbatim.
- [ ] **T021** `dotnet test tests/Architecture.Tests -c Release` is green. `git diff origin/develop -- tests/`
  shows only the five new files and the csproj, with no existing test touched.
- [ ] **T022** PR to `develop` (`--base develop`). The verification note:
  - names CI's integration shards as the observer for spec §4.3;
  - quotes T006's pre-change green run;
  - states the latency impact from spec §7: on the "event → overlay state" leg, nothing measurable.

  Phase 6 runs `/code-review` and `/security-review` (T004, T018, T019).

## Dependencies

```
T000 → T001 → {T002, T003, T004, T005} → T006
T006 → {T010 → T011} ∥ {T012 → T013} ∥ {T014 → T015} ∥ {T016 → T017}
     → T018 → T019 → T020 → T021 → T022
```

The `[P]` sets own disjoint files. On one branch they still land in sequence, in the order
A, B, C, D, E. If the orchestrator fans them out to worktrees, each group must be rebased onto the
previous group's tip before it is committed to this branch. Group E depends on nothing in A-D, but
it goes last on purpose, so that its security diff is the last thing reviewed.
