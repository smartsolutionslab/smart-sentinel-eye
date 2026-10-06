# Tasks 302 — A revoked credential says so in the log

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2205
**Engineer:** `backend-engineer` (single engineer).
**Phase 4a colour:** behaviour-changing → **red first** for T001; T002 is the
characterisation baseline that must stay green **unmodified**.

**Parallelism:** none — one context, a strict chain T001 → T002 → T003 → T004.
No foundational task; no ADR-0109 contention file, so this slice may run
concurrently with any other slice.

---

## US1 — A delivery refused because of revocation is logged as such

### [T001] [US1] Red: the new test class (test-writer)

**New:** `tests/Integration.Tests/EventIngestion/RevokedWebhookDeliveryIsLoggedIntegrationTests.cs`
**Edit:** `tests/Integration.Tests/ci-shards/shard-1.filter` — append
`|FullyQualifiedName~SmartSentinelEye.Integration.Tests.EventIngestion.RevokedWebhookDeliveryIsLoggedIntegrationTests.`

The three facts in plan §5, keyed on the exact phrase
`Refused a delivery to revoked webhook integration '<name>'` (never on the
name alone — the revocation-time record names it too). Each fact asserts the
401 **before** reading logs. Use `aspire.CaptureLogs("event-ingestion")`.
Run; quote the verbatim failure. Expected: facts 1 and 2 red (phrase/sentinel
never appears); fact 3 red because it waits on fact 1's record first.

**Done when:** all three observed red for the stated reason, output captured.

### [T002] [US1] Characterisation baseline (test-writer, same pass as T001)

Run, before any `src/` change, and capture green:
`WebhookRevocationRefusesDeliveryIntegrationTests` (3 facts),
`AnonymousIngestIsRefusedTests`, `WebhookBearerValidationIntegrationTests`.
These files are **not edited** at any point in this slice.

### [T003] [US1] Implement (backend-engineer) — depends on T001, T002

1. **New** `src/EventIngestion/Api/Log.cs` with
   `RevokedWebhookIntegrationDeliveryRefused` exactly as plan §4.
2. `src/EventIngestion/Api/EventsEndpoints.Writes.cs`:
   - `IngestWebhook` (`:121`): add `[FromServices] ILoggerFactory loggerFactory`;
     pass `loggerFactory.CreateLogger(typeof(EventsEndpoints))` to
     `AuthenticateWebhookAsync`.
   - `AuthenticateWebhookAsync` (`:185`): add `ILogger logger` before the
     `CancellationToken`; split `:211` into the two branches of plan §3; log
     only on the revoked branch; both still `return null`.
   - Extend the `<summary>` at `:176-184` by one sentence (plan §3).
   - `:138-141` unchanged.

**Done when:** T001 green, T002 green with its files byte-identical
(`git diff --stat` shows no change to them), Release build clean.

### [T004] [US1] Verify (phase 5)

Boot the stack; register + revoke an integration; POST to it and to a
never-registered name; read the `event-ingestion` structured logs in the Aspire
dashboard and quote the one record that appears (and the absence for the
other). Latency: N/A (refusal path, no event produced).
