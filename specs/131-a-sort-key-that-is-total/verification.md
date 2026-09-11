# Verification — Spec 131

## The red, and what it showed

`ListCamerasPagingOrderTests`, against `7c7b6d0f~1`:

```
Failed ...Paging_a_retired_camera_and_its_live_replacement_returns_each_exactly_once
   Shouldly.ShouldAssertException : paged.Order()
    should be
[01a09008-3806-7877-81fe-401d318f2bea, 01a09008-3808-703a-b820-aad472e9cfc8, 01a09008-3809-70c2-a9c5-e9a6c10cca98]
    but was
[01a09008-3806-7877-81fe-401d318f2bea, 01a09008-3808-703a-b820-aad472e9cfc8, 01a09008-3808-703a-b820-aad472e9cfc8]
    difference
[01a09008-3806-7877-81fe-401d318f2bea, 01a09008-3808-703a-b820-aad472e9cfc8, *01a09008-3808-703a-b820-aad472e9cfc8*]
```

`…3808` is returned twice and `…3809` never — one row duplicated, one row lost,
from a two-page walk of three cameras. The `registeredAt` fact fails the same
way. **Both green after the fix, unmodified.**

## EF translation — the risk the unit test cannot cover

`OrderBy` that will not translate throws at runtime, not at build, so the new
column was put through EF's own SQL generation (`ToQueryString`, Npgsql
provider, no connection required):

```
ORDER BY c.name, c.fab, c.camera_id
LIMIT @p OFFSET @p

ORDER BY c.registered_at DESC, c.fab, c.camera_id
LIMIT @p OFFSET @p
```

Fully server-side. Nothing client-evaluates, and `camera_id` is the mapped
column, not a converted-value projection.

## Query plan and cost — reasoned from the schema, not measured

**`cameras` carries exactly one index besides the primary key**:
`ux_cameras_fab_name_normalized_active` on `(fab, name_normalized)`, partial on
`status <> 'Decommissioned'` (`CameraConfiguration.cs:132-136`).

Neither ORDER BY could ever have been served by an index:

- `ORDER BY name, fab` leads on `name`; the index leads on `fab`, and on
  `name_normalized` rather than `name`, and is partial.
- `registered_at` has no index at all.

So both queries already required a **full sort**, and adding a third sort key
cannot cost a plan that never existed. What changes is the comparator: for rows
that compare equal on the first two keys, one extra 16-byte `uuid` comparison.
On the volume the issue measured — 19 cameras, 0 ties — that is zero extra
comparisons.

**This is a structural argument from the schema, not an `EXPLAIN`.** No Aspire
stack was booted and no throwaway Postgres was pulled: C: is at 97% (8.5 GB
free) and this repo has had Docker killed by a full disk before. The confirming
measurement, if wanted, is `EXPLAIN (ANALYZE, BUFFERS)` on the two statements
above against the run-mode volume; the prediction is *Seq Scan → Sort* before
and after, with the same node shape.

## Not on the latency path

Confirmed: none of the six §IV legs is a catalogue query. The event-to-overlay
path does not read `GET /cameras`. The one caller that pages the whole
catalogue, `CameraCatalogFabLookup`, is a one-time startup pass (ADR-0116).

## Suites

| Suite | Result |
|---|---|
| `dotnet build -c Release` | **0 warnings, 0 errors** |
| `CameraCatalog.Application.Tests` | 76 passed |
| `CameraCatalog.Domain.Tests` | 80 passed |
| `Architecture.Tests` | 388 passed |
| `StreamDistribution.Infrastructure.Tests` | 34 passed |

## Left for Phase 5

An end-to-end page-through over the real database — the fixture proving the
statement above runs, not merely that it compiles.
