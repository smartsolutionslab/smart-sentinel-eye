# Contract: Streams API after fab scoping

**Feature**: `016-stream-fab-scoping` | **Date**: 2026-08-10

**Three endpoints exist. Two gain a fab; the third deliberately does not.**
Verified against `src/StreamDistribution/Api/` before this document was written
— the omission of that step in spec 015 cost three requirements.

## What is *not* here, and why

**No fab resolution table.** Specs 013, 014 and 015 each open with the ADR-0114
decision table — inferred, named, refused, ambiguous. None of it applies:
**there is no operator-driven write in this context.** A stream is provisioned
by an integration-event handler reacting to `CameraRegisteredV1`, never by a
request.

So there is no `?fabId=` on any write, no `STREAM_FAB_REQUIRED`, and no
`STREAM_FAB_AMBIGUOUS`. Their absence is a decision
([research.md](./research.md) §4), not an oversight.

Reads still resolve the caller's fabs, because a listing does have a caller.

## Endpoints

### `GET /streams` — list

- Returns only streams in fabs the caller holds (FR-005).
- With `?fabId=`, narrowed to that one after the guard.
- Without it, spans **all** of theirs — a read does not have to choose.
- A stream whose fab is not yet attributed is returned to **nobody** (FR-009).
  This falls out of the query rather than being special-cased: the filter is
  `fab IN (caller's fabs)`, and NULL satisfies no `IN`.

### `GET /streams/{cameraIdentifier}` — read one

- Resolved within the caller's fabs.
- A stream in a fab the caller lacks → **404**, byte-identical to a camera that
  has no stream at all (FR-006). Compared field by field in the test, not by
  status alone.
- A stream not yet attributed → the same 404. It is not distinguishable from
  "no stream", which is correct: the caller is not entitled to know it exists.
- **No ambiguity case.** The route key is a camera identifier, which is globally
  unique, so a name cannot resolve in two fabs. This is the row every sibling
  contract has and this one cannot.

### `POST /streams/authorize` — MediaMTX callback

**Not fab-scoped, deliberately.**

The caller is the media server, not an operator. It authenticates as itself and
holds no fab groups, so there is no caller fab to resolve. Scoping it would mean
inventing a per-fab identity for MediaMTX — a concept that does not exist and
would need its own decision about how many instances run per fab.

Recorded here rather than left as a silently untouched third endpoint. It is
also the only latency-sensitive route in this context (it gates playback), and
this feature does not touch it at all.

## Response shapes

| Status | When |
|---|---|
| 200 | The caller holds the stream's fab |
| 403 | `RESOURCE_FAB_NOT_AUTHORIZED` — `?fabId=` names a fab the caller lacks, or the caller holds none (FR-007) |
| 404 | No stream for that camera, **or** a stream in a fab the caller lacks, **or** a stream not yet attributed — identical in all three cases (FR-006, FR-009) |

The three-way 404 is the point. A stream record carries the MediaMTX path its
video is served on, so a distinguishable response would let an operator confirm
another plant's camera exists and is streaming — the first step to watching it.

Both scoped endpoints must **declare** 403 in their OpenAPI metadata, which
became reachable with this feature. Spec 013 shipped that wrong on one endpoint
and it took a review to catch.

## Read model

The stream DTO gains `fab`. Unlike cameras and variables this is not for
telling two same-named rows apart — a stream is keyed by camera and cannot
collide. It is so an operator holding several fabs can see *which plant* a
stream belongs to without cross-referencing the camera catalogue.
