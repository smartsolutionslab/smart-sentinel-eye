# Contract: Retire a camera

**Feature**: `028-retire-camera` · 2026-08-23

Reinstates the endpoint spec 015 withdrew, with one change to its shape and the
reason for it.

---

## `POST /cameras/{camera}/retire`

Retires a camera. Terminal.

**Required scope**: `sse.cameras.write`

**Path parameter**: `camera` — the camera's identifier (Guid), as returned by
`POST /cameras`.

**Body**: none.

### Keyed by identifier, not by name — a deliberate change

Spec 015's withdrawn entry listed `POST /cameras/{name}/decommission`. This
feature keys on the **identifier** instead, because this feature is what makes a
name reusable.

Once FR-006 ships, a name identifies at most one *active* camera in a fab but
may name several over time. A URL that resolves to a different object depending
on when it is called is a poor key, and a retire endpoint keyed that way could
not address a camera that had already been retired — which FR-005 requires to
succeed.

`retire` rather than `decommission` in the path: shorter, and the domain value
stays `Decommissioned`. The status is the record; the endpoint is the verb.

### Fab resolution

Identical to every other camera write (spec 015, ADR-0114). A caller holding one
fab needs nothing; a caller holding several names the fab via `fabId`.

### Responses

| Status | Title | When |
|---|---|---|
| **204** | — | Retired. Also returned when the camera was **already** retired (FR-005) — the outcome the caller asked for is true either way. |
| **400** | `CAMERA_FAB_REQUIRED` | Multi-fab caller omitted `fabId`. |
| **403** | `RESOURCE_FAB_NOT_AUTHORIZED` | Caller named a fab it does not hold, or holds none. |
| **404** | `CAMERA_NOT_FOUND` | No such camera **in the caller's fab** — including when it exists in another fab. |

### 404, not 403, for another fab's camera

FR-004, and a security property rather than a nicety. A 403 would confirm the
camera exists, letting an operator enumerate another fab's camera names one
request at a time. The refusal must be indistinguishable from one for a name
that was never registered.

This is spec 015's established choice; it is restated here because it is the
kind of thing a later refactor "tidies" into a more informative error.

### 204, not 200

Nothing useful is returned. The camera's new state is already known to the
caller — they asked for it — and returning the aggregate would invite a client
to treat the response as a read model.

---

## Effect on `GET /cameras`

Retired cameras are **excluded by default** (FR-007).

**Query parameter**: `includeRetired` (boolean, default `false`). When true,
retired cameras appear, each carrying its status so a client can distinguish
them.

This is additive: existing callers see no change, which matters because the
listing is already consumed by the management app.

---

## Announced to other contexts

`CameraRetiredV1` on the integration bus (per-module queues, ADR-0088):

```
Camera      Guid
Fab         string
Name        string
RetiredAt   DateTimeOffset
RetiredBy   Guid
Metadata    EventMetadata
```

**Consumers**: AuditObservability records it; StreamDistribution retires the
stream and removes its SFU path.

The announcement rides the outbox, so it survives a crash between the commit and
the send — and it is what FR-008a means by the two contexts sharing an
announcement rather than a transaction.

---

## Not in this contract

- **No un-retire.** Terminal by decision.
- **No bulk retire.** One camera per request; a fab-wide sweep is a different
  feature with different blast radius.
- **No `PUT /cameras/{camera}`.** Editing remains withdrawn, tracked as #1435.
