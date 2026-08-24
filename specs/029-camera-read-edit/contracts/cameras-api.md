# Contract: Read a single camera, and correct one

**Feature**: `029-camera-read-edit` · 2026-08-24

Completes what spec 015 withdrew and spec 028 began. Both endpoints key on the
**identifier**, for the reason spec 028's retire endpoint does: names are
reusable, so a name identifies at most one *active* camera per fab but several
over time.

---

## `GET /cameras/{camera}`

Reads one camera.

**Required scope**: `sse.cameras.read`

**Path parameter**: `camera` — the camera's identifier (Guid), as returned by
`POST /cameras` and by every row of `GET /cameras`.

### Response

`200` with the camera, and an **`ETag`** carrying its version:

```
ETag: "7"
```

```json
{
  "cameraIdentifier": "0192f3c1-...",
  "version": 7,
  "fab": "munich",
  "name": "line-3-inlet",
  "rtspUrl": "rtsp://10.0.5.12/h264",
  "registeredAt": "2026-08-24T09:12:44.918Z",
  "status": "Registered"
}
```

The version is on **both** the ETag and the body, following `RuleDto` — the
body copy is what lets `GET /cameras` hand every row a version without a
per-row fetch.

### Retired cameras are returned

`status` is `Decommissioned` and the camera is returned normally. Retirement
removes a camera from the default *listing* (spec 028 FR-007); it does not make
its record unreadable (FR-002). "Show me what is out there" and "tell me about
this camera" are different questions.

### Responses

| Status | Title | When |
|---|---|---|
| **200** | — | Found in one of the caller's fabs. |
| **400** | `CAMERA_INVALID_REQUEST` | The identifier is not a well-formed Guid. Safe to distinguish — it reveals nothing about what exists. |
| **400** | `CAMERA_FAB_REQUIRED` | The caller holds no usable fab. |
| **403** | `RESOURCE_FAB_NOT_AUTHORIZED` | The caller named a fab they do not hold. |
| **404** | `CAMERA_NOT_FOUND` | No such camera **in the caller's fabs** — including when it exists in another. |

---

## `PATCH /cameras/{camera}`

Corrects a camera's address.

**Required scope**: `sse.cameras.write`

**Required header**: `If-Match` with the version from the read.

```json
{ "rtspUrl": "rtsp://10.0.5.44/h264" }
```

`PATCH` rather than `PUT`: the request carries the editable attributes only,
and a `PUT` would imply the absent ones (name, fab, status) are being replaced
with nothing. Only `rtspUrl` is editable — renaming is out of scope (FR-012,
#1850), and fab and identifier are immutable (FR-008, FR-009).

### Responses

| Status | Title | When |
|---|---|---|
| **204** | — | Changed. The new version is on the `ETag`. |
| **400** | `CAMERA_INVALID_REQUEST` | Malformed identifier, or an address that is not a usable RTSP URL. |
| **403** | `RESOURCE_FAB_NOT_AUTHORIZED` | Caller named a fab they do not hold. |
| **404** | `CAMERA_NOT_FOUND` | No such camera in the caller's fabs — including another fab's. |
| **409** | `CAMERA_RETIRED` | The camera is retired. Terminal (FR-005). |
| **412** | `CAMERA_VERSION_STALE` | `If-Match` quoted a version that is no longer current. |
| **428** | `IF_MATCH_REQUIRED` | No `If-Match`. Not a fallback to no concurrency control (ADR-0113). |
| **400** | `IF_MATCH_MALFORMED` | `If-Match` present but not a single strong tag. |

> **Corrected 2026-08-24 (spec 031).** This table listed the 412 as carrying
> `PRECONDITION_FAILED` — a code that existed nowhere in `src/`. The
> implementation answered `CAMERA_VERSION_MISMATCH`, and a client written
> against this contract would have keyed on a string that never arrives.
>
> Nothing broke, because nothing reads this document mechanically — which is
> exactly how it drifted. Spec 031 then renamed the code to
> `CAMERA_VERSION_STALE`, so the row above is now the value the implementation
> actually returns. The status is unchanged; ADR-0119 records why it stays 412
> while six other contexts spell the same meaning as 409.

### Ordering is part of the contract

**The fab is resolved before every other precondition** — before the `If-Match`
header is looked at, before the camera is loaded, before the body is validated
(FR-007).

This is not tidiness. Answering `428 IF_MATCH_REQUIRED` for a camera in another
fab **confirms that camera exists**, which is the enumeration FR-006 exists to
prevent. The tempting implementation validates the cheap header first; it must
not. Likewise `409 CAMERA_RETIRED` is only ever returned for a camera the
caller can already see.

Order: **fab → camera (within fab) → `If-Match` → body → terminal state.**

---

## 404, not 403, for another fab's camera

FR-006, restated from spec 015 and reinstated here because a single-camera read
is what finally makes it expressible.

A `403` would confirm the camera exists, letting an operator enumerate another
plant's cameras one request at a time — and a camera record carries its RTSP
address. The refusal must be indistinguishable from one for an identifier that
was never registered: **the same status and the same body**, asserted field by
field rather than by status alone (SC-003).

This applies to **both** endpoints. The read is the obvious one; the edit is
where it will regress, because an edit has four more ways to fail and each is a
chance to answer something more specific.

---

## Effect on `GET /cameras`

Additive: every row gains **`version`**.

```json
{ "cameraIdentifier": "...", "version": 7, "fab": "munich", "...": "..." }
```

So an operator can correct an address straight from the listing without a
read-one round-trip. Existing consumers are unaffected — the management app's
`CameraSummary` is a plain TypeScript interface with no runtime validation.

---

## Announced to other contexts

`CameraAddressChangedV1` on the integration bus (per-module queues, ADR-0088):

```
Camera       Guid
Fab          string
PreviousUrl  string
Url          string
ChangedAt    DateTimeOffset
ChangedBy    Guid
Metadata     EventMetadata
```

**Consumers**: AuditObservability records it (FR-011). StreamDistribution
re-points the MediaMTX path to the new source — **research finding 2**, which
the spec does not currently require. Without it the catalogue reports the new
address while the SFU keeps streaming from the old one, indefinitely.

The MediaMTX **path name does not change**: it derives from the camera
identifier, which is immutable, so no kiosk's WHEP URL breaks. Only the source
the path pulls from moves.

The announcement rides the outbox, so it survives a crash between the commit and
the send — and the catalogue's change does not depend on the SFU being
reachable, exactly as spec 028 FR-008a has it for retirement.

---

## Not in this contract

- **No rename.** FR-012; tracked as #1850, which must first settle whether
  anything in this product is renameable.
- **No `DELETE /cameras/{camera}`.** Retirement is the terminal operation and
  it keeps the record (spec 028).
- **No bulk edit.** One camera per request.
- **No read-by-name.** Superseded with FR-010; revisit both together if a
  name-keyed lookup is ever wanted.
