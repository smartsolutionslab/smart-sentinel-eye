# Tasks 380: the EventIngestion Api helpers under five parameters

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2971 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T000a first adds four
characterisation tests (`6a32aa9f`), observed green on unchanged code; T001 then runs the suite on
that code and its output is quoted verbatim in the PR. The same
commands must pass after T010-T012 with `git diff 6a32aa9f -- tests/` empty. An assertion that would have to
change is a **block**, not an edit (ADR-0144).

**Engineer:** backend-engineer (T010-T012). **Test-writer:** T000a (adds four tests, spec §4, D-4) and T001 (runs only). **Security-sensitive:** T012 changes the webhook bearer check's signature; Phase 6 runs
`/security-review` (security-reviewer) as well as `/code-review`.

**Scope is 3 of the issue's 13 hits** (H6, H10, H13). The other 10 are blocked on an owner decision
(spec §6) and **must not be attempted**: they are reachable only by changing a route handler's
parameter list, which edits `EventIngestionRouteTableCharacterisationTests`' baseline.

**Forbidden in every task after T000a:** any file under `tests/`; any route handler's signature (`IngestManual`,
`IngestWebhook`, `ListEvents`, `ListDeadLetters`, `Declare`, `Change`, `Undeclare`, `Register`,
`Retire`, `Revoke`); the three existing `*Services` records; any body change in
`AuthenticateWebhookAsync` beyond the signature and two deconstruction lines; a new file, a file
name ending in `Endpoints.cs`; `Log.cs`; ADRs, the constitution, CLAUDE.md; suppressions,
thresholds, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing touches Shared.Kernel, Shared.Contracts or AppHost. T010 and T011 own
disjoint files and may run in parallel; T012 owns `WebhookDeliveryAuthentication.cs` and
`EventsEndpoints.Writes.cs`, disjoint from both. Commits land sequentially (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [x] **T000** On `c0f4969d`, run spec §1's Release `--no-incremental` build and record the S107
  list under `src/EventIngestion/Api` (expect the 14 of spec §1) and the full Sonar warning list
  there (plan §4 gate measurement; expect S107 only).

## Phase 2: Characterisation, observed green (test-writer)

- [x] **T000a** [US1] Add four tests to `EventSourceAndTypeEndpointCharacterisationTests`: keyed
  declare stores `Mode` and `DeclaredBy`; keyed register stores the kind and `RegisteredBy`; a
  replayed key on each answers the stored identifier and the handler does not run. Observe green on
  unchanged code. Commit `6a32aa9f`. They guard *the handler does not run on replay*, not *the
  command is not built on replay*. Depends on T000.

- [x] **T001** [US1] Run every command in plan §4 "In-process" (and "Aspire fixture" if the stack
  is free) on unchanged code. Capture each summary line verbatim, with non-zero counts. Writes no
  file. Depends on T000.

## Phase 3: The change (backend-engineer; one commit each, each builds and stays green)

- [x] **T010** [P] [US1] `src/EventIngestion/Api/EventSourcesEndpoints.cs`: `DeclareAsync` takes
  `(handler, DeclareSourceModeCommand command, cancellationToken)`; the call in `Declare` builds
  the command inline in the lambda on one line (plan §2.1; no hoisted local, to avoid S138 on `Declare`). Commit:
  `refactor(event-ingestion): DeclareAsync takes the command it sends`. Depends on T001.
- [x] **T011** [P] [US1] `src/EventIngestion/Api/EventTypesEndpoints.cs`: same for `RegisterAsync`
  (plan §2.2). Commit: `refactor(event-ingestion): RegisterAsync takes the command it sends`.
  Depends on T001.
- [x] **T012** [P] [US1] `src/EventIngestion/Api/WebhookDeliveryAuthentication.cs` +
  `src/EventIngestion/Api/EventsEndpoints.Writes.cs`: add `WebhookDelivery` and
  `WebhookAuthenticationServices` (plan §2.3); `AuthenticateWebhookAsync` takes `(delivery,
  services, cancellationToken)` and deconstructs both first, the rest of its body byte-identical;
  `IngestWebhook` passes the two records with the logger created inline, same category. One commit
  (the signature and its only caller must change together to build — ADR-0087). Commit:
  `refactor(event-ingestion): webhook authentication takes the delivery and its collaborators`.
  Depends on T001. **Security-sensitive.**
- [x] **T013** [US1] `using` cleanup in the touched files only (`dotnet format style --diagnostics
  IDE0005`, scoped); fold into T010-T012's commits where it applies. Depends on T010, T011, T012.

## Phase 4: Verify (orchestrator)

- [x] **T020** [US1] Re-run T001's commands unmodified; quote the summaries. `git diff --stat
  6a32aa9f -- tests/` empty. Release `--no-incremental` build: S107 under
  `src/EventIngestion/Api` is exactly the 11 of plan §4 (H6, H10, H13 gone), and no S138, S104,
  S1541, S134 or other new warning there. Attach the `AuthenticateWebhookAsync` diff (signature +
  two lines) to the PR for the security review. Depends on T013.
- [x] **T021** Comment on #2971 with spec §6 (the ten blocked hits, the counterfactual that
  `[FromServices]` is not counted, and options a-c), and note that the route-handler hits of #2972-#2975 in contexts with a route-table baseline hit the same block, while `IdentityFabResolution.ResolveReadFabsAsync` (#2975) is a fixable helper and AuditObservability (#2974) has no route-table baseline (spec §6).
  The issue stays open after the PR merges (use `Refs #2971`, not a closing keyword). Depends on T020.

## Dependencies

T000 → T001 → {T010, T011, T012} → T013 → T020 → T021. T010, T011, T012 are file-disjoint (`[P]`);
each must build and pass the §4 in-process suite on its own commit.
