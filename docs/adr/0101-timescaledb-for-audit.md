# ADR-0101: TimescaleDB hypertable for the AuditObservability hot tier

**Status:** Accepted
**Date:** 2026-05-29
**Supersedes:** —
**Superseded by:** —

## Context

Spec 009 introduces the **AuditObservability** bounded context: a
single subscriber to every existing `*V1` integration event,
storing one normalised row per delivery for compliance + live
forensic queries. Sustained write rate at the spec 006 target
(100 ev/s across the fab fleet) plus the read patterns from the
spec — flat search with time + actor + kind filters, plus
per-resource timelines — point at time-series workloads:

- **Writes are append-only and monotonic in `occurred_at`.** No
  updates, no deletes (the retention worker drops whole partition
  units, not individual rows).
- **Reads are time-bounded.** Every search endpoint takes
  `since` / `until` and almost always filters to a recent
  window; per-resource timelines are short (< 1 000 rows per
  aggregate lifetime) but cross-cutting searches need to scan
  months of data efficiently.
- **Retention is the gating non-functional concern.** Spec 009
  caps the hot tier at 90 days; older data goes to MinIO cold
  archive. The mechanism for ageing data out has to be cheap and
  not block writers.

Plain PostgreSQL gives us the storage but requires us to roll
our own partitioning + retention + index maintenance. Doable but
non-trivial:

- `PARTITION BY RANGE (occurred_at)` declarative partitioning
  works on PG 17 — but the partition manager (create new
  partitions ahead of time, detach + export + drop old ones) is
  bespoke code we'd write + maintain.
- Index maintenance on a partitioned multi-fab heap means
  per-partition indexes; vacuuming + bloat tracking
  multiplies.
- Compression of cold-but-still-hot partitions (say day 60–90)
  isn't built in; we'd live with full row volume right up to
  the retention boundary.

## Decision

The **AuditObservability** context's `audit_events` table is a
**TimescaleDB hypertable**, partitioned by `occurred_at` with a
**1-month chunk interval**. The Community (TSL — free for
self-host) edition is what we run, since the compression policy
below is a TSL feature; hypertables themselves are Apache-2. We
use:

- `create_hypertable()` for partition management.
- `add_retention_policy()` for the 90-day drop boundary, called
  by our own retention worker **after** the per-chunk export to
  MinIO has succeeded (so the policy itself doesn't fire; we
  invoke `drop_chunks()` directly).
- `add_compression_policy()` for in-place compression of chunks
  older than 30 days, with `compress_after => INTERVAL '30
  days'`, so days 30–90 sit in column-compressed storage but
  stay queryable from the same table without code changes.

TimescaleDB is scoped to the **AuditObservability context only**
(its own database `audit-db` on the existing Aspire `postgres`
server). Every other context stays on plain PostgreSQL.

## Consequences

**Positive:**

- Partition management is one line of SQL instead of a custom
  worker.
- Native column compression on the warm-but-not-hot window cuts
  the hypertable's disk footprint by ~5-10× without affecting
  the read API.
- `drop_chunks()` is instantaneous (catalog-level metadata
  update), avoids `DELETE` vacuum bloat, and matches the spec's
  per-chunk archive boundary exactly — each Timescale chunk
  becomes one MinIO object.
- Continuous-aggregate refreshes are available if/when a
  spec-010 dashboard needs hourly/daily roll-ups; v1 doesn't use
  them but the door is open.

**Negative:**

- One new extension on the production PostgreSQL StatefulSet
  (Helm chart's `postgres` image bumped to a Timescale-bundled
  variant). Backup/restore tooling must understand the
  Timescale-specific catalog tables, but the standard `pg_dump`
  path works since v2.x.
- The Timescale OSS edition does **not** include the Toolkit
  hyperfunctions (percentile_agg, etc.) under the Apache 2
  license. Spec 009 doesn't need them; if a future spec does,
  we either switch to the TSL (Timescale Source-Available
  License — free for self-host) variant or write the agg in
  application code.
- One more thing for ops on-call to know. The runbook (spec 010
  follow-up) gains a Timescale-specific section.

## Alternatives Considered

**Plain PostgreSQL declarative range partitioning + custom retention worker — REJECTED.**
Functional but bespoke. We'd own:
- A partition-ahead worker (must create next month's partition
  before traffic arrives).
- A retention worker that `DETACH PARTITION` + exports + `DROP`.
- A custom compression story (manual `pg_repack` runs, or
  accept 5-10× larger hot tier).
Not a hard "no" — if the constitution review rejects TimescaleDB,
this is the documented fallback. Same on-disk row shape; same
read API; only the storage engine + ops loop differ. Cost is
roughly 200 LOC of background workers + monthly partition-create
crontab equivalent.

**Single unpartitioned table + scheduled `DELETE` — REJECTED.**
The simplest possible v1, but vacuum bloat at 100 ev/s + 90-day
retention is severe; at our target storage growth (~78 GB) the
`DELETE` cycles would dominate IO. No clean per-chunk boundary
for the cold archive either.

**TimescaleDB Cloud — REJECTED.**
A SaaS offering would handle ops entirely but conflicts with the
constitution's **on-prem-first** principle (§I.3, ADR-0025: each
fab runs k3s; no cloud dependencies for the data plane).

**ClickHouse / Druid / dedicated columnar store — REJECTED.**
Better at the read workload at scale but introduces a whole new
database engine to the constitution. The audit volume comfortably
fits in a TimescaleDB hypertable; the operational simplicity of
"same Postgres server, one extension" is worth far more than
the marginal query-perf gain.

## Implementation Notes

- The `audit-db` Aspire resource enables the `timescaledb`
  extension at first migration via `CREATE EXTENSION IF NOT
  EXISTS timescaledb;`. The AppHost runs the **single-node
  community** image `timescale/timescaledb:2.27.1-pg17` (pinned).
  We deliberately avoid two other variants:
  - The `-ha` (Spilo/Patroni) image is ~1.5 GB; the single-node
    image is far lighter for dev/CI with no loss of the features
    this context uses.
  - The `…-oss` (Apache-2-only) tags **drop compression**, which
    this context's migration uses (`timescaledb.compress` +
    `add_compression_policy`). Compression is a TimescaleDB
    Community (TSL — free for self-host) feature, so the
    community image is the correct, and lightest, choice. The
    earlier "OSS Apache-2 edition is enough" framing was
    imprecise: hypertables are Apache-2, but the compression
    policy this context relies on is TSL/community.
- The retention worker calls TimescaleDB's
  `show_chunks(audit_events, older_than => INTERVAL '90 days')`
  to discover archive candidates, then per-chunk:
  1. `SELECT * FROM <chunk>` streams rows out as NDJSON.
  2. Upload to MinIO with `Content-MD5` checksum.
  3. Verify ETag round-trip.
  4. `SELECT drop_chunks('audit_events', older_than =>
     <chunkBoundary>);`
- Compression: `add_compression_policy('audit_events', INTERVAL
  '30 days')` runs in the background via Timescale's own job
  scheduler. We do not call it from application code.
- The constitution's tech-stack section (§ Backend) gains one
  line: "**TimescaleDB** (PostgreSQL extension) is permitted in
  time-series-shaped contexts; current use is
  AuditObservability per ADR-0101."
