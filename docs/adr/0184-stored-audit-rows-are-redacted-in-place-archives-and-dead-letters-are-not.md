# ADR-0184: Camera URLs already in audit rows are redacted in place; archived chunks and dead letters are left to age

**Status:** Proposed (records the owner's ruling of 2026-10-21 on #2990, restated 2026-10-22, and the owner's answers of 2026-10-22 to the four questions this ADR raised, folded in as Decision points 6 to 9; awaits owner approval of the ADR as a whole)
**Date:** 2026-10-22
**Amends:** ADR-0101 (one sentence of its Context: "No updates, no deletes")
**Relates to:** ADR-0067, ADR-0118, ADR-0139, ADR-0144, ADR-0153, ADR-0155, ADR-0179 (on PR #3085), spec 009, spec 306, spec 377, issues 2977, 2990, 2999, 3016, 3092

## Context

Spec 377 (#2977) made the audit subscriber store camera URLs as their origin only
(`scheme://host[:port]`). It changed the write path and nothing that was already written. Three
places still hold full camera URLs, including any path, query or userinfo credential (#2990):

| Where | What holds the URL | Verified |
|---|---|---|
| Postgres `audit_events.payload` (jsonb), hot tier, 90 days | rows with `event_kind` `CameraRegisteredV1` (`Url`) and `CameraAddressChangedV1` (`PreviousUrl`, `Url`) written before spec 377, and any row an old replica writes during a rolling deploy | yes, see below |
| Azure Blob container `audit-archive`, cold tier | gzipped NDJSON chunks already archived from those rows | yes, see below |
| Wolverine dead-letter storage | the full original message body of any failed `CameraRegisteredV1` / `CameraAddressChangedV1` delivery, for every subscriber, not only the audit one | location not verified (below) |

**What the code says, on `develop` at `2fa460af`:**

- **The fields.** `AuditPayloadRedaction` (`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`)
  registers exactly three fields: `CameraRegisteredV1.Url`, `CameraAddressChangedV1.PreviousUrl`,
  `CameraAddressChangedV1.Url`. `AuditPayloadRedactionCoverageTests` (Architecture.Tests) pins the
  register against every string property of every integration event. `event_kind` stores
  `message.GetType().Name` (`IntegrationEventAuditHandler.cs:64`), so the two kinds are selectable by
  name. Free-text fields that *could* contain a URL (`FabEventIngestedV1.Payload`,
  `SystemVariable*.Value`) are on the verbatim inventory and outside this ADR, as they were outside
  spec 377.
- **No redaction on read.** `AuditRowMapper.Map` returns `audit.Payload.Content.Value` verbatim, and
  the archiver writes `row.Payload.Content.Value` (spec 377 FR-008). The mask-on-read the owner chose
  for the camera API (#3016, ADR-0179) covers `GET /cameras*` only, not `GET /audit/*`. So a stored
  row is served as stored, and only rewriting it changes what `sse.audit.read` returns.
- **Re-delivery does not repair a row.** `AuditEventRepository.SaveAsync` writes
  `INSERT ... ON CONFLICT (event_identifier, occurred_at) DO NOTHING`; the first row written wins. An
  old replica's unredacted row survives the redelivery of the same event to a new replica.
- **`DetailChanged`.** The redacted `CameraAddressChangedV1` payload carries an appended top-level
  `DetailChanged` member (`true`, `false`, or `null` for unknown). Rows written before spec 377, and
  rows an old replica writes, have no such key; absence means "not computed at capture", never
  `false`.
- **Idempotence.** `UrlRedaction.ToOrigin` (`src/Shared.Kernel/UrlRedaction.cs`) maps an origin to
  itself and the placeholder `<unparseable url>` to itself; spec 377 §6 and the
  `AuditPayloadRedaction` remarks rely on it. **Not proven by a test over the adversarial corpus**
  (IPv6 with zone, explicit ports, non-ASCII hosts per spec 379): the implementation must prove it
  (Implementation Notes, step 2). Note that `AuditPayloadRedaction.Serialize` as a whole is **not**
  idempotent on an already redacted payload: it recomputes `DetailChanged` from the redacted
  addresses and would turn a stored `true` into `false`. A rewrite must therefore redact the three
  string values only, never re-run `Serialize`.
- **`PayloadSizeBytes`** is defined as the UTF-8 byte count of the payload string as serialised by
  the handler (`StoredPayload.From`), not of the jsonb text Postgres returns (jsonb normalises
  whitespace and key order). A rewrite has to adjust it by the difference between the old and new
  JSON-encoded values, not recompute it from `payload::text`.
- **Compression.** `audit_events` is a TimescaleDB hypertable (image `timescale/timescaledb:2.27.1-pg17`,
  `AppHost.cs:78`) with a 30-day compression policy (`InitialAuditObservability`), so rows aged 30 to
  90 days sit in compressed chunks. DDL has already failed there once (SqlState 0A000, recorded in
  `AuditFabAttribution` and `AuditIngestBreakdownColumns`). Whether a row-level `UPDATE` succeeds on a
  compressed chunk in this deployment is **not verified**: no test in the repository calls
  `compress_chunk`.
- **Archived chunks carry an integrity record.** `AzureBlobAuditChunkArchiver` uploads each chunk
  with a Content-MD5, returns it, and the retention worker publishes it in `AuditChunkArchivedV1.ContentMd5`,
  which is itself audited. A rewritten blob no longer matches the hash the trail recorded for it.
- **Cold-tier retention does not exist.** Nothing in `src/` deletes an archived blob or sets a
  lifecycle policy on `audit-archive`; the runbook names the cold tier but no age limit. Archived
  chunks are kept indefinitely.
- **Dead letters.** Wolverine 6.51 persists through `PersistMessagesWithPostgresql(connection, outboxSchema)`
  per context, and listeners run Inline with native acks except the opted-in durable-inbox types
  (`WolverineDefaults.cs`). Whether a failed delivery of these two types lands in the context's
  Postgres dead-letter table or in a RabbitMQ dead-letter queue, and whether any expiry applies, is
  **not verified**: no expiry setting appears in `src/`.
- **There is no production deployment yet** (ADR-0118). The exposure exists in dev and CI stacks and
  their persistent volumes; the decision is about the shape the first production deployment inherits.

**How "append-only" is enforced in this repository.** The migration's legitimacy hinges on this, so
it was searched for, not assumed:

- **Not by the database.** No `REVOKE`, `GRANT` or trigger exists anywhere under `src/` or
  `deploy/` (grep for `\b(REVOKE|GRANT|TRIGGER)\b` in `.cs`, `.sql`, `.yaml`). The audit service
  connects through Aspire's `audit-db` reference with the server's single user (`PostgresUser`,
  default `postgres`, `AppHost.cs:22`), the same user the MigrationRunner uses. That user is a
  superuser by the image's default (**not verified against a running container**), so it can
  `UPDATE` and `DELETE` any row.
- **By the shape of the code.** `IAuditEventRepository` has `Add` and `SaveAsync` only; the one SQL
  statement is the `INSERT ... ON CONFLICT DO NOTHING`. `AuditEvent` has `private init` state and
  `StoredPayload` a private two-argument constructor whose remarks say repairing stored data "is a
  migration, and this feature adds none".
- **By statement.** ADR-0101's Context: "Writes are append-only ... No updates, no deletes (the
  retention worker drops whole partition units, not individual rows)." A comment in
  `PrimitiveBoundaryTests` repeats "append-only". Spec 009 §Out of scope defers tamper evidence
  (signed or Merkle log) and records that the compliance reviewer accepted "standard Postgres
  durability for v1".
- **Precedent.** The append-only statement has been crossed once already: `AuditFabAttribution`
  (spec 306, #2540) added a column and backfilled it with `UPDATE audit_events SET fab_attribution ...`,
  without an ADR. It added derived metadata; it did not change a captured payload. This ADR is the
  first change to captured content.

So append-only is **convention plus code shape**, with no mechanism in the database and no tamper
evidence. A migration that rewrites rows breaks nothing that is enforced; it breaks a stated
property, which is why it is decided here rather than in a spec.

## Decision

The owner ruled on #2990 (2026-10-21):

1. **Postgres audit rows are rewritten.** An idempotent redaction migration replaces the three
   registered camera URL values in stored `CameraRegisteredV1` and `CameraAddressChangedV1` payloads
   with `UrlRedaction.ToOrigin` of the stored value. Re-applying it is a no-op. Rows an old replica
   writes during a rolling deploy get the same treatment.
2. **Only those values change.** No other payload member, no other event kind, and no other column
   except `payload_size_bytes` (kept consistent with the content) and `schema_version` (point 6).
   No `DetailChanged` key is added to a row that lacks one: its absence keeps meaning "not computed
   at capture".
3. **Archived NDJSON chunks are not rewritten.** Each chunk's MD5 is part of the audit trail
   (`AuditChunkArchivedV1`), and rewriting the chunk would falsify the trail's own integrity record.
   The exposure is documented with a retention note and a runbook line instead.
4. **Dead-lettered message bodies are not rewritten.** They are documented with a retention note and
   a runbook line.
5. **ADR-0101 is amended.** Its "no updates, no deletes" becomes: *rows are appended and not updated,
   except by a migration an ADR authorises; this ADR authorises one, scoped to points 1 and 2.* A
   second in-place edit of captured content needs its own ADR.

The owner answered this ADR's four questions (2026-10-22):

6. **A rewritten row is stamped `schema_version = 2`.** New rows keep `SchemaVersion.Current`,
   which stays `1`; nothing in the write path changes. The column's values are defined as:

   | `schema_version` | Meaning |
   |---|---|
   | `1` | The payload is as the subscriber serialised it at capture. For the two camera kinds that is redacted if a post-377 build wrote it and full if a pre-377 build did; the version alone does not say which. |
   | `2` | The payload was captured under version 1 and its registered camera URL fields were then redacted in place by the step this ADR authorises. Every other member, including the presence or absence of `DetailChanged`, is as captured. |

   `2` is therefore a mark of an edit to the append-only trail, carried by the row itself, not a
   different payload shape: a `CameraRegisteredV1` or `CameraAddressChangedV1` payload at `2` has
   the same members as one at `1`. Only the step writes `2`, and only on a row it rewrites.

   **How the step uses it.** The step selects only rows at `schema_version = 1` of the two kinds;
   a row at `2` is finished and never read again. The step's completion check is a count over that
   candidate set: rows at `1` with any registered value not equal to `UrlRedaction.ToOrigin` of
   itself. Zero means no unredacted row remains in the hot tier; the rows the step has ever rewritten
   are exactly the rows at `2`, so `count(*) WHERE schema_version = 2` reconciles against the sum of
   the applied runs' logged rewrite counts.

   **How readers treat it.** `AuditRowDto.SchemaVersion` already exposes the column on `GET /audit/*`
   and no reader, server-side or in `apps/`, branches on it today. Readers and consumers of the audit
   API treat `2` exactly as `1` for parsing: same members, same types. A consumer that cares about
   provenance reads `2` as "camera URL fields redacted after capture (ADR-0184); the original values
   no longer exist". `SchemaVersion.From` already accepts any non-negative value, so no guard
   changes. A future write-path version bump must take `3`, not `2`.
7. **The step runs on every MigrationRunner run for 90 days, then a follow-up removes it.** The
   unredacted-row count of point 6 ships with the step and is logged on every run, dry or applied
   (Implementation Notes, step 1). The removal follow-up may merge when **both** hold: (a) at least
   90 days (the hot-tier window, ADR-0101 / spec 009 FR-013) have passed since the step's first
   **applied** run in the deployment whose data it exists to clean, so every row written before that
   run has aged into the cold tier; and (b) the most recent run there logged an unredacted count of
   zero before it rewrote anything. (b) is the evidence that no pre-377 replica is still writing; a
   non-zero count resets nothing automatically, it blocks the removal until a later run reads zero.
   With no production deployment yet (ADR-0118), the 90 days start at the first applied run of the
   first production deployment; dev and CI stacks do not start the clock. The removal is a separate
   issue (Implementation Notes, step 7). This ADR's amendment of ADR-0101 does not lapse with the
   code: rows at `2` stay in the trail until they age out.
8. **Retention for the cold tier and dead letters is a separate decision**, filed as issue 3092
   ("Archived audit chunks and dead letters holding pre-redaction camera URLs have no retention
   limit"). This ADR's retention note states today's position (kept indefinitely) and links 3092; it
   does not set a limit.
9. **The first run is a dry run.** The step counts and logs, and changes nothing, until a human sets
   a flag after reading the logged count. The flag is a deploy-time setting, not a code change and not
   an operator endpoint, configured the way this repository already configures a service's behaviour
   (Implementation Notes, step 4). **The applied run is irreversible**: once a row is rewritten, no
   setting, rollback or re-run restores its original values (step 5). Setting the flag back to dry
   run after an applied run stops further rewrites and restores nothing.

## Consequences

**Positive.**

- `GET /audit/*` stops returning full camera URLs for every row still in the hot tier, which is
  the only place the API reads.
- Chunks archived after the rewrite has run carry redacted payloads, because the archiver reads
  the rows as stored. From the first run on, the cold tier stops receiving new exposure.
- The rolling-deploy window is closed rather than documented: the step that fixes history also fixes
  whatever an old replica wrote.

**Negative.**

- **The original URLs are destroyed.** The rewrite cannot be undone, by design. A forensic question
  that needed the full historic address (which credential a device used, which stream path) can no
  longer be answered from the hot tier. It can still be answered from chunks archived before the
  run, which is the exposure this ADR accepts.
- **The trail no longer records what was captured, only what remains.** The `schema_version = 2`
  stamp (point 6) says that a row was edited, not what it said before. There is no tamper evidence
  to reconcile against (spec 009), and the stamp is not tamper evidence either: the same database
  user that writes it could write `1`.
- **`schema_version` gains a meaning other than shape.** Until now a version could only have marked
  a payload shape; `2` marks provenance. Recorded so the next write-path bump takes `3` (point 6).
- **A human step precedes the first rewrite** (point 9). Until someone reads the count and sets the
  flag, every run reports exposure and fixes none of it, and the rolling-deploy residue is counted,
  not cleaned.
- **Hot and cold tiers disagree for chunks archived before the run.** The same event reads with an
  origin in Postgres and a full URL in the blob. Both are correct for what they are.
- **A chunk archived but not dropped** (the drop failed after the upload) will, on the next retention
  pass, hash differently from its blob and be **re-uploaded** with redacted content
  (`AzureBlobAuditChunkArchiver` re-uploads on a hash mismatch). That is a blob rewrite by the
  existing archiver, not by this step, and the new MD5 is published in a new `AuditChunkArchivedV1`.
  Rare; recorded so it is not mistaken for a violation of point 3.
- **Exposure in the cold tier and in dead letters does not end on its own today**, because neither
  has a retention limit (Context). The retention note can only say so; setting a limit is issue
  3092 (point 8).
- **Database backups**, where they exist, keep the original rows for as long as backups are kept.
  Out of scope; no production backup regime exists yet (ADR-0118).

## Alternatives Considered

- **Leave everything untouched** (the owner's interim ruling of 2026-10-17). Rejected on
  2026-10-21: the audit API keeps serving the URLs for up to 90 days per row, and an old replica can
  add new ones at every deploy.
- **Redact on read in `AuditRowMapper`.** Rejected by spec 377 FR-008 and not reopened: the stored
  data would still hold the URL, every future reader would have to remember the mask, and the
  archiver would keep copying it into the cold tier.
- **Rewrite the archived chunks too.** Rejected (point 3): it falsifies the MD5 the trail recorded
  for each chunk, and with no tamper evidence in the system, the published hash is the only
  integrity check the archive has.
- **Express the redaction in SQL inside an EF `Migration`.** Rejected for the shape (Implementation
  Notes): `ToOrigin` refuses ambiguous authorities, non-ASCII hosts, IPv6 zones and control
  characters by rules that live in C# (`UrlAuthority`, spec 379). A SQL re-implementation would be a
  second redactor that can disagree with the first; an EF migration also runs once, so it cannot
  reach rows an old replica writes after it.
- **Delete the affected rows instead of rewriting them.** Not proposed by anyone; it would remove
  the record that a camera was registered or moved, which the trail exists to keep.

## Implementation Notes

What the lane builds once this ADR is Accepted and #2990 carries `agent:ready`. Behaviour-changing:
phase 4a is **red** (ADR-0139). Security-sensitive: phase 6 includes `security-reviewer`.

**1. Shape.** A redaction step in `AuditObservability.Infrastructure`, invoked by
`AuditObservabilityMigrator.RunAsync` **after** `MigrateAsync`, on **every** MigrationRunner run
(ADR-0067). It is a migration in the MigrationRunner's sense, not an EF `Migration` class, for the
two reasons in Alternatives. Per run:

- Select `audit_id, occurred_at, payload->'Url', payload->'PreviousUrl'` from `audit_events` where
  `event_kind IN ('CameraRegisteredV1', 'CameraAddressChangedV1') AND schema_version = 1` (served by
  `ix_audit_kind_occurred`). Rows at `2` are finished (Decision point 6) and never read.
- In C#, for each non-null string value compute `UrlRedaction.ToOrigin(value)`. A row whose values
  are all already equal to their origin is skipped and keeps `schema_version = 1`: it was redacted at
  capture, not edited. That comparison **is** the idempotence check and the **unredacted-row count**
  of Decision point 7: the number of selected rows with at least one value unequal to its origin. No
  marker column is added; `schema_version` is the existing column.
- **Dry run** (the default, step 4): log the count and stop. No `UPDATE` is issued, no transaction
  is opened for writing.
- **Applied run**, for each counted row: `UPDATE audit_events SET payload = jsonb_set(...)` for each changed key,
  `payload_size_bytes = payload_size_bytes - delta` (`delta` = UTF-8 length of
  `JsonSerializer.Serialize(old)` minus that of `JsonSerializer.Serialize(new)`, so the size stays on
  the handler's serialisation basis including its escaping), `WHERE audit_id = @id AND occurred_at = @at
  AND schema_version = 1 AND payload->>'<key>' = @old` for every key changed, setting
  `schema_version = 2` in the same statement. The guards make a concurrent second run a zero-row
  update rather than a double edit. After the updates, recount and log it. A non-zero recount is not
  a failure: during a rolling deploy an old replica can insert a row while the step runs, and the
  next run cleans it. It is the number the removal criterion (Decision point 7) cannot pass with.
- Nothing else: no `DetailChanged` key added, null values left null, other event kinds never read.
- Log one structured line per run, `[LoggerMessage]` (ADR-0050). **Never a URL, old or new.**
  - *Dry run:* the mode (`dry run`), rows examined, the unredacted-row count split by `event_kind`,
    the oldest and newest `occurred_at` among the counted rows, and the sentence that no row was
    changed, that setting the flag applies the rewrite, and that the rewrite is irreversible.
  - *Applied run:* the mode (`applied`), rows examined, the unredacted-row count before the
    rewrite (the Decision point 7 evidence), rows rewritten, the count after, and the rewritten
    rows' `event_identifier`s.
- A failure fails the MigrationRunner run (ADR-0067, issue 2062): an audit row that cannot be
  redacted is a visible failed deploy, not a silently skipped row.

**2. Tests, red first** (Integration.Tests against the Aspire stack, ADR-0103; Architecture or
Application tests for the pure parts):

- *Red:* seed rows by direct `INSERT INTO audit_events` (the existing pattern in
  `RetentionRoundtripIntegrationTests`, `DropChunkIdentityIntegrationTests`) carrying
  `?token=SECRET-<guid>`, a path secret and a userinfo credential in `Url` and `PreviousUrl`; run
  the migrator **with the flag set to apply**; assert `payload::text` contains no marker, the values
  equal `ToOrigin` of the seeded ones, `payload_size_bytes` equals the UTF-8 length of the handler's
  serialisation of the redacted message, `schema_version` is `2`, and no `DetailChanged` key
  appeared. Observed failing before the step exists.
- *Red, dry run:* the same seeded rows, migrator run with the flag **unset** (the default): every
  row's `payload::text`, `payload_size_bytes` and `schema_version` are byte-identical before and
  after, and the captured log line reports the seeded count per `event_kind` and contains none of
  the seeded markers. Observed failing before the step exists (no count is logged). A second case
  sets the flag to apply and asserts the same seeded rows are then rewritten: the dry run must not
  have consumed anything.
- *Red, the count:* the unredacted-row count over a mix of one unredacted row, one post-377 row and
  one row already at `2` is exactly `1`, and an applied run logs that `1` before and `0` after.
- *Red:* the same against a **compressed chunk** (`compress_chunk` on a seeded row older than 30
  days). This settles the unverified DML-on-compressed-chunk question; if it fails for a reason other
  than the missing step, the lane parks and reports (it does not decompress chunks on its own
  authority).
- *Green, characterisation:* a post-377 row (redacted at capture, `DetailChanged: true`) and a row
  of an unrelated kind are byte-identical in `payload::text`, `payload_size_bytes` and
  `schema_version` (still `1`) before and after an applied run.
- *Idempotence:* a property test over the existing URL adversarial corpus
  (`CameraUrlZoneDelimiterFuzzAdversarialTests` inputs, spec 379 non-ASCII cases, the
  `<unparseable url>` placeholder) asserting `ToOrigin(ToOrigin(x)) == ToOrigin(x)`. If any input
  breaks it, the lane stops: the ruling's "re-applying is a no-op" would be false.

**3. Re-run safety proof** (an integration test, not an argument): with the flag set to apply, run
the migrator twice over the seeded rows and assert the second run logs zero rows rewritten and leaves `payload`,
`payload_size_bytes` and `schema_version` unchanged. Then insert one more unredacted row (the
rolling-deploy replica) and run a third time: exactly that row is rewritten.

**4. Who may run it, and the flag.** Nobody runs it by hand. It runs as part of the
MigrationRunner, with the MigrationRunner's database credentials, on every deploy; a rolling-deploy
residue is therefore cleaned at the next deploy, not instantly. The lane does not add a manual
command or an operator-facing endpoint. The one human action is the flag (Decision point 9).

The MigrationRunner has no settings of its own today (its `appsettings.json` holds logging levels
only), so the flag follows the pattern the audit context already uses for its retention and archive
behaviour, end to end:

- **Options class.** `AuditUrlRedactionOptions` in `AuditObservability.Infrastructure`, shaped like
  `AuditRetentionOptions` / `AuditArchiveOptions`: a `SectionName` constant
  `"AuditObservability:UrlRedaction"` and one property, `bool Apply`, defaulting to `false` (dry
  run). Absent configuration means dry run; there is no way to apply by omission.
- **Binding.** `builder.Services.AddOptions<AuditUrlRedactionOptions>().Bind(builder.Configuration.GetSection(AuditUrlRedactionOptions.SectionName))`
  in `AddAuditObservabilityPersistence`, beside the `AuditObservabilityMigrator` registration it
  serves, which is the module the MigrationRunner calls. The step reads `IOptions<>`.
- **Composition root.** In `AppHost.cs`, a non-secret
  `builder.AddOverridableParameter("AuditUrlRedactionApply", "false")` passed to the `migrations`
  resource as `.WithEnvironment("AuditObservability__UrlRedaction__Apply", auditUrlRedactionApply)`,
  the same `Section__Key` form as `EventIngestion__IngestRetry__MaximumRetryWindow` and
  `AuditArchive__ContainerName`. The human flips it as any overridable parameter is set
  (`Parameters:AuditUrlRedactionApply=true`, a `Parameters__AuditUrlRedactionApply` environment
  variable, or user secrets; spec 188). The comment in `OverridableParameters.cs` counts the
  non-trapped parameters as a sum and must be updated with it.
- **The flag stays set.** After the first applied run it is left at `true`, so every later run in
  the 90-day window cleans rolling-deploy residue. Setting it back to `false` returns the step to
  counting.

**5. Rollback.** The data change cannot be rolled back, by design: the step keeps no copy of the
original values, and the applied run is irreversible (Decision point 9). The dry run is the only
point at which the decision can still be revisited. Rolling back the *code* removes the step; already redacted rows stay redacted, and a
reinstated old build would at worst write new unredacted rows that the step cleans when it returns.
There is no `Down`.

**6. Documentation, same PR.** In `docs/runbooks/audit-observability.md`:

- a retention note under *Where data lives*: chunks archived before the first run of the step, and
  dead-lettered `CameraRegisteredV1` / `CameraAddressChangedV1` bodies, still hold full camera URLs,
  are not rewritten (ADR-0184), and are kept until a retention rule removes them (none exists for
  either today; issue 3092);
- the first-run procedure: deploy, read the dry-run line, set `AuditUrlRedactionApply` to `true`,
  redeploy; that the applied run is irreversible; and what `schema_version = 2` means (Decision
  point 6);
- one runbook line: on finding a dead-lettered message of either type, resolve its cause and purge it
  rather than leaving it parked, because its body holds the full address;
- the ADR-0101 amendment (Decision point 5) applied to ADR-0101's Context as a dated note.

**7. The removal follow-up is a separate issue.** When the step's PR merges, a follow-up issue is
filed, by the lane as part of that delivery or by the owner, titled for removing the step and its
flag, carrying Decision point 7's two conditions as its acceptance criteria and **without**
`agent:ready`: its first condition is a date the lane cannot observe and its second a production log
line, so a human confirms both and then labels it. The removal deletes the step, the options class,
the binding, the AppHost parameter and the tests of steps 2 and 3; it keeps `schema_version = 2`
rows, the runbook's definition of `2` and this ADR.
