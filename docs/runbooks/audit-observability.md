# Runbook: AuditObservability

Spec 009 introduces the **AuditObservability** bounded context:
a bus-fed audit trail of every `*V1` integration event with hot
search in a TimescaleDB hypertable and a daily Azure Blob
Storage cold archive past the 90-day retention boundary (ADR-0155
replaced the original MinIO archiver after its registry access
broke — #2265, #2266, #2267). This runbook is the operational
reference for the on-call rotation.

## Where data lives

- **Hot tier (last 90 days)**: PostgreSQL database
  `audit-db` on the shared `postgres` server (Aspire resource
  name `postgres`, image `timescale/timescaledb-ha:pg17-oss`).
  Single hypertable `audit_events` partitioned on
  `occurred_at` with a 1-month chunk interval. Native
  TimescaleDB column compression kicks in for chunks older
  than 30 days.
- **Cold tier (older than 90 days)**: Azure Blob Storage container
  `audit-archive` (Aspire resource name `storage`; Azurite emulator
  in dev/CI). One gzipped-NDJSON blob per Timescale chunk at
  `fab=<fabId-or-_unscoped>/year=YYYY/month=MM/chunk-<chunkId>.ndjson.gz`.
- **Retention note on camera URLs (ADR-0184)**: chunks archived before the
  first applied run of the URL redaction step (the dry run rewrites nothing), and dead-lettered
  `CameraRegisteredV1` / `CameraAddressChangedV1` bodies, still hold full
  camera URLs. The step does not rewrite them, and archived chunks are kept until a
  retention rule removes them (none exists today; issue #3092), and dead letters
  for 14 days (see "Dead letters and the outbox backlog").
  The outgoing table of `wolverine_camera_catalog` also holds such a body
  until it is delivered (ADR-0185); it holds nothing the `cameras` row does
  not, and the step does not touch it.

## Redact stored camera URLs (ADR-0184)

The MigrationRunner runs the URL redaction step after the EF migrations on
every start. It reduces the camera URLs that audit rows stored before spec 377
to their origin, in place.

First-run procedure:

1. Deploy. The step runs as a dry run: it changes nothing and logs one line
   with `Examined`, `RegisteredCount`, `AddressChangedCount` and the oldest and
   newest `occurred_at`. Read it in the `migrations` logs. No URL is ever
   logged.
2. Set the AppHost parameter `AuditUrlRedactionApply` to `true` and redeploy.
   Do this promptly after reading the dry-run count: chunks archived between
   the dry-run deploy and the applied deploy keep their full URLs. The value
   must be exactly `true` or `false`. Anything else fails to bind, which fails
   the `migrations` job and, through `GateOnMigrations`, every service that
   waits on it; it is not treated as a dry run.
3. The applied run is **irreversible**: it rewrites each row on its own
   commit, so an interrupted run leaves finished rows finished and the next
   run resumes. It logs `CountBefore`, `Rewritten` and `CountAfter`;
   `CountAfter` should be `0`. The log also lists the rewritten event
   identifiers, but an OTLP attribute length limit can truncate that list:
   `Rewritten` is the reconciliation field, not the list.

`schema_version = 2` on an `audit_events` row means the step rewrote it: its
camera URLs were reduced to their origin and `payload_size_bytes` was lowered
to match. New rows keep the current schema version and are redacted at capture.

If a dead-lettered `CameraRegisteredV1` or `CameraAddressChangedV1` message
turns up, resolve its cause and purge it rather than leaving it parked: its
body holds the full address.

## Dead letters and the outbox backlog (ADR-0189)

Two things can pile up behind Wolverine, and they are treated oppositely.

**A dead letter is kept 14 days, then deleted.** A message Wolverine gave up on
is kept in two places and both age out after 14 days
(`WolverineDefaults.DeadLetterRetention`, 1,209,600,000 ms):

- **Postgres**: the `wolverine_dead_letters` table of the owning context's
  `wolverine_*` schema. The durability agent deletes rows past their `expires`
  column.
- **RabbitMQ**: the shared `wolverine-dead-letter-queue`, through the policy
  `wolverine-dead-letter-age-out` (a `message-ttl` policy, never a queue
  argument). The AppHost applies it through the management API on every boot,
  so a persistent dev container needs no `docker rm` to pick it up. A mounted
  `definitions.json` was tried and rejected: on the pinned `4-management-alpine`
  image it stops the broker creating its default user.

A dead letter is gone after 14 days whether or not anyone looked. Triage and
replay inside that window. Dead-lettered `CameraRegisteredV1` /
`CameraAddressChangedV1` bodies hold full camera URLs, so the window also
bounds how long they are retained.

**Limits of the broker TTL.**

- Dead letters that arrived **before** the policy was first applied are not
  known to be covered by the TTL. That the policy reaches messages already in
  the queue is not recorded as observed, so treat any older message as
  needing a manual purge.
- RabbitMQ applies only the single highest-priority user policy that matches a
  queue. A future policy with a higher priority matching
  `wolverine-dead-letter-queue` (a catch-all pattern, for example) overrides
  `wolverine-dead-letter-age-out`, and the TTL silently stops applying.

**What the `dead-letters` check means.** `/health` reports `dead-letters`
Degraded when `wolverine-dead-letter-queue` holds at least one message. It is
never Unhealthy (the queue is shared, so failing readiness would drain every
replica), and it reports Healthy when the broker cannot be reached. Degraded
means a message exhausted its retries and is waiting for a person; the log line
and the check's data carry the queue name and depth. The check clears when the
queue is empty.

**Inspect before it expires.**

1. Depth and age: RabbitMQ management UI, queue `wolverine-dead-letter-queue`,
   or `GET /api/queues/%2F/wolverine-dead-letter-queue`. The Postgres side
   (**UNVERIFIED**: the table and column names below have not been run against
   a booted table; check them against the schema first):
   `SELECT id, message_type, received_at, expires, exception_type, exception_message FROM wolverine_<context>.wolverine_dead_letters ORDER BY received_at;`
2. Read the exception to decide whether the cause is fixed. Do not replay into
   a handler that still fails.
3. Replay. **UNVERIFIED**: the Postgres path below has not been run against a
   booted table. In Postgres, set `replayable = true` on the rows and the
   durability agent should re-enqueue them (the `replayable` path itself is
   untested). On the broker, to read a message use the management UI's *Get
   messages* with *Ack mode: Nack message requeue true*. **Every other ack mode
   removes the message from the queue**, including *Nack message requeue
   false*, which deletes it. Republish through the owning service rather than
   moving raw messages between queues.
4. Purge what you will not replay. A body holding a camera URL is better purged
   than parked.

**An outbox backlog is drained, never expired.** A Degraded
`outbox-<prefix>` check means committed messages have not been delivered. They
are the record of a business change: fix the broker or the handler and let the
backlog drain. Nothing ages them out, and nothing here may be used to delete
them.

**One-time check on first production deploy (ADR-0189 Decision 3).** Confirm
that no archived cold-tier chunk in `audit-archive` predates the first applied
run of the URL redaction step; a chunk archived earlier still holds full camera
URLs and no sweep removes it.

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

## Read an archived NDJSON blob

The Azure CLI isn't bundled with the stack. Install it, or run it via
Docker from `mcr.microsoft.com/azure-cli`.

Azurite serves a fixed, publicly-documented well-known development
account (`devstoreaccount1`) — the same for every Azurite instance
everywhere, not a secret — so no password lookup is needed. Only the
published port (from the Aspire dashboard's `storage` resource) varies.

```bash
# Run az via Docker.
alias az='docker run --rm mcr.microsoft.com/azure-cli:latest az'

# Azurite's well-known connection string (port from the Aspire dashboard;
# host.docker.internal works on Docker Desktop, on Linux Docker add
# --add-host=host.docker.internal:host-gateway to the alias above).
CONN="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:<port>/devstoreaccount1;"

# List archived blobs.
az storage blob list --container-name audit-archive --connection-string "$CONN" --output table

# Pull one chunk down.
az storage blob download --container-name audit-archive \
  --name "fab=munich/year=2026/month=02/chunk-<id>.ndjson.gz" \
  --file chunk-<id>.ndjson.gz --connection-string "$CONN"

# Each line is one verbatim V1 payload + the indexed metadata.
zcat chunk-<id>.ndjson.gz | jq .
```

## Common failure modes

### Retention sweep fails on a single chunk

The worker logs `Failed to archive chunk <id>; leaving it in
place for the next sweep.` and continues with the next chunk
(NFR-004 accepts ≤ 5 min of audit lag during outages). Drill
in via the logs to see whether Azure Blob Storage is unreachable, the
`drop_chunks` call rolled back, or the upload's content hash didn't
match the local MD5. Once the underlying issue is resolved,
the next nightly sweep retries the same chunk; archiver is
idempotent (existing-blob content-hash match short-circuits).

Before #2425 was fixed, this retry was not actually possible:
`DropChunkAsync` called `drop_chunks` with only an upper bound
(`older_than`), so dropping any *later* chunk also dropped every
un-archived chunk older than it. A chunk left in place by a
failed archive could therefore be silently destroyed by the very
next chunk's successful drop — with nothing in the log to tell
the difference, since the logged count was always `-1` regardless
of how many chunks were actually removed. The fix bounds the drop
to the chunk's own range (`older_than` **and** `newer_than`) and
logs the dropped chunk's name, so the retry described above is
now genuinely what happens. If you are diagnosing an incident from
before this fix landed, treat "the chunk will retry" as false for
that incident — check whether an older, un-archived chunk's data
was lost when a subsequent chunk was dropped.

### `DropChunkAsync` drops more than one chunk

The worker logs `Dropping audit chunk <id> removed <N>
TimescaleDB chunks instead of exactly one` at Error. The drop has
already committed by the time this logs — there is nothing to
roll back. It means the chunk's own `[OccurredFrom, OccurredUntil)`
window stopped isolating a single chunk, which today can only
happen if `audit_events` gained a second (space) partitioning
dimension, since same-dimension chunk ranges never overlap.
Check `timescaledb_information.chunks` for `audit_events` and
compare against `information_schema` / `timescaledb_information.dimensions`
for an unexpected `add_dimension`. The named extra chunks were
dropped without being archived first — treat their data as gone
from both the hypertable and the blob archive, and restore from a
database backup if it's needed.

### `migrations` fails in AuditObservability after the migrations applied

The URL redaction step failed, or `AuditUrlRedactionApply` held a value that
is not `true` or `false` (that fails the bind). Set `AuditUrlRedactionApply`
to `false`, redeploy, and investigate. Rows already rewritten stay
rewritten; the next applied run resumes with the rest.

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
