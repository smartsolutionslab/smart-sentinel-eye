# Tasks 306 — A null that says which null (#2540)

**Phase 4a colour: RED (behaviour-changing)** — plan §7. New facts must be
observed failing before any `src/` change; the integration red ("property
`fabAttribution` absent") is the load-bearing one. The two spec-217 exposure
facts are **not** red/green subjects: assertions untouched, comments only.

Engineer: **backend-engineer**. Single context (AuditObservability); no
foundational/shared task — nothing blocks on Shared.Kernel, Contracts or AppHost.

Format: `[ID] [P?] [Story] — owner — description`

## Phase 4a — tests first (test-writer)

- [ ] **T001 [P] [US1]** — test-writer — Add plan §8.1's five facts to
  `tests/AuditObservability.Domain.Tests/AuditEvent/AuditEventTests.cs`. Run;
  quote output (compile-red expected: `FabAttribution`/`FabScope` undefined).
- [ ] **T002 [P] [US1]** — test-writer — Add plan §8.2's four facts to
  `tests/AuditObservability.Application.Tests/EventHandlers/IntegrationEventAuditHandlerTests.cs`;
  new `tests/AuditObservability.Application.Tests/EventHandlers/FabNeutralEventsTests.cs`
  (three facts); add `The_returned_row_carries_its_fab_attribution_by_name` to
  `tests/AuditObservability.Application.Tests/Queries/Handlers/GetAuditEventQueryHandlerTests.cs`.
  Run; quote output.
- [ ] **T003 [P] [US1]** — test-writer — Add plan §8.3's two facts to
  `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs`
  and two to `tests/Integration.Tests/AuditObservability/NeutralFabRetentionRowIntegrationTests.cs`,
  reading `fabAttribution` via `JsonElement` so they compile today. No new
  class → no shard-filter edit. Run against the Aspire fixture
  (`--filter "FullyQualifiedName~UnresolvedFabAuditRowIntegrationTests|FullyQualifiedName~NeutralFabRetentionRowIntegrationTests"`);
  quote output: the four new facts **red at runtime**, the twelve existing facts
  green.
- [ ] **T004 [US1]** — test-writer — *Depends on T003 (same file).* Capture the
  comment-stripped SHA-256 of `UnresolvedFabAuditRowIntegrationTests.cs` **after
  T003, before T004's edit**; replace the two `<summary>` blocks with plan §8.4's
  text verbatim; re-hash and quote both hashes (must be identical). Do not touch
  either assertion.

T001, T002, T003 own disjoint files → parallel. T004 follows T003.

## Phase 4b — implementation (backend-engineer; tests may not be edited)

- [ ] **T005 [US1]** — backend — Domain (plan §2): new
  `src/AuditObservability/Domain/AuditEvent/FabAttribution.cs`,
  `FabScope.cs`; `AuditEvent.cs` — property, `V1Envelope` trailing
  `FabScope FabScope = FabScope.Owned`, rule in `From`; doc-comment fixes in
  `AuditEvent.cs` (`Fab`) and `FabIdentifier.cs`. → T001 green.
- [ ] **T006 [US1]** — backend — *Depends on T005.* Application (plan §3): new
  `src/AuditObservability/Application/EventHandlers/FabNeutralEvents.cs`;
  `IntegrationEventAuditHandler.cs` passes `FabScope`; `DTOs/AuditRowDto.cs` +
  `Queries/Handlers/AuditRowMapper.cs` add `FabAttribution`; mechanical update of
  `AuditRowDto` constructions in `GetAuditEventQueryHandlerTests.cs` (new
  component only — no assertion change). → T002 green.
- [ ] **T007 [US1]** — backend — *Depends on T005.* Infrastructure (plan §4):
  `Persistence/Configurations/AuditEventConfiguration.cs`;
  `Persistence/AuditEventRepository.cs` insert column; `dotnet ef migrations add
  AuditFabAttribution` in `Persistence/Migrations/` + backfill SQL + hand-trim
  spurious churn. Confirm `git diff --stat` shows migration, Designer and
  snapshot. T006 and T007 touch disjoint files → may run in parallel after T005.
- [ ] **T008 [US1]** — backend — *Depends on T006, T007.* Stop any running
  stack; Release build (analyzers clean); run T001–T003 suites → all green;
  run plan §8.5's regression net unmodified (full Architecture.Tests, the five
  named integration classes). Quote output. A migration failure on boot (A1) is
  reported verbatim, not worked around.

## Phase 5 — verify

- [ ] **T009 [US1]** — orchestrator — spec §Independent procedure steps 1–5
  against a booted stack (check AppHost PID start time vs commit); include the
  `GROUP BY` over the persistent volume (SC-9, A2) and list any unexpected
  null-fab `event_kind`. Write `specs/306-a-null-that-says-which-null/verification.md`.
  Confirm `git diff` empty for `Queries/Handlers/*QueryHandler.cs` and
  `Api/AuditEndpoints.cs` (FR-006).

## Phase 6 / 7

- [ ] **T010** — `/code-review` + `/security-review` (plan §10).
- [ ] **T011** — PR to `develop` (`--base develop`), `Closes #2540`; quote
  T001–T004 red and T008 green outputs; state that read-path behaviour is
  deliberately unchanged; recommend (not `agent:ready`) follow-ups: (a) a human
  decision on closing the `Unresolved` exposure, keyed on `fabAttribution`;
  (b) frontend `audit.api.ts` field + its stale "null = cross-fab" comment.
  Re-check spec number 306 against remote branches/open PRs first (305 is held
  by PR #2731).
