# Runbook: AuditObservability

Spec 009 introduces the **AuditObservability** bounded context:
a bus-fed audit trail of every `*V1` integration event with hot
search in a TimescaleDB hypertable and a daily MinIO cold
archive past the 90-day retention boundary. This runbook is the
operational reference for the on-call rotation.

## Where data lives

- **Hot tier (last 90 days)**: PostgreSQL database
  `audit-db` on the shared `postgres` server (Aspire resource
  name `postgres`, image `timescale/timescaledb-ha:pg17-oss`).
  Single hypertable `audit_events` partitioned on
  `occurred_at` with a 1-month chunk interval. Native
  TimescaleDB column compression kicks in for chunks older
  than 30 days.
- **Cold tier (older than 90 days)**: MinIO bucket
  `audit-archive` (Aspire resource name `minio`). One
  gzipped-NDJSON object per Timescale chunk at
  `s3://audit-archive/fab=<fabId-or-_unscoped>/year=YYYY/month=MM/chunk-<chunkId>.ndjson.gz`.

## Inspect hot tier

Open a `psql` against the `audit-db` connection string from
the Aspire dashboard and:

```sql
-- Chunks in the hypertable, oldest first.
SELECT chunk_name, range_start, range_end,
       pg_size_pretty(total_bytes) AS size,
       is_compressed
FROM timescaledb_information.chunks
WHERE hypertable_name = 'audit_events'
ORDER BY range_start ASC;

-- Storage breakdown.
SELECT hypertable_size('audit_events');

-- Most-recent rows.
SELECT occurred_at, event_kind, fab_id, actor_username
FROM audit_events
ORDER BY occurred_at DESC
LIMIT 20;
```

## Trigger the retention worker manually

The `AuditRetentionHostedService` runs once on startup + then
on a 24 h timer (`AuditObservability:Retention:TickInterval`).
To force a sweep without restarting the process:

1. Open the Aspire dashboard → `audit-observability` resource
   → `Logs`.
2. Restart the resource (`Stop` → `Start`). The hosted service
   re-runs `RunOnceAsync` on every boot.
3. Tail the logs for `Retention sweep at ...` lines — one per
   tick — and `Archived chunk <id> (<n> rows) to <key>` per
   chunk processed.

For a unit-style trigger from code (debugging only), the
`AuditRetentionHostedService.RunOnceAsync(CancellationToken)`
method is `public` and safe to call from a hook.

## Read an archived NDJSON object

```bash
# Configure mc against the dev MinIO (Aspire dashboard → minio).
mc alias set audit http://localhost:<port> minioadmin minioadmin

# List archived objects.
mc ls --recursive audit/audit-archive/

# Pull one chunk down.
mc cp audit/audit-archive/fab=munich/year=2026/month=02/chunk-<id>.ndjson.gz .

# Each line is one verbatim V1 payload + the indexed metadata.
zcat chunk-<id>.ndjson.gz | jq .
```

## Common failure modes

### Retention sweep fails on a single chunk

The worker logs `Failed to archive chunk <id>; leaving it in
place for the next sweep.` and continues with the next chunk
(NFR-004 accepts ≤ 5 min of audit lag during outages). Drill
in via the logs to see whether MinIO is unreachable, the
`drop_chunks` call rolled back, or the upload's ETag didn't
match the local MD5. Once the underlying issue is resolved,
the next nightly sweep retries the same chunk; archiver is
idempotent (existing-object ETag match short-circuits).

### `event_identifier` collisions

Every `*V1` carries a globally-unique identifier; the audit
unique index absorbs replays silently
(`INSERT ... ON CONFLICT DO NOTHING`). If you see a sudden
drop in audit volume, check Wolverine for a poison-letter
loop instead — the table will simply have one row per unique
event, not two.

### Hypertable grows past expected size

The retention worker hasn't been running. Confirm via:

```sql
SELECT MAX(occurred_at) FROM audit_events;
SELECT MIN(occurred_at) FROM audit_events;
```

If `MIN(occurred_at)` is significantly older than 90 days,
the worker has been silent or failing. Pull
`audit-observability` logs in the Aspire dashboard and look
for the `Retention sweep at ...` cadence.

## Performance targets

- **Ingest latency**: ≤ 50 ms p99 from RabbitMQ deliver-ack to
  row committed under sustained 100 ev/s
  (spec 009 NFR-001).
- **Search latency**: `GET /audit?since=<24h>` returns the
  first 50 rows in ≤ 200 ms p99 on warm hot tier (NFR-002).
- **Per-resource timeline**: ≤ 100 ms p99 (NFR-002).
- **Audit lag during subscriber downtime**: ≤ 5 min before the
  oldest in-flight bus message blocks (NFR-004; depends on
  RabbitMQ persistence retention).
