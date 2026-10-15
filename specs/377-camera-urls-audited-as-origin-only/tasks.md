# Tasks 377: Camera URLs are audited as their origin only

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2977 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (behaviour-changing: the stored, returned and archived payload changes),
**plus an adversarial pass**. Every red test compiles on unchanged code — the unit and archive tests
drive the existing `IntegrationEventAuditHandler`, and the architecture test resolves
`AuditPayloadRedaction` by name — so each must be seen **failing on an assertion**, not on a compile
error. A test that arrives green is a phase-4 failure, except the characterisation facts named in
T002, which must arrive green.

**Engineer:** backend-engineer (T010-T012). **Test-writer:** T001-T004. **Test-adversary:** T020.
**Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.

**Forbidden in every task:** the shape of any record in `Shared.Contracts` (T012's doc-only remark
excepted); `RtspUrl.cs`, `StreamSourceUrl.cs`, `UrlRedaction.cs`; `AuditRowMapper.cs` logic and
`AzureBlobAuditChunkArchiver.cs` (they inherit; no redact-on-read); `SchemaVersion.Current`; any
migration; any existing assertion other than `CameraAddressAuditIntegrationTests.cs:40-41`; the
literals in `AuditChunkArchiverCharacterisationTests`; project references; ADRs, the constitution,
CLAUDE.md; suppressions, thresholds, `.editorconfig`, analyzer configs; existing audit rows or blobs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** T010 (`AuditPayloadRedaction`) blocks T011. T001-T004 own four disjoint test files
and run in parallel. T012 (docs) is `[P]` with T010/T011.

---

## Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] New
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadRedactionTests.cs` per plan
  §5.1, red facts only: registration, same-origin change (`DetailChanged == true`), cross-origin,
  identical, unparseable, empty, null, and the **API DTO sink** via `AuditRowMapper.Map`. Assert on the
  parsed JSON and on the raw stored string with ASCII markers. Expected red: every `Url`/`PreviousUrl`
  value and marker assertion fails; `DetailChanged` lookups fail as absent.
- [ ] **T002** [P] [US1] Characterisation in the same project, separate file
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadPassThroughCharacterisationTests.cs`:
  `CameraRenamedV1`, `FabEventIngestedV1`, `OverlayRevisionPublishedV3` (non-integral decimals) stored
  payloads equal `JsonSerializer.Serialize(message, message.GetType())`. **Expected green now** and
  must stay green unmodified after T011.
- [ ] **T003** [P] [US1] New `tests/Architecture.Tests/AuditPayloadRedactionCoverageTests.cs` per plan
  §5.2: the classification fact (verbatim inventory derived by running the enumeration, then pinned
  as literals), the register pin, and the probe counterfactual (`ProbeV1` with a misleadingly named
  URL field and a nested record is reported unclassified). Expected red: `AuditPayloadRedaction` type
  not found (assert it, so the failure is an assertion message, not a `NullReferenceException`).
- [ ] **T004** [P] [US1] Add one Fact to
  `tests/Architecture.Tests/Persistence/AuditChunkArchiverCharacterisationTests.cs` per plan §5.3
  (archive sink). No existing literal edited. Expected red: the marker is present in the gunzipped
  NDJSON.

Run, and return **verbatim** output for each:

```sh
dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayloadRedactionTests|FullyQualifiedName~AuditPayloadPassThroughCharacterisationTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~AuditPayloadRedactionCoverageTests|FullyQualifiedName~AuditChunkArchiverCharacterisationTests"
```

- [ ] **T005** [US1] Integration (written now, red argued, observed in CI): in
  `tests/Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs` (shard-4; no new class,
  so no `ci-shards/*.filter` edit) change lines 40-41 to the origins per plan §5.4 and add
  `A_query_string_credential_reaches_neither_the_audit_row_nor_the_audit_api`. Docker is down
  locally: do **not** boot a stack; state that its red is argued from T001 and that CI shard-4 is
  authoritative after T011.

Commit T001-T005 together: `test(2977): pin camera URLs in audit payloads to their origin (red)`.
The commit builds; the suite it belongs to is red until T011's commit (ADR-0087 consequence, as in
CLAUDE.md's bisect note).

Gate: the orchestrator holds the verbatim red output of T001, T003, T004 and the green output of
T002 before dispatching Phase 2.

## Phase 2: Fix (backend-engineer; depends on Phase 1)

- [ ] **T010** [US1] **Foundational.** New `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`
  per plan §2.1 (register with `nameof`, pass-through for unregistered types, `with`-copy path,
  fail closed on an absent registered field, null left null, `DetailChanged` from the typed message).
- [ ] **T011** [US1] `IntegrationEventAuditHandler.cs:75` →
  `Payload: AuditPayloadRedaction.Serialize(message)`. Nothing else in the handler. T001-T004 green;
  T002 unmodified.
- [ ] **T012** [P] [US1] Doc-only: `AuditRowDto.cs` summary, `AuditPayload.cs` remarks,
  `CameraAddressChangedV1.cs` remarks (spec §6 assumption 3). Comments only; verify with the
  strip-comments-and-hash characterisation for the contract file.

Verify (all must be clean):

```sh
dotnet build -c Release
dotnet test tests/AuditObservability.Application.Tests -c Release
dotnet test tests/AuditObservability.Domain.Tests -c Release
dotnet test tests/Architecture.Tests -c Release
dotnet test tests/Shared.Kernel.Tests -c Release
dotnet format --verify-no-changes
```

Commit: `fix(2977): audit camera URLs as their origin only`.

## Phase 3: Adversarial (test-adversary; depends on T011)

- [ ] **T020** [US1] Extend `AuditPayloadRedactionTests.cs` (or a sibling
  `AuditPayloadRedactionAdversarialTests.cs`) with plan §5.5's cases. Any case that leaks is a
  phase-4 failure returned to T010, not a test to adjust. Commit:
  `test(2977): adversarial cases for audit URL redaction`.

## Phase 4: Follow-ups and verification

- [ ] **T030** File the follow-up issue (spec FR-009): *existing audit rows and archived blobs still
  hold full camera URLs* — options rewrite (redaction is idempotent; `DetailChanged` computable from
  the old raw values) vs retention; label as needing an owner decision (not `agent:ready`); add it to
  Project #13. Reference it in the PR body. Do **not** close #2977 from a subagent.
- [ ] **T031** Phase 5 note: quote the stored payload of a same-origin change from the in-process
  test, and CI shard-4's result for `CameraAddressAuditIntegrationTests` (download the trx before
  any re-run). Latency: N/A (spec header).

## Dependencies

```
T001 ─┐
T002 ─┤
T003 ─┼─> gate ─> T010 ─> T011 ─> T020 ─> T031
T004 ─┤                 └ T012 [P]
T005 ─┘                                 T030 (any time after T011)
```
