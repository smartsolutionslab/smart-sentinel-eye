# Contract: Cameras API after fab scoping

**Feature**: `015-camera-fab-scoping` | **Date**: 2026-08-10

**Two endpoints exist** — `POST /cameras` and `GET /cameras`. Both gain a fab; none changes shape otherwise. The context has no
fab check at all today, so every row below is new behaviour.

## Fab resolution (applies to every endpoint)

Identical to the rules and system-variables APIs (ADR-0114). Resolved
immediately after model binding and **before** any precondition is read.

| Caller assigned to | `fabId` supplied | Outcome |
|---|---|---|
| exactly one fab | omitted | inferred from the caller (FR-007) |
| exactly one fab | that fab | accepted |
| several fabs | omitted | **400** — a fab must be chosen (FR-007) |
| several fabs | one of theirs | accepted |
| any | a fab they lack | **403** `RESOURCE_FAB_NOT_AUTHORIZED` (FR-008) |
| no fabs | anything | **403** (FR-009) |

Reuses `FabResolution` and `FabClaims` from `ServiceDefaults` **unchanged**.
This feature adds no resolution mechanism; it applies the existing one.

## Endpoints

### `POST /cameras` — register

- Optional `?fabId=` per the table. Created in the resolved fab.
- **409** `CAMERA_NAME_TAKEN` now means *taken in this fab*; the same name in
  another fab is accepted (FR-002).

### `GET /cameras` — list

- Returns only cameras in fabs the caller holds (FR-005).
- With `?fabId=`, narrowed to that one after the guard.
- Without it and the caller holds several: spans **all** of theirs. A read does
  not have to choose — the same asymmetry with `POST` the other two APIs have.

### ~~`GET /cameras/{name}`` — read one~~

**Withdrawn 2026-08-10.** This endpoint does not exist and this spec does not add one. Tracked as #1435.

### ~~`PUT /cameras/{name}`` — edit~~

**Withdrawn 2026-08-10.** This endpoint does not exist and this spec does not add one. Tracked as #1435.

### ~~`POST /cameras/{name}/decommission` — retire~~

**Withdrawn 2026-08-10.** This endpoint does not exist and this spec does not
add one — a camera cannot be retired at all today. Listed here in error when
the contract was drafted. When a retire behaviour lands it takes the same fab
resolution and the same 404 semantics as edit.

**Landed 2026-08-24 as `POST /cameras/{camera}/retire`** — spec 028, see
[its contract](../../028-retire-camera/contracts/cameras-api.md). The fab
resolution and the 404-not-403 semantics are as promised above.

**The key changed, from the name to the identifier.** Spec 028 is what makes a
name reusable, so a name identifies at most one *active* camera per fab but
several over time — a URL keyed on it would resolve to a different object
depending on when it was called, and could not address a camera already
retired, which that spec requires to succeed. `retire` rather than
`decommission` in the path: shorter, while the persisted status stays
`Decommissioned`. The status is the record; the endpoint is the verb.

`GET /cameras` also gained `includeRetired` (default `false`) and a `status` on
every row, so this spec's listing shape is a subset of what it returns now.

## Response shapes

| Status | Title | When |
|---|---|---|
| 400 | `CAMERA_FAB_REQUIRED` | multi-fab caller omitted `fabId` on a write |
| 403 | `RESOURCE_FAB_NOT_AUTHORIZED` | fab named that the caller lacks, or caller holds none |
| 409 | `CAMERA_NAME_TAKEN` | name already used **in that fab** |

The 404-for-another-fab choice is why FR-006 exists: a 403 would confirm the
camera is there, letting an operator enumerate another fab's camera names one
guess at a time — and a camera record carries its RTSP address.

Every endpoint that gains a 400 or 403 path must declare it, or the generated
OpenAPI claims a status that can happen cannot. Spec 013 shipped this wrong on
one endpoint and it took a review to catch.

## Read model

`CameraDto` gains `fab`. A multi-fab operator's listing can otherwise hold two
rows of one name with nothing to tell them apart (FR-013) — the gap #1303 was
for rules.
