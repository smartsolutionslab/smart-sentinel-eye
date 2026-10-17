# Tasks 396: An unresolved audit row is hidden from fab-scoped reads

Spec: [spec.md](./spec.md). Plan: [plan.md](./plan.md). Issue #2927 (feature-level; autonomous lane,
ADR-0144; already on Project #13). No per-task issues.

**Phase 4a colour: RED** (behaviour-changing: three read paths stop returning `Unresolved` rows to
fab-scoped callers; a named-fab search starts returning `NotApplicable` rows). Characterisation tests
listed in plan §5.2 must be observed GREEN before and after, **unmodified**.

**Existing tests authorised for retirement: exactly R-1 … R-12** (plan §5.1), by the test-writer in
phase 4a, never by the engineer. Basis: the owner decision on #2927 (R-1, R-2, R-5 … R-12) and R1's
settlement of the nit (R-3, R-4). `SearchAuditRefusalAndScopeCharacterisationTests.cs:296` is **not**
retired although #2927 lists it — it pins the unscoped caller, kept "as today".
**Arrangement-only edits authorised:** plan §5.3, nothing else. The PR body names every retired
assertion, every §5.3 edit, and why.

Format: `[ID] [P?] [Story] description`. `[P]` = owns disjoint files (ADR-0109).

**Foundational:** none. No Shared.Kernel, Shared.Contracts, ServiceDefaults, AppHost, realm or
migration change. Everything is inside AuditObservability and its two test projects.

## Phase 4a: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] `tests/AuditObservability.Application.Tests/Queries/Handlers/SearchAuditRefusalAndScopeCharacterisationTests.cs`:
  retire R-1 (`:270`), R-2 (`:306`), R-3 (`:252`), R-4 (`:262`); add N-1 … N-4 (plan §5.4). Correct the
  class doc comment (`:25`, "a fab-less row is visible to every fab-scoped caller"). Leave `:296` and every
  refusal/throw-order test byte-identical.
- [ ] **T002** [P] [US1] `.../SearchAuditQueryHandlerTests.cs`: retire R-5 (`:78`), R-6 (`:136`); add N-5,
  N-6. Comment-only update inside `Naming_a_fab_excludes_a_row_whose_fab_was_never_resolved` (`:93`); its assertions
  unchanged.
- [ ] **T003** [P] [US1] `.../GetResourceTimelineCharacterisationTests.cs`: retire R-7 (`:198`); add N-7.
- [ ] **T004** [P] [US1] `.../GetResourceTimelineQueryHandlerTests.cs`: retire R-8 (`:65`), R-9 (`:78`);
  add N-8, N-9. `:101` unchanged.
- [ ] **T005** [P] [US1] `.../GetAuditEventQueryHandlerTests.cs`: §5.3 arrange edit (`:18`, `:33`, `:53` →
  `CallerFabs: []`); add N-10 … N-13. Red = compile failure on the missing `CallerFabs` (quote it). The
  test-writer may not add the parameter to production code to get past it. N-11/N-12 are characterisation
  of kept behaviour whose only red is that compile failure — say so in the report.
- [ ] **T006** [P] [US1] `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs`:
  §5.3 arrangement edit (`:355-360`, null-fab row read from the audit store and projected to the same
  JSON property names); retire R-10 (`:126`), R-11 (`:180`), R-12 (`:221`); add I-1 … I-5. Facts `:74`,
  `:87`, `:104`, `:115`, `:144`, `:241`, `:256`, `:270` keep their assertions byte-identical.
- [ ] **T007** [P] [US1] `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`:
  §5.3 seed edits (`OverlayRow` `:436` gains `FabScope.Neutral`; `:97`, `:138` seed a neutral row); add
  I-6, I-7. All existing assertions unchanged.
- [ ] **T008** [US1] (after T001-T007) Run and return **verbatim** output. Unit (local, no Docker):

  ```sh
  dotnet test tests/AuditObservability.Application.Tests/SmartSentinelEye.AuditObservability.Application.Tests.csproj \
    --filter "FullyQualifiedName~SearchAuditRefusalAndScopeCharacterisationTests|FullyQualifiedName~SearchAuditQueryHandlerTests|FullyQualifiedName~GetResourceTimelineCharacterisationTests|FullyQualifiedName~GetResourceTimelineQueryHandlerTests|FullyQualifiedName~GetAuditEventQueryHandlerTests"
  ```

  Integration (**Docker required — down locally**): build only, then report as *not run*:

  ```sh
  dotnet build tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj
  # when Docker is available (CI shards 2 and 4):
  dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
    --filter "FullyQualifiedName~UnresolvedFabAuditRowIntegrationTests|FullyQualifiedName~CrossFabReadGuardIntegrationTests|FullyQualifiedName~NeutralFabRetentionRowIntegrationTests"
  ```

  T005's compile red blocks the whole unit project from building; capture that first, then — to observe
  T001-T004 red as assertion failures — re-run with T005 temporarily stashed and say so. Never weaken a
  test to get a cleaner red.
- [ ] **T009** [US1] (after T008) **test-adversary** pass, same projects: the 404's byte-equivalence to an
  unknown id (I-4), multi-fab caller, blank `fabId=`, a caller holding the row's camera's fab, cursor
  walking past a hidden row, an unclassified (unregistered neutral) event type now hidden, malformed-only
  claims (residual, expect visible — characterisation). New adversary tests go in the T001-T007 files.

T001-T007 own disjoint files and fan out; T006 and T007 share no file but share the AspireCollection at
run time (serialised), which does not block writing them in parallel.

## Phase 4b: Green (backend-engineer; tests read-only)

- [ ] **T010** [US1] (after T008) `src/AuditObservability/Application/Queries/Handlers/AuditReadScope.cs`
  per plan §3.1 — one per-entry parse of `CallerFabs` (moved from
  `SearchAuditQueryHandler.ParseUsableFabs`), one EF-translatable expression of R1, `Hides`.
- [ ] **T011** [US1] (after T010) `SearchAuditQueryHandler.cs`: `ScopeToFabs` delegates to
  `AuditReadScope` (plan §3.2); remove the moved parse; rewrite comments `:95-107`, `:112-114` (FR-007).
  Keep `FabIdentifier.From(fab)` unguarded (throw order pinned).
- [ ] **T012** [US1] (after T010) `GetResourceTimelineQueryHandler.cs:59` → `AuditReadScope.Named(fab)`;
  rewrite comment `:49-58`.
- [ ] **T013** [US1] (after T010) `GetAuditEventQuery.cs` gains required `CallerFabs`;
  `GetAuditEventQueryHandler.cs` answers `AuditEventNotFound` when `Hides` (plan §3.4);
  `SearchAuditQuery.cs` summary updated.
- [ ] **T014** [US1] (after T013) `src/AuditObservability/Api/AuditEndpoints.cs` `GetSingle`: pass
  `FabClaims.AssignedFabs(user)`; keep the `Resolved`-row guard; correct the class/endpoint comment.
- [ ] **T015** [US1] (after T011-T014) Re-run T008's unit command — all green, §5.2 tests unmodified;
  `dotnet build -c Release` clean (IDE0007/IDE0005, analyzers); Application coverage ≥ 80 %.

T011, T012, T013 touch disjoint files but are one engineer's sequential slice (each depends on T010);
no fan-out is worth its coordination cost here.

## Phase 5: Verify

- [ ] **T016** Spec §Independent end-to-end procedure, steps 1-7, on a booted stack (needs Docker).
  Record `NFR002_AuditSearchLatencyTests` p99 from two runs (memory: the first run after churn reads
  as a regression). Latency §IV: N/A.

## Phase 6: QA

- [ ] **T017** `backend-reviewer` + `security-reviewer`: one rule on three paths; no fail-open default;
  404 not an oracle; no assertion edited beyond R-1…R-12 and §5.3; residual (malformed-only claims) and
  the pre-existing `Resolved` 403 oracle filed as findings without `agent:ready`.

## What the lane must not take

- Stopping new `Unresolved` rows, re-attributing historic rows, a new `sse.*` scope (option B).
- Changing the `Resolved` other-fab `403` on `GET /audit/{id}` to 404 (existing control).
- Changing the malformed-only-claims bucket (spec 245 pins it).
- Any ADR or constitution edit — block instead (ADR-0144).
