# Tasks 459: Models become DTOs through extension methods

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3154 (feature-level, on Project #13)
**Slice:** 1 of 1 (ten commits, one PR). **Engineer:** backend-engineer (C# Application/Api only; frontend untouched).
**Colour:** **characterisation** — behaviour-preserving. The net (existing tests + the 11 new characterisation classes)
is captured green at `c683329c` before any `src/` edit and passes **unmodified** after every commit; the single permitted
test edit is plan §"The one permitted test edit". A test that goes red during a move is a regression: stop, report it
verbatim, never edit an assertion. The guard (US10) is new and is **observed red at the base** first, committed last.
**Reviewers:** backend-reviewer (all); security-reviewer on the Identity commit (credential DTOs carry secrets).
**New ADR:** no.

**Parallelism (ADR-0109):** T001-T002 are foundational and block everything. US1-US9 (T010-T090) own disjoint files
(each context's `src/<Context>/Application/{DTOs,Queries/Handlers,Commands/Handlers}`; US2 also `src/Identity/Api`) and
are marked `[P]`. One branch has one index — the orchestrator may run them sequentially in this worktree; `[P]` records
that order does not matter, not that two engineers should share the branch. T100 (guard) depends on all of US1-US9.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a — characterisation and red guard (test-writer; blocks 4b)

- [ ] T001 [Foundational] Write the 11 characterisation classes in plan §"Coverage" (9 handler-level, 2 SQL-capture).
      Every field of each listed DTO asserted with a value distinct from every same-typed sibling; drive the existing
      handlers only (no reference to any `*Mappings` symbol — it does not exist yet). SQL-capture tests copy the
      interceptor pattern of `AuditReadScopeTranslationAdversarialTests` and assert the captured SELECT equals the
      literal captured at `c683329c`. Run at `c683329c` with `--logger trx`; quote totals verbatim — **all green**; a
      red one means the test is wrong or a defect was found → stop and report (a defect is a separate issue, not this
      refactor). Commit 0 — the 11 files under `tests/*Application.Tests/**` and `tests/Architecture.Tests/Persistence/`
- [ ] T002 [Foundational] Capture the net: run `dotnet test` over the nine `*.Application.Tests` projects and
      `tests/Architecture.Tests`; save the sorted passed-test-name list (scratchpad, not committed). Write
      `tests/Architecture.Tests/DtoMappingConventionTests.cs` per plan §"The guard"; run it at `c683329c` and quote the
      red output of `No_handler_or_endpoint_builds_a_dto` (expect every inventory site) and of
      `Every_context_that_serves_dtos_has_its_mappings_in_DTOs`; the synthetic C1-C5 facts green. **Do not commit the
      guard yet** — keep it in the scratchpad until T100 (depends T001)

## Phase 4b — moves, one context per commit (backend-engineer; may not edit any test except the T030 rename)

Each task: create the context's `*Mappings.cs` in `Application/DTOs/` per plan §"Site inventory", repoint every caller,
delete the helper/mapper, move bodies **verbatim**. Then `dotnet build -c Release` clean; the T002 net green with the
same names; `git diff` of `tests/` empty (except T030). One commit each, `refactor(3154): …`.

- [ ] T010 [P] [US1] CameraCatalog — `CameraMappings.cs` (`ToDto`, `ToAddressDto`, `ToSummaryDtoExpression`,
      `ToCameraListPageDto`); redaction `with` stays in `ListCamerasQueryHandler`. `ListCamerasProjectionSql…` must
      stay byte-identical — `src/CameraCatalog/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T020 [P] [US2] Identity — `RegisteredClientMappings.cs` (`ToSummaryDtoExpression`, three credential mappings
      with locals as parameters, `ToReplayedClientDto`), `ReplayedClientMappings.cs` (public; three replay mappings);
      repoint `RegisteredClientProjection`, RegisterDevice, EnrollKiosk, RotateWebhookClient (remove `CredentialsOf`,
      keep its "read after SaveAsync" comment at the call site), ReplayRegisteredClient, and the three Api replay
      methods. `RegisteredClientProjectionSql…` byte-identical; the exact-JSON endpoint characterisations green —
      `src/Identity/Application/{DTOs,Queries/Handlers,Commands/Handlers}`, `src/Identity/Api/{Devices,Kiosks,WebhookRotation}Endpoints.cs` (depends T002)
- [ ] T030 [P] [US3] AuditObservability — `AuditEventMappings.cs` (`ToAuditRowDto`, `ToAuditPageDto`); inline both
      `ToPage` bodies' cursor/trim lines into `HandleAsync`; empty page via `List<AuditEvent> none = []`; delete
      `AuditRowMapper.cs`; apply the permitted rename in the 10 EventHandlers test files (and optionally the comment at
      `UnresolvedFabAuditRowIntegrationTests.cs:441`) — `src/AuditObservability/Application/{DTOs,Queries/Handlers}`,
      the 10 files (depends T002)
- [ ] T040 [P] [US4] Automation — `RuleMappings.cs` (from `RuleMapper`, incl. `MapAction` and its `NotSupportedException`
      arm), `DryRunResultMappings.cs` (`NoMatch`, `Matched`); delete `RuleMapper.cs`; `SceneTargetWireMapping` untouched
      — `src/Automation/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T050 [P] [US5] EventIngestion — `EventMappings.cs`, `RegisteredEventTypeMappings.cs`, `SourceModeMappings.cs`,
      `WebhookIntegrationMappings.cs`, `DeadLetterMappings.cs`; post-mapping ordinal sorts unchanged; implicit
      conversions (`RejectedAt`, `Registration.*`, `Declaration.*`, `RegisteredAt`) kept as written —
      `src/EventIngestion/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T060 [P] [US6] LayoutComposition — `LayoutMappings.cs` (incl. tile sort and `ToPublishedLayoutDto` with
      `Single(Published)`), `WallMappings.cs`; repoint GetLayout, ListLayouts, GetWall, ListWalls, EditWallScenes,
      SwitchWallScene (both sites) — `src/LayoutComposition/Application/{DTOs,Queries/Handlers,Commands/Handlers}` (depends T002)
- [ ] T070 [P] [US7] OverlayDesigner — `OverlayMappings.cs` (no `OrderBy` added to elements; admin-list sort stays in
      the handler) — `src/OverlayDesigner/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T080 [P] [US8] StreamDistribution — `StreamMappings.cs` (`ToHealthDto(this Stream, string whepUrl)`; handlers
      compute `whepUrls.For(stream.Path)`) — `src/StreamDistribution/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T090 [P] [US9] SystemVariables — `VariableMappings.cs`, `PlaceholderResolutionMappings.cs`,
      `ResolvedTextPairMappings.cs` — `src/SystemVariables/Application/{DTOs,Queries/Handlers}` (depends T002)
- [ ] T100 [US10] Commit the guard from T002 unchanged; it is now green. Commit 10 —
      `tests/Architecture.Tests/DtoMappingConventionTests.cs` (depends T010-T090)

## Phase 5 — verify

- [ ] T110 Full net vs T002's name list: identical names, all passed; guard green; plant C1-C5 counterfactuals on the
      real tree (scratch edits), quote each red, revert; the two SQL captures byte-identical; tests/ diff limited to the
      T001 additions, T100 and the T030 rename (comment-stripped normalised diff empty); `grep -rnE 'new [A-Z][A-Za-z]*Dto\('
      src --include=*.cs | grep -v /obj/ | grep -v Mappings.cs` → empty; no `RuleMapper`/`AuditRowMapper`/`ToPage`/
      `internal static .*Dto Map` left; `dotnet build -c Release` clean; no Integration.Tests class added → no shard
      filter entry needed (confirm); each of the ten commits builds and passes on its own (ADR-0087) (depends T100)

## Phase 6 — review

- [ ] T120 backend-reviewer: every move verbatim (argument order, implicit conversions, `!`, `ToString()`), locals stay
      parameters, no new public type except `ReplayedClientMappings`, no Domain/Infrastructure edit, no cross-context
      reference; guard V1-V4 and its stated limit. security-reviewer on T020: secrets flow unchanged, no secret in a
      `ToString`/log path introduced
