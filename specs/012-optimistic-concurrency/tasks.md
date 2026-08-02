# Tasks: 012 — Optimistic Concurrency (make ADR-0043 real)

**Input:** Design document at [plan.md](./plan.md) (Phase 2 closed)

**Requirement source:** [#1154](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/1154)
as corrected. No `spec.md` — remediation of an accepted ADR, not a new
capability.

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to
  parallelise.
- **[Story]** — ADR (decision record), CORE (shared mechanism),
  XREQ (cross-request layer), CTX (per-context rollout), FE (frontend),
  POLISH.

## Scale (measured, not estimated)

| | Count |
|---|---|
| Command handlers total | 30 |
| Mutate-existing | 18 (+1 hybrid) |
| — of those, machine-driven, **not** gated (`ReportStreamHealth`) | 1 |
| **Commands receiving a stale-version gate** | **18** (17 + 1 hybrid) |
| Create-only (no gate) | 10 |
| Mutating HTTP endpoints | 28 |
| **Bodiless mutating endpoints** | **14** — exactly half |
| Aggregate roots | 10 |
| EF registration sites | 9 (+1 extra scoped registration) |

**The 14 bodiless endpoints are why this design uses `If-Match`.** Half
the mutating surface has nowhere to put an expected version — publish,
archive, branch, revert, and three DELETEs. A header covers all 28
uniformly; a body field would mean inventing request bodies for 14
endpoints.

## Two traps in this codebase

1. **5 of 30 handlers declare their error types inline** in `*Command.cs`
   rather than in a `*Errors.cs` file — so a glob on `*Errors.cs`
   **undercounts**. Four of the five are mutate-existing:
   `ArchiveRuleCommand.cs:11`, `PublishRuleCommand.cs:11`,
   `DisableDeviceCommand.cs:11`, `DisableKioskCommand.cs:11` (plus
   `RotateWebhookClientCommand.cs:23`).
2. **Three "commands" are not mutations.** `AuthorizeWhepCommandHandler`
   never calls `SaveAsync`; `POST /rules/{name}/dry-run`
   (`RulesEndpoints.cs:73`) and `POST /streams/authorize`
   (`StreamEndpoints.cs:45`) persist nothing. None may receive a gate.

## Path conventions

- Shared mechanism: `src/ServiceDefaults/Persistence/`, `src/Shared.Kernel/`
- Per-context: `src/<Context>/{Application,Api,Infrastructure}/`
- Frontend: `apps/shared/src/api/`, `apps/management-web/`
- Tests: `tests/ServiceDefaults.Tests/`, `tests/Integration.Tests/<Context>/`, `e2e/`

---

## Phase 1: Decision — ADR-0113

Blocks everything. Implementing against an unamended ADR would leave the
code contradicting the decision record.

- [ ] **T001 [ADR]** Write `docs/adr/0113-optimistic-concurrency-two-layer.md`
      amending ADR-0043. Must record: (a) Overlays and Automation are
      **EF Core, not Marten** — the stream-version exemption ADR-0043
      grants them is not real; (b) **drop retry-once** — re-applying a
      mutation to freshly-loaded state is the silent overwrite this work
      removes; (c) the **two-layer design** and why either alone is
      insufficient; (d) the `If-Match` transport decision, justified by
      the 14 bodiless endpoints; (e) the 409-vs-412 call.
- [ ] **T002 [ADR]** Add `**Superseded in part by:** ADR-0113` to
      ADR-0043's header so a reader landing there first is not misled.

---

## Phase 2: Core mechanism — make the token move

Lands in **one change** with Phase 3. Per plan.md's sequencing hazard, a
bump without the handling turns today's silent bug into 500s.

### Tests first (the interceptor is system-wide; test before wiring)

- [ ] **T003 [P] [CORE]** `AggregateVersionInterceptorTests` in
      `tests/ServiceDefaults.Tests/Persistence/`: a `Modified` root is
      incremented by exactly 1.
- [ ] **T004 [P] [CORE]** `Added` roots are **not** bumped; `Unchanged`
      roots with no dirty descendants are not bumped and stay `Unchanged`.
- [ ] **T005 [CORE]** **The subtle case** — a root whose own columns are
      untouched but which has a dirty *owned* descendant (added,
      modified, deleted: three cases) **is** bumped and promoted to
      `Modified`. Without this assertion `Layout` and `Overlay` stay
      unprotected, which is most of the point of the change.
- [ ] **T006 [CORE]** After a bump, `OriginalValue` is unchanged while
      `CurrentValue` is `+1` — the `WHERE` predicate still targets the
      loaded version. Assert on the `PropertyEntry`, not generated SQL.

### Implementation

- [ ] **T007 [CORE]** Non-generic aggregate-root marker in
      `src/Shared.Kernel/`, mirroring the `IValueObject<T>` convention,
      implemented by `AggregateRoot<TIdentifier>`. Needed because the
      interceptor cannot pattern-match an open generic. `Version` keeps
      its `protected set` — EF writes through the change tracker.
- [ ] **T008 [CORE]** `AggregateVersionInterceptor : SaveChangesInterceptor`
      in `src/ServiceDefaults/Persistence/`. Skip `Added`/`Deleted`; bump
      when `Modified` **or** any owned descendant is dirty (recursive
      traversal); bump via
      `entry.Property(...).CurrentValue = OriginalValue + 1`.
- [ ] **T009 [P] [CORE]** Explicit `Microsoft.EntityFrameworkCore`
      `PackageReference` in
      `src/ServiceDefaults/SmartSentinelEye.ServiceDefaults.csproj`
      rather than relying on the transitive one from
      `WolverineFx.EntityFrameworkCore`.

**Checkpoint:** T003–T006 green; interceptor wired to nothing yet.

---

## Phase 3: Fallback mapping — the rare true race

Lands with Phase 2. ADR-0047 assigns infrastructure signals to
middleware, so this needs no `try`/`catch` in 18 handlers.

- [ ] **T010 [CORE]** Shared `DbUpdateConcurrencyException` → 409 Problem
      Details mapping, shaped consistently with
      `ApiErrorResults.ToProblem()` (`src/ServiceDefaults/ApiErrorResults.cs:15-20`).
- [ ] **T011 [CORE]** Integration test: two `DbContext`s from the factory
      load the same aggregate and both save; the second fails and
      surfaces as 409. **Not a mocked throw** — a mock proves nothing
      about the EF wiring, which is the entire point of #1154.

---

## Phase 4: Wire the interceptor

- [ ] **T012 [CTX]** Register the interceptor at all nine
      `AddDbContextFactory` sites:
      `AuditObservabilityPersistenceModule.cs:32`,
      `AutomationPersistenceModule.cs:28`,
      `CameraCatalogInfrastructureModule.cs:40` (note: inconsistent file
      name — no separate persistence module),
      `EventIngestionPersistenceModule.cs:29`,
      `IdentityPersistenceModule.cs:23`,
      `LayoutCompositionPersistenceModule.cs:27`,
      `OverlayDesignerPersistenceModule.cs:27`,
      `StreamDistributionPersistenceModule.cs:28`,
      `SystemVariablesPersistenceModule.cs:29`.
      **AuditObservability also has an extra `AddScoped` registration at
      `:34`** — the interceptor must go on both, and the comment at
      :22-31 explains why a second `AddDbContext` breaks MigrationRunner.
- [ ] **T013 [CTX]** Confirm and document that `AuditEventRepository` is
      unaffected — it buffers rows and issues raw
      `ExecuteSqlInterpolatedAsync` upserts, never calling
      `SaveChangesAsync`, so it bypasses the change tracker. Record the
      exclusion in the PR body rather than "fixing" it.

**Checkpoint:** `aspire run` healthy; existing integration suite green.
The token is live everywhere and conflicts return 409, not 500.

---

## Phase 5: Cross-request layer

- [ ] **T014 [XREQ]** Shared `If-Match` helper in `src/ServiceDefaults/`:
      parse the header into an expected version; **reject a mutating
      request that omits it**. A silent fallback would recreate exactly
      today's bug. Decide the missing-header status here (428 vs 400) and
      record it in ADR-0113.

### LayoutComposition — 5 mutate commands, 4 bodiless endpoints

- [ ] **T015 [P] [XREQ]** `Version` on `LayoutDto`
      (`src/LayoutComposition/Application/DTOs/LayoutDto.cs:9`); emit it
      as `ETag` on `GET /layouts/{id}`.
- [ ] **T016 [P] [XREQ]** `LayoutRevisionStale` case in all five error
      unions (`PublishRevisionErrors.cs`, `ArchiveRevisionErrors.cs`,
      `BranchDraftRevisionErrors.cs`, `EditDraftRevisionErrors.cs`,
      `RevertRevisionErrors.cs`), shaped after
      `PublishRevisionError.InvalidStateTransition`
      (`PublishRevisionErrors.cs:25`), status `Conflict`.
      **Closes #240 and #283.**
- [ ] **T017 [XREQ]** Thread the expected version through the five
      commands and handlers; compare after load, **before** mutating.
- [ ] **T018 [XREQ]** Read `If-Match` on all five mutating endpoints —
      `LayoutEndpoints.cs:46, 53, 59, 66, 73` (four are bodiless).
- [ ] **T019 [XREQ]** Update existing mutating integration tests for the
      now-required header. Mechanical but broad — belongs in this change,
      not a follow-up.
- [ ] **T020 [XREQ]** Integration test: `GET` → mutate → mutate again
      with the stale `ETag` → 409, deterministically and without racing.

### OverlayDesigner — identical by ADR-0104

ADR-0104 §"Intentional-pattern note" requires the sibling context to
receive the same lifecycle change. Structure mirrors T015–T020 exactly.

- [ ] **T021 [P] [XREQ]** `Version` on `OverlayDto` (`OverlayDto.cs:8`) + `ETag`.
- [ ] **T022 [P] [XREQ]** Stale case in the five error unions.
- [ ] **T023 [XREQ]** Thread + compare in the five handlers.
- [ ] **T024 [XREQ]** `If-Match` on `OverlayEndpoints.cs:46, 53, 59, 66, 73`.
- [ ] **T025 [XREQ]** Update existing integration tests.
- [ ] **T026 [XREQ]** Stale-version integration test.

### SystemVariables — 2 mutate commands

- [ ] **T027 [P] [XREQ]** `Version` on `VariableDto` (`VariableDto.cs:9`) + `ETag`.
- [ ] **T028 [P] [XREQ]** Stale cases in `SetVariableValueErrors.cs`,
      `ArchiveVariableErrors.cs`.
- [ ] **T029 [XREQ]** Thread + compare in both handlers.
- [ ] **T030 [XREQ]** `If-Match` on `SystemVariableEndpoints.cs:57`
      (`PUT /{name}/value`) and `:65` (bodiless archive).
- [ ] **T031 [XREQ]** Integration test. `PUT /{name}/value` is the
      cleanest lost-update in the system — prioritise this one.

### Automation — 2 mutate commands, both inline-error

- [ ] **T032 [P] [XREQ]** `Version` on `RuleDto` (`RuleDto.cs:21`) + `ETag`.
- [ ] **T033 [P] [XREQ]** Stale cases — **declared inline** in
      `ArchiveRuleCommand.cs:11` and `PublishRuleCommand.cs:11`, not in a
      `*Errors.cs` file. See trap 1.
- [ ] **T034 [XREQ]** Thread + compare; `If-Match` on
      `RulesEndpoints.cs:43, 49` (both bodiless).
      **Do not touch `:73` (dry-run)** — it persists nothing.
- [ ] **T035 [XREQ]** Integration test.

### Identity — 2 mutate + 1 hybrid

- [ ] **T036 [P] [XREQ]** `Version` on `RegisteredClientSummaryDto`
      (`RegisteredClientSummaryDto.cs:9`) + `ETag`. Note the other three
      Identity DTOs are POST-response-only and need nothing.
- [ ] **T037 [P] [XREQ]** Stale cases — **inline** in
      `DisableDeviceCommand.cs:11`, `DisableKioskCommand.cs:11`,
      `RotateWebhookClientCommand.cs:23`.
- [ ] **T038 [XREQ]** Thread + compare. **`RotateWebhookClient` is an
      upsert** — gate only the mutate branch
      (`RotateWebhookClientCommandHandler.cs:66`), never the register
      branch (`:91-94`), which has no prior version to compare against.
- [ ] **T039 [XREQ]** `If-Match` on `DevicesEndpoints.cs:36`,
      `KiosksEndpoints.cs:36` (both bodiless DELETEs), and
      `WebhookRotationEndpoints.cs:33`.
- [ ] **T040 [XREQ]** Integration test.

### EventIngestion — 1 mutate command

- [ ] **T041 [P] [XREQ]** `Version` on `WebhookIntegrationDto`
      (`WebhookIntegrationDto.cs:8`) + `ETag`.
- [ ] **T042 [P] [XREQ]** Stale case in `RevokeWebhookIntegrationErrors.cs`.
- [ ] **T043 [XREQ]** Thread + compare; `If-Match` on
      `WebhookIntegrationsEndpoints.cs:41` (bodiless DELETE).
- [ ] **T044 [XREQ]** Integration test.

### Deliberate exclusions

- [ ] **T045 [XREQ]** Record these in the PR body so a later reader does
      not "fix" them:
      - **`ReportStreamHealth`** is mutate-existing but **machine-driven**
        (health watcher, not an operator). An expected-version gate would
        be wrong — there is no client holding a stale view. StreamDistribution
        has **no mutating HTTP surface at all**.
      - **CameraCatalog** — create-only; no update path exists.
      - **AuditObservability** — zero aggregate roots, zero commands;
        `AuditEvent` is explicitly append-only
        (`AuditEvent.cs:24`).
      - **`Event` / `DeadLetter`** — ingestion path, written once, never
        operator-edited. They get the Phase 2–4 layers, no `If-Match`.
      - **`AuthorizeWhep`**, **dry-run**, **`/streams/authorize`** — not
        mutations despite their placement/verb.

---

## Phase 6: Frontend

Two corrections to plan.md, from the inventory:

1. **management-web only, not "both SPAs."** kiosk-web is read-only by
   design — `apps/kiosk-web/src/app/store.ts:8-9` states no mutations
   originate there.
2. **`Camera` and `Stream` drop out.** `cameras.api.ts` has only
   `registerCamera` (a create) and `listCameras` — no single-entity GET
   to source an ETag from and no update path. `streams.api.ts` is
   read-only. The real surface is **Layout, Overlay, Variable, Rule**.

All seven RTK Query slices live in `apps/shared/src/api/` and route
through one chokepoint — so transport is one file, not seven.

### Transport

- [x] **T046 [FE]** `If-Match` on every mutating layout endpoint, from
      the version the client already holds. The shared `ifMatch()` helper
      in `gateway.ts` is the single source of the header format, mirroring
      the backend's `ConcurrencyHeaders.ETag`. Note the response-header
      problem that ruled out central capture: `fetchBaseQuery`'s default
      `responseHandler` discards `Response.headers`, so a tag could not
      be read from `result.data` without replacing the handler — moot now
      that the version travels on the body.
- [x] **T047 [P] [FE]** ~~ETag store keyed by entity~~ — **superseded
      2026-08-02.** A central store must map a request URL back to the
      resource whose tag guards it (`POST /layouts/{id}/revisions/2/publish`
      is guarded by the tag from `GET /layouts/{id}`), and any miss
      degrades to a request with no version — the silent fallback
      ADR-0113 rejects. The version is threaded through each mutation's
      arguments instead, so the type checker rejects a call site that
      forgets. The header format lives in one place, `ifMatch()` in
      `gateway.ts`.
- [ ] **T048 [FE]** Exclude `dryRunRule` (`rules.api.ts:91`) from
      `If-Match` — a mutation only in RTK's HTTP-verb sense.

### Conflict handling — current behaviour is actively wrong

- [ ] **T049 [FE]** Status-aware error helper alongside
      `apps/shared/src/api/problemDetail.ts:11`, which is **status-blind**
      and cannot distinguish 409 from 400/500.
- [ ] **T050 [FE]** Fix the conflict copy in `LayoutEditorDialog.tsx:113`
      and `OverlayEditorDialog.tsx:61`. Both render **"Could not save …
      Try again."** — on a 409 that instructs the operator to perform the
      exact overwrite this work prevents. The dialogs already stay open
      on error (`LayoutEditorDialog.tsx:100-110`), so only the message
      and the refetch are missing.
- [ ] **T051 [FE]** Surface swallowed mutation errors. `LayoutsPage.tsx:42`
      discards with `if ('error' in result) return;`;
      `SystemVariablesPage.tsx:29` never inspects the result and then
      clears the pending edit (:30-32), so on a conflict the operator
      watches their value vanish and the old one return, unexplained. The
      `role="alert"` banners there are bound to the **list query's**
      error, not the mutation's.
- [ ] **T052 [FE]** Conflict UX: reload-and-discard as the honest first
      cut. **Never retry automatically.**
- [ ] **T053 [P] [FE]** If conflicts are logged via
      `apps/shared/src/observability/resilienceLog.ts:9`, they need a new
      subsystem beyond `'stream' | 'hub' | 'session' | 'crash'`. The
      comment at :4-7 declares the format a breaking-change contract
      asserted on by Playwright — a deliberate change, not incidental.

### Per-context transport — added 2026-08-02

The plan budgeted frontend transport **once**, for Layout, because it
assumed a central ETag store would cover every context at the same time.
That store was dropped (T047), so each context now needs its own small
transport step — and it must land **before** that context's server-side
requirement, or its mutations 428 from the running UI.

Missing this for Overlay was caught while sequencing T023; recording the
rest so the remaining three do not surface the same way.

- [x] **T060 [FE]** OverlayDesigner — send `If-Match` on the four mutating
      overlay endpoints. Blocks T023-T026.
- [ ] **T061 [FE]** SystemVariables — send `If-Match` on `setVariableValue`
      and `archiveVariable`. Blocks T029-T031.
- [ ] **T062 [FE]** Automation — send `If-Match` on publish + archive.
      **Not `dryRunRule`** (T048). Blocks T034-T035.
- [ ] **T063 [FE]** Identity / EventIngestion have no SPA client, so their
      server-side halves (T038-T044) need no transport step. Confirm before
      requiring the header there — a headless caller would 428 with no UI to
      fix.

### E2E

- [ ] **T054 [FE]** Two-context conflict test modelled on
      `e2e/layouts.spec.ts:28`. `support/sign-in.ts:7` is single-identity
      and nothing in `e2e/` uses `browser.newContext()` — but **two
      contexts sharing the same `operator` user suffice**, since
      concurrency is per-aggregate, not per-user. No second Keycloak user
      needed.
- [ ] **T055 [P] [FE]** E2E for `setVariableValue`
      (`systemVariables.api.ts:65`) — the cleanest lost-update in the
      system and **currently covered by no e2e test at all**.

---

## Phase 7: Polish

- [ ] **T056 [P] [POLISH]** Add
      `.ProducesProblem(StatusCodes.Status409Conflict)` to
      LayoutComposition's endpoints, which currently lack it, wherever a
      409 became reachable.
- [ ] **T057 [P] [POLISH]** Close #240 and #283; re-evaluate #843 against
      the delivered shape.
- [ ] **T058 [POLISH]** Coverage gates hold (Domain ≥ 90, Application ≥
      80, Shared ≥ 90 — ADR-0065). `ServiceDefaults` gains the
      interceptor; confirm no gate dips.
- [ ] **T059 [POLISH]** Phase-5 verification note on the PR: the two
      integration tests from T011 and T020 cited by name, plus the e2e
      run. "Done" is those observations, not a green compile.

---

## Dependency notes

- Phase 1 blocks everything.
- Phases 2 + 3 **must land together** — plan.md's sequencing hazard.
- Phase 4 requires 2 + 3.
- Phase 5's six context blocks are **independent of each other** and
  parallelisable once Phase 4 is in. Within a block, the `[P]` pair
  (DTO + error cases) can run concurrently; thread/endpoint/tests are
  sequential after them.
- **Phase 6's transport (T046-T048) must land BEFORE Phase 5's endpoint
  half (T018 and its per-context siblings).** Corrected 2026-08-02 —
  the original order had the server requiring `If-Match` while the SPA
  still did not send it, which returns 428 on every layout mutation from
  the real UI until the frontend catches up. Sending a header the server
  ignores is a no-op, so the client goes first. Recorded in ADR-0113's
  Implementation Notes.
- Phase 6's conflict handling (T049-T053) still requires the Phase-5
  blocks, since there is no 409 to handle until the server compares.
- **Each context's transport task (T060-T062) blocks its own server-side
  block**, not just Layout's: T060 → T023-T026, T061 → T029-T031,
  T062 → T034-T035.
- T054/T055 require Phase 6 transport.

## Implementation strategy

**The risk here is the migration, not the feature.** The system has a
silent bug today; a half-landed fix has a loud one. Hence: decide (1) →
build the mechanism with tests but wire nothing (2–3) → turn it on (4) →
add the operator-facing layer (5–6).

**Suggested PR sequence:**

1. **PR A** — ADR-0113 + the ADR-0043 pointer (T001–T002).
2. **PR B** — interceptor, marker, tests, fallback mapping, real
   two-context conflict test (T003–T011). Wired to nothing.
3. **PR C** — register across all nine contexts (T012–T013). Small diff,
   high blast radius; deserves its own review.
4. **PR D** — shared `If-Match` helper (T014) + LayoutComposition and
   OverlayDesigner together (T015–T026), since ADR-0104 wants them in
   lockstep and they are 10 of the 18 gated commands.
5. **PR E** — SystemVariables, Automation, Identity, EventIngestion
   (T027–T045).
6. **PR F** — frontend + e2e (T046–T055).
7. **PR G** — polish (T056–T059).

## Notes

- Task IDs are stable; GitHub issues are created from them in Phase 3's
  second half and reference `T0NN` in the title, matching specs 001–011.
- #240 and #283 fold into T016 rather than being worked separately.
- #843 (Automation fab guard) is **not** part of this work — it belongs
  to #1155's tenancy decision.
