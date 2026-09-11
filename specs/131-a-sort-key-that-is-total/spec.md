# Spec 131 — A sort key that is total

**Issue:** #2144 — *Camera paging tie-breaks on a pair that is unique only for live rows*
**Branch:** `fix/2144-a-sort-key-that-is-total`
**ADRs:** 0037 (phases), 0039/0090 (Guid v7 identifiers), 0139 (red first), 0144 (autonomous lane), 0036 (smallest change), 0043/0113 (concurrency — not this)

## The defect

`ListCamerasQueryHandler.SortBy` orders by `(Name, Fab)` or
`(Registration.At, Fab)`. Neither pair is a total order over the rows the
handler can return.

`(Name, Fab)` is unique **only for live rows**. The constraint that makes it
unique is partial, and partial on purpose:

```
builder.HasIndex(CameraFabProperty, NormalizedNameProperty)
    .HasDatabaseName("ux_cameras_fab_name_normalized_active")
    .IsUnique()
    .HasFilter("status <> 'Decommissioned'");
```

*(`src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs:132-136`;
the migration writes the same predicate — `20260823194632_CaseInsensitiveCameraNames.cs:68`.
Re-read for this spec rather than inherited from the issue: the predicate is
exactly as the issue states it.)*

`Camera.Retire` says why it is partial: *"Replacement hardware is registered
afresh and may take this camera's name, because retiring releases it within the
fab."* So a decommissioned camera and its live replacement **legitimately share
`(Name, Fab)`**, and with `includeRetired=true` both are in the result set.

The handler pages with `Skip`/`Take`. Offset paging resumes by position, and a
position is only meaningful if the order is determined. When it is not, a page
boundary landing inside a tie can hand the same row back twice and never show
the other one — SQL gives no guarantee that two separate executions break a tie
the same way.

`(Registration.At, Fab)` has the same shape and no index behind it at all: two
cameras in one fab registered in the same instant are a tie, retired or not.

## What changed since the issue was written

**#2076 has landed** (closed 2026-09-06). `CameraCatalogFabLookup` is in the
tree at `src/StreamDistribution/Infrastructure/Attribution/CameraCatalogFabLookup.cs`
and already pages the whole catalogue with `includeRetired=true`, 200 rows at a
time. The issue's framing — "#2076 *makes* the fab lookup one of those callers"
— is past tense now. The caller exists.

**Reachability is still bounded, and the bound is the only thing keeping it
latent.** It needs more than one page of rows *and* a tie straddling the
boundary. The measurement the issue cites (19 cameras, 0 ties, 2026-09-06) is
below the page size, so nothing is being dropped today. At the constitution's
250-cameras-per-fab target it is several pages per fab, and retired rows are
never deleted — the total grows with history, not with installed hardware
(`StreamFabAttributionOptions.PageSize`'s own remark says so). The window
closes on its own.

And the failure mode is the one #2076 exists to prevent: a camera the paging
drops is a camera the fab lookup cannot see, and a stream it serves stays
unattributed — silently, because the attribution pass only logs a count.

## The fix

Append `camera.Id` to every arm of `SortBy`. `CameraIdentifier` is a Guid v7
(ADR-0039, ADR-0090), unique by construction for every row whether retired or
not, and `IComparable<CameraIdentifier>` in memory / a `uuid` column in
Postgres. That makes the sort key total, so the order is determined and offset
paging has a position to resume from.

The existing `.ThenBy(camera => camera.Fab)` stays. It is not wrong, it is what
orders a multi-fab listing readably, and removing it would change visible
ordering for a reason unrelated to this defect.

## Requirements

- **FR-001** — Paging through a listing with `includeRetired=true` returns every
  matching camera **exactly once**, including when a decommissioned camera and a
  live camera share `(Name, Fab)` and the tie straddles a page boundary.
- **FR-002** — The same holds for `sort=registeredAt`, where two cameras share a
  registration instant.
- **FR-003** — The order is determined by the query alone: two executions that
  see the rows in different physical order produce the same page.
- **FR-004** — Which rows are returned does not change. `includeRetired` keeps
  its meaning exactly.

## Out of scope, deliberately

- **The partial index.** It is partial so a name can be reused after
  decommissioning. The sort key is the defect; the index is the feature.
- **The page size.** Neither `ListCamerasDefaults.MaximumLimit` nor
  `StreamFabAttributionOptions.PageSize` changes to make a test easier.
- **Keyset paging.** The issue raises it and answers itself: a larger change,
  not smuggled in behind a tie-break.
- **`ListVariablesQueryHandler`.** See below — a sibling in shape, not in
  consequence, and a second context is a second issue.

## The sibling audit the issue asked for

`grep -rn '\.Skip(' src/` returns **exactly one** hit in the whole tree:
`ListCamerasQueryHandler.cs:92`. No other handler pages, so no other handler can
duplicate or skip a row.

One handler does tie-break on the same non-unique shape:
`SystemVariables/Application/Queries/Handlers/ListVariablesQueryHandler.cs:41-44`
orders by `(Name, Fab)`, and `VariableConfiguration.cs:115-116` carries the same
partial unique index (`state <> 'Archived'`). With `includeArchived=true` an
archived variable and its live successor tie there too. **But it returns the
whole list unpaged**, so the consequence is a non-deterministic order between
two rows, not a lost one. Recorded, not fixed here.

Paged and already total: `SearchAuditQueryHandler` and
`GetResourceTimelineQueryHandler` tie-break on `auditEvent.Id`;
`ListEventsQueryHandler` on `eventEntity.Id`. They were written the way this one
now is.

## Latency

**Not on the §IV path.** None of the six legs is a catalogue query — the
event-to-overlay path does not read `GET /cameras`. The attribution pass that
does read it is a one-time startup job (ADR-0116), not a per-event leg.

## Done looks like

A decommissioned camera and its live replacement sharing `(Name, Fab)`,
straddling a page boundary, and a full `includeRetired=true` page-through
returning both, exactly once each — where today one comes back twice and the
other not at all.
