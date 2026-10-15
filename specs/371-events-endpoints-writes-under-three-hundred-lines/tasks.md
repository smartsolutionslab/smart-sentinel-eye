# Tasks 371: `EventsEndpoints.Writes.cs` under 300 lines

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2962 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 runs the
existing suite on unchanged code (`8102af6f`) and its output is quoted verbatim in the PR. The same
commands must pass after T010-T012 with `git diff -- tests/` empty. An assertion that would have to
change is a **block**, not an edit (ADR-0144).

**Engineer:** backend-engineer (T010-T013). **Test-writer:** T001 (runs only; writes nothing — spec
365's suite already covers both paths, spec §4). **Security-adjacent:** Phase 6 runs
`/security-review` as well as `/code-review`.

**Forbidden in every task:** any file under `tests/`; any change to a member body other than the
three call-site qualifications and two `private` → `internal` modifiers (plan §2); moving
`IngestManual`, `IngestWebhook` or `IngestManualServices`; a new `EventsEndpoints` partial file; a
file name ending in `Endpoints.cs`; ADRs, the constitution, CLAUDE.md; suppressions, thresholds,
`.editorconfig`, `SonarLint.xml`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing touches Shared.Kernel, Shared.Contracts or AppHost. **No `[P]` in Phase 3**:
T010-T012 all edit `EventsEndpoints.Writes.cs`, so they are sequential.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record on `8102af6f`: `wc -l src/EventIngestion/Api/EventsEndpoints*.cs` and the
  Release `--no-incremental` S104 grep (plan §4). Expect one hit, `EventsEndpoints.Writes.cs` 519.

## Phase 2: Characterisation, observed green (test-writer; runs only)

- [ ] **T001** [US1] Run every command in plan §4 "In-process" (and "Aspire fixture" if the stack is
  free) on unchanged code. Capture each summary line verbatim, with non-zero counts. Writes no file.

## Phase 3: The move (backend-engineer; sequential, one commit each, each builds and stays green)

- [ ] **T010** [US1] Create `src/EventIngestion/Api/WebhookDeliveryAuthentication.cs`; move
  `BearerPrefix` + lines 244-428 (plan §2.1); `AuthenticateWebhookAsync` → `internal`; qualify the
  call at line 197. Commit: `refactor(event-ingestion): webhook bearer authentication moves out of
  EventsEndpoints.Writes`. Depends on T001.
- [ ] **T011** [US1] Create `src/EventIngestion/Api/EventIngestStore.cs`; move lines 445-518 (plan
  §2.2); `StoreOrRefuseAsync` → `internal`; qualify the calls at lines 71 and 225. Commit:
  `refactor(event-ingestion): StoreOrRefuseAsync moves out of EventsEndpoints.Writes`. Depends on T010.
- [ ] **T012** [US1] `using` cleanup across the three files (`dotnet format style --diagnostics
  IDE0005` scoped to `src/EventIngestion/Api`); may be folded into T010/T011's commits. Depends on T011.
- [ ] **T013** [US1] Comment pointers in `src/EventIngestion/Api/Program.cs:14` and
  `src/ServiceDefaults/Authorization/FabClaims.cs:19` (plan §2.4); prove with a comment-stripped hash.
  Commit: `docs(event-ingestion): comments point at WebhookDeliveryAuthentication`. Depends on T010.

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] Re-run T001's commands unmodified; quote the summaries. `git diff --stat
  origin/develop -- tests/` empty. `wc -l` every touched file <= 300. Release `--no-incremental`
  build: no S104 and no new warning for `src/EventIngestion/Api`. Attach the pure-move proof (plan
  §4). Depends on T012, T013.

## Dependencies

T000 → T001 → T010 → T011 → T012 → T020; T010 → T013 → T020. Nothing parallelisable: one context,
one source file at the centre of every edit.
