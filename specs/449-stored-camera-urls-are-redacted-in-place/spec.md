# Spec 449: Stored camera URLs in audit rows are redacted in place

**Issue:** #2990 (feature-level, on Project #13) · **Branch:** `feat/2990-redact-audit-urls`
**ADRs:** **ADR-0184** (the decision; Decisions 1-9 and Implementation Notes 1-7 are this spec's requirements, cited
by number rather than restated), ADR-0101 (amended by ADR-0184 Decision 5: "no updates, no deletes" now admits one
ADR-authorised migration), ADR-0067 (MigrationRunner; a failure fails the run), ADR-0050 (`[LoggerMessage]`),
ADR-0103 (Aspire fixture, no Testcontainers), ADR-0139 / ADR-0144 (red first, autonomous lane), ADR-0179 (mask-on-read
covers `GET /cameras*` only), ADR-0185 (the outgoing table of `wolverine_camera_catalog` is a fourth place a full URL
rests; **not** a rewrite target, recorded only), ADR-0118 (no production deployment yet).
**Lane:** autonomous · **Phase-4a colour:** **red** (behaviour-changing) · **Engineer:** backend-engineer only
(AppHost two-line wiring included; no separate infra-engineer slice).
**Security-sensitive:** **yes** — edits an append-only audit trail irreversibly. `security-reviewer` is mandatory at
phase 6.
**New ADR needed:** **no.** Every design choice below is dictated by ADR-0184's Implementation Notes. One conditional
stop is carried forward from it: if `UPDATE` on a compressed chunk fails for a reason other than the missing step
(R4), the lane **parks and reports**; decompressing chunks would need an owner ruling.
**Written 2026-10-24 against `develop` at `8479dcdb`.**

---

## Premise, checked at `8479dcdb`

1. **Holds.** `AuditPayloadRedaction` (`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`)
   registers exactly `CameraRegisteredV1.Url`, `CameraAddressChangedV1.PreviousUrl`, `CameraAddressChangedV1.Url`
   (public `UrlFields`). `event_kind` = `message.GetType().Name`. Keys are PascalCase (default `JsonSerializer`).
2. **Holds.** `Serialize` is not idempotent on a stored payload (it recomputes `DetailChanged`); the rewrite must call
   `UrlRedaction.ToOrigin` (`src/Shared.Kernel/UrlRedaction.cs`) on the three string values only.
3. **Holds.** `AuditEventRepository.SaveAsync` is `INSERT ... ON CONFLICT (event_identifier, occurred_at) DO NOTHING`;
   no code path updates a row. `AuditRowMapper` and the archiver do not redact on read.
4. **Holds.** `audit_events` is a hypertable with `add_compression_policy(..., INTERVAL '30 days')`
   (`InitialAuditObservability`); DDL failed there with 0A000 twice; **no test calls `compress_chunk`**, so DML on a
   compressed chunk is unverified (R4 settles it).
5. **Holds.** `AuditObservabilityMigrator.RunAsync` only calls `MigrateAsync`; MigrationRunner calls
   `AddAuditObservabilityPersistence()` (`src/MigrationRunner/Program.cs:44`); every API service is gated on it
   (`ServiceDependencies.GateOnMigrations`, `AppHost.cs:470-477`). `SchemaVersion.Current` is `1`; no row is at `2`.
6. **Holds.** No `AuditObservability.Infrastructure.Tests` project exists; Docker-free Infrastructure tests live in
   `Architecture.Tests` (precedent `AuditEventInsertCharacterisationTests`). Existing partial idempotence tests:
   `UrlRedactionAdversarialTests.The_reduction_is_idempotent_on_its_own_output`, `UrlRedactionUserInfoAsHostTests`.
7. **Out of scope, as the ADR rules:** archived NDJSON chunks (Decision 3), dead letters (Decision 4), the
   `wolverine_camera_catalog` outgoing table (ADR-0185), free-text fields that might hold a URL, retention (#3092,
   #3115), any operator endpoint or manual command (Note 4).
8. **Spec number 449.** 440-448 taken across `origin/develop`, open PRs, every remote branch and the four worktrees
   (446 = `sse-3078`, 447 = `sse-3127`, 448 merged). Re-check before push.

## User story (P1, the only one)

**As** a security officer, **I want** the camera URLs already stored in the hot-tier audit trail reduced to their
origin, counted first and rewritten only after a human sets a flag, **so that** `GET /audit/*` stops serving
credentials, paths and queries captured before spec 377 or written by an old replica during a rolling deploy.

**Independent test:** seed unredacted `CameraRegisteredV1` / `CameraAddressChangedV1` rows by direct `INSERT`; run the
migrator dry (default) and observe a logged count and byte-identical rows; run it with `Apply = true` and observe the
rows redacted, sized, stamped `2`; run again and observe zero rewrites.

## Acceptance scenarios

```gherkin
Scenario: Dry run is the default and changes nothing (Decision 9)
  Given unredacted rows of both kinds at schema_version 1
  And AuditObservability:UrlRedaction:Apply is absent
  When the MigrationRunner runs
  Then every row's payload::text, payload_size_bytes and schema_version are byte-identical
  And one log line reports mode "dry run", rows examined, the unredacted count per event_kind,
      the oldest and newest occurred_at, and that the applied run is irreversible
  And the log line contains no seeded URL, origin or marker

Scenario: Applied run redacts the three values only (Decisions 1, 2, 6)
  Given the same rows and Apply = true
  When the MigrationRunner runs
  Then Url / PreviousUrl equal UrlRedaction.ToOrigin of the seeded values
  And payload::text contains none of the seeded secrets
  And payload_size_bytes equals the UTF-8 length of the handler's serialisation with redacted values
  And schema_version is 2, no DetailChanged key appeared, null values stayed null
  And the log line reports count-before, rewritten, count-after and the rewritten event_identifiers, no URL

Scenario: Rows that need nothing are not touched (conflict / idempotence)
  Given a post-377 row (redacted at capture, DetailChanged true), a row at schema_version 2,
        and a row of an unrelated kind carrying a full URL in a free-text field
  When an applied run executes
  Then all three are byte-identical, the first stays at schema_version 1

Scenario: Re-run and rolling-deploy residue (Note 3)
  Given an applied run has completed
  When it runs again Then zero rows are rewritten and nothing changes
  When one more unredacted row is inserted and it runs a third time Then exactly that row is rewritten

Scenario: Compressed chunk (Note 2)
  Given an unredacted row older than 30 days whose chunk is compressed with compress_chunk
  When an applied run executes Then the row is rewritten like any other

Scenario: Bad input fails the deploy, not silently (ADR-0067)
  Given a candidate row the step cannot rewrite (the guarded UPDATE throws)
  Then the MigrationRunner run fails, and the exception message names no URL

Scenario: Authorisation
  Then no HTTP endpoint, command or operator path exists; the only human action is the AppHost parameter
       AuditUrlRedactionApply (non-secret), and the step runs with the MigrationRunner's credentials only
```

## Requirements

- **FR-001** The step runs in `AuditObservabilityMigrator.RunAsync` after `MigrateAsync`, on every run (Note 1).
- **FR-002** Candidates: `event_kind IN ('CameraRegisteredV1','CameraAddressChangedV1') AND schema_version = 1`; the
  keys come from `AuditPayloadRedaction.UrlFields` (single source), not from literals.
- **FR-003** A row is unredacted iff some non-null registered string value `v` has `ToOrigin(v) != v` (ordinal). That
  count is logged on every run (Decision 7 evidence).
- **FR-004** Dry run (default, `Apply = false`): no `UPDATE`, no write transaction.
- **FR-005** Applied run: one guarded `UPDATE` per row — `jsonb_set` per changed key, `payload_size_bytes -= delta`
  (`delta` = Σ UTF-8 length of `JsonSerializer.Serialize(old)` − that of `Serialize(new)`), `schema_version = 2`,
  `WHERE audit_id, occurred_at, schema_version = 1, payload->>'<key>' = @old` per changed key. Each statement commits
  on its own, so a failed run leaves every row either finished (`2`) or untouched (`1`) and the next run resumes.
- **FR-006** Recount after the updates; a non-zero recount is logged, not a failure.
- **FR-007** No URL (old or new) in any log line, exception message, metric or trace attribute.
- **FR-008** `AuditUrlRedactionOptions` (`SectionName = "AuditObservability:UrlRedaction"`, `bool Apply = false`)
  bound in `AddAuditObservabilityPersistence`; AppHost `AddOverridableParameter("AuditUrlRedactionApply", "false")`
  → `migrations.WithEnvironment("AuditObservability__UrlRedaction__Apply", ...)`; `OverridableParameters.cs` comment
  count updated (seven → eight non-trapped).
- **FR-009** Runbook additions per Note 6 (retention note linking #3092, first-run procedure, meaning of
  `schema_version = 2`, dead-letter purge line, the ADR-0185 outgoing-table mention).
- **FR-010** File the removal follow-up issue (Note 7) without `agent:ready`, Decision 7's two conditions as AC.

## Success criteria

- **SC-1** Red tests R1-R5 (plan) observed failing before the step exists, failures quoted in the PR.
- **SC-2** Idempotence property `ToOrigin(ToOrigin(x)) == ToOrigin(x)` holds over the whole adversarial corpus; if any
  input breaks it the lane **stops** (ADR-0184 Note 2).
- **SC-3** Characterisation C1 green before and unmodified after.

## Latency budget impact

**N/A.** The step runs in the one-shot MigrationRunner before any API service starts (`GateOnMigrations`); it is on
neither the audit ingest path nor the event-to-overlay path (constitution §IV). Boot cost: one indexed select over
`ix_audit_kind_occurred` plus one `UPDATE` per unredacted row.

## Assumptions (marked)

- **A1** Candidate volume is small (two event kinds × ≤250 cameras × 90 days), so the candidate projection is read in
  one query without keyset batching. Per-row autocommit already makes the step resumable. If the owner expects
  volumes where this matters, a batch size is a later change, not an ADR question.
- **A2** EF command logging does not log parameter values (no `EnableSensitiveDataLogging` anywhere in `src/`), so
  bound old/new URLs do not reach logs; the security review confirms against `DatabaseCommandLogLevelTests` and the
  MigrationRunner's `PostgresNoticeLoggingInterceptor`.
