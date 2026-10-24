# Plan 449: Stored camera URLs in audit rows are redacted in place

[spec](spec.md) · Issue #2990 · ADR-0184 (authority), ADR-0101 as amended, ADR-0067, ADR-0050, ADR-0103, ADR-0139

## Constitution / ADR check

| Gate | Result |
|---|---|
| Decision exists | ADR-0184 Accepted 2026-10-24; this plan implements its Implementation Notes 1-7 verbatim in shape. No new ADR. |
| Append-only (ADR-0101) | Crossed exactly as ADR-0184 Decision 5 authorises: three values, `payload_size_bytes`, `schema_version`. |
| Boundaries | AuditObservability only, plus `Shared.Kernel.UrlRedaction` (already referenced). No cross-context reference. |
| §II primitives | Nothing added to a Domain assembly. Application/Infrastructure types carry primitives as SQL projections only. |
| Composition root | One overridable parameter in `AppHost`, one environment variable on `migrations`. |
| Retry/idempotency | Not an HTTP path; the guarded `UPDATE` is the idempotence mechanism. |
| Latency §IV | N/A (MigrationRunner, before services start). |

## Bounded context and layers

**AuditObservability.** No Domain change (`AuditEvent` stays insert-only; `StoredPayload`'s remark already says repair
"is a migration").

- **Application** — `src/AuditObservability/Application/Redaction/StoredCameraUrlRewrite.cs` (new, pure, static):
  - `IReadOnlyCollection<string> EventKinds` and per-kind JSON keys, **derived from `AuditPayloadRedaction.UrlFields`**
    (`type.Name` → field names).
  - `Option<StoredCameraUrlRewrite.Plan> PlanFor(string eventKind, IReadOnlyDictionary<string, string?> storedValues)`:
    for each non-null value `v`, `o = UrlRedaction.ToOrigin(v)`; keys with `o != v` (ordinal) become `Change(Key, Old, New)`;
    none → `None`. `Plan.SizeDelta` = Σ (`Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(old))` −
    same for `new`). Throws on an unknown kind (programming error, ADR-0067 fail-the-run).
  - `Plan` / `Change` are records; a `Plan` never contains a key whose value was null or already an origin.
- **Infrastructure** — new folder `src/AuditObservability/Infrastructure/Redaction/`:
  - `AuditUrlRedactionOptions` — `SectionName = "AuditObservability:UrlRedaction"`, `bool Apply { get; set; }` (default
    `false`), shaped like `AuditArchiveOptions`.
  - `AuditUrlRedactionStep(IDbContextFactory<AuditObservabilityDbContext>, IOptions<AuditUrlRedactionOptions>,
    ILogger<AuditUrlRedactionStep>)`, `Task RunAsync(CancellationToken)`:
    1. `SELECT audit_id, occurred_at, event_identifier, event_kind, payload->>'Url', payload->>'PreviousUrl'
       FROM audit_events WHERE event_kind = ANY(@kinds) AND schema_version = 1` (keys/kinds from the Application
       register; key names are code constants, never user input, so they may be formatted into the `jsonb_set`
       path / `->>` operand; values are always parameters).
    2. `PlanFor` per row → unredacted set; compute per-kind counts, oldest/newest `occurred_at`.
    3. Dry: log `UrlRedactionDryRun(examined, registeredCount, addressChangedCount, oldest, newest)`; return.
    4. Applied: per plan, one `ExecuteSqlInterpolatedAsync` (autocommit, no wrapping transaction):
       `UPDATE audit_events SET payload = jsonb_set(jsonb_set(payload,'{Url}',to_jsonb(@newUrl::text)), ...),
        payload_size_bytes = payload_size_bytes - @delta, schema_version = 2
        WHERE audit_id = @id AND occurred_at = @at AND schema_version = 1 AND payload->>'Url' = @oldUrl [AND ...]`.
       Count rows affected = 1 as rewritten (0 = a concurrent run won; not an error). Recount via step 1-2.
       Log `UrlRedactionApplied(examined, countBefore, rewritten, countAfter, rewrittenEventIdentifiers)`.
  - `Log.cs`: two `[LoggerMessage]` entries (Information). Message text says no row changed / applying is
    irreversible / set `AuditUrlRedactionApply` for the dry run. **Parameters are counts, instants and Guids only.**
  - `AuditObservabilityMigrator` gains the step as a constructor dependency and calls it after `MigrationsApplied`.
  - `AuditObservabilityPersistenceModule.AddAuditObservabilityPersistence`: `AddOptions<AuditUrlRedactionOptions>()
    .Bind(builder.Configuration.GetSection(...))` and `AddSingleton<AuditUrlRedactionStep>()` beside the migrator, as
    one chain.
- **AppHost** — `var auditUrlRedactionApply = builder.AddOverridableParameter("AuditUrlRedactionApply", "false");`
  and `.WithEnvironment("AuditObservability__UrlRedaction__Apply", auditUrlRedactionApply)` on `migrations`;
  `OverridableParameters.cs` comment: "The other eight parameters (`PostgresUser`, `AuditUrlRedactionApply`,
  `MediaMtxApiSecret` and the five `*ClientSecret` values ...)". Engineer checks `AppHostParameterOverrideTests` /
  `StackStatusReport` for any parameter enumeration that must learn the new non-secret name.
- **Docs** — `docs/runbooks/audit-observability.md` per spec FR-009.

## Invariants

- I1 Only rows at `schema_version = 1` of the two kinds are read; only the step writes `2`; only on a row it rewrites.
- I2 Only registered keys whose value ≠ its origin change; `DetailChanged` presence/absence and every other member
  untouched; JSON `null` stays `null`.
- I3 `payload_size_bytes` stays on the handler's serialisation basis (escaping included: `<unparseable url>`
  serialises as `<unparseable url>`).
- I4 Dry run issues no DML. Absent configuration = dry run.
- I5 No URL leaves the process in logs, exceptions, metrics or traces.

## Messaging

None. No domain event, no integration event, no audit row about the rewrite (ADR-0184 asks for a log line; the
`schema_version = 2` stamp is the in-row provenance mark). Recorded so a reviewer does not ask for one.

## Phase 4a — tests the test-writer writes (red unless marked)

Docker-free first; integration tests are CI-only (Aspire fixture, `[Collection(AspireCollection.Name)]`).

- **U1** (`tests/AuditObservability.Application.Tests/Redaction/StoredCameraUrlRewriteTests.cs`, red: type absent →
  compile failure quoted): full URL with `?token=SECRET`, path secret, userinfo → plan with origin; already-origin →
  `None`; placeholder `<unparseable url>` → `None`; null value → key absent from plan; one of two keys changed → plan
  has one change; `SizeDelta` equals the difference of real `AuditPayloadRedaction.Serialize`d payloads for a seeded
  full vs redacted message (oracle is the handler, not a re-derivation); unknown kind throws; `EventKinds`/keys equal
  `AuditPayloadRedaction.UrlFields` (both directions).
- **P1** (`tests/Architecture.Tests/StoredUrlRedactionIdempotenceTests.cs`, **expected green** — a property over existing
  code; quote green; if red, **STOP**, ADR-0184 Note 2): `ToOrigin(ToOrigin(x)) == ToOrigin(x)` over the
  `CameraUrlZoneDelimiterFuzzAdversarialTests` inputs, the spec-379 non-ASCII cases, the userinfo/bracket-tail corpora
  and the placeholder. Reuse the existing `TheoryData` members rather than copying literals.
- **U2** (`tests/Architecture.Tests/Persistence/AuditUrlRedactionStepTests.cs`, red, Docker-free via the
  interceptor pattern of `AuditEventInsertCharacterisationTests` + a hand-written capturing `ILogger`): with
  `Apply = false`, no `UPDATE` command is issued; log line carries counts and no seeded marker. With `Apply = true`,
  the `UPDATE` text carries the four guards and `schema_version = 2`, and no URL appears in the command text (only in
  parameters). Options default: `new AuditUrlRedactionOptions().Apply` is `false`; binding from the section name.
- **R1** (`tests/Integration.Tests/AuditObservability/StoredCameraUrlRedactionIntegrationTests.cs`, red): seed by
  direct `INSERT` (pattern: `DropChunkIdentityIntegrationTests.SeedBackdatedRowAsync`) with markers `SECRET-<guid>` in
  query, path and userinfo of `Url`/`PreviousUrl`; build the migrator with `ConnectionStringDbContextFactory`,
  `Options.Create(new AuditUrlRedactionOptions { Apply = true })` and a capturing logger; assert Scenario 2 per row,
  re-reading from the database.
- **R2** dry run: Scenario 1; then a second applied run rewrites the same rows (dry run consumed nothing).
- **R3** the count: first an applied run to clear any residue in the shared database, then seed one unredacted, one
  post-377, one at `2`; applied run logs `1` before and `0` after.
- **R4** compressed chunk: seed at `now - 45 days`, `SELECT compress_chunk(c) FROM show_chunks('audit_events', older_than => now() - interval '40 days', newer_than => ...)` scoped to the seeded chunk; assert compressed (premise); applied run rewrites it. **A failure other than "step absent" → park and report.**
- **R5** re-run safety (Note 3): applied ×2 → second logs 0 rewritten, rows unchanged; insert one more, third run
  rewrites exactly it.
- **C1** (characterisation, **green**): post-377 row with `DetailChanged: true` and an unrelated-kind row are
  byte-identical (`payload::text`, `payload_size_bytes`, `schema_version` = 1) across an applied run. Captured green on
  unchanged code using a no-op where the step will be invoked (the migrator run), then unmodified after.
- Every integration test re-reads fresh from the database; no assertion checks its own input. New class → entry in
  one `tests/Integration.Tests/ci-shards/shard-N.filter` (shard-4 holds the audit retention tests).

## Counterfactuals the engineer runs (each quoted, each reverted)

1. Drop the `schema_version = 1` filter → R3/R5 red. 2. Recompute size from `payload::text` → R1 red.
3. Call `AuditPayloadRedaction.Serialize` instead → C1 red (`DetailChanged` flips). 4. Default `Apply = true` → R2/U2 red.
5. Log the old value → U2/R2 marker assertion red.

## Risks

- DML on compressed chunks (R4) — park on unexpected failure.
- Shared-database interference: R3 clears residue first; all others assert on their own seeded `audit_id`s.
- The in-fixture MigrationRunner already ran dry at boot; tests drive the migrator in-process, as
  `DropChunkIdentityIntegrationTests` drives the inventory.
