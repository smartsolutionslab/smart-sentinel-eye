# Contract: Cameras API after fab scoping

**Feature**: `015-camera-fab-scoping` | **Date**: 2026-08-10

Five endpoints. All gain a fab; none changes shape otherwise. The context has no
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

### `GET /cameras/{name}` — read one

- Resolved within the caller's fabs.
- Another fab's camera → **404**, indistinguishable from a name that does not
  exist (FR-006). Compared field by field in the test, not by status alone.
- Held in two of the caller's own fabs and no `?fabId=` → **400**
  `CAMERA_FAB_AMBIGUOUS`, naming the candidates (FR-010). They are all fabs the
  caller holds, so naming them leaks nothing.

### `PUT /cameras/{name}` — edit

- Resolves fab, then looks the camera up **within that fab**.
- Unknown name *or* another fab's → **404**, identical either way.
- Any existing precondition behaviour is unchanged and evaluated **after** the
  fab check — the reverse order answers a precondition failure to a request that
  was never the caller's to make.

### `POST /cameras/{name}/decommission` — retire

- Same resolution and same 404 semantics as edit.

## Response shapes

| Status | Title | When |
|---|---|---|
| 400 | `CAMERA_FAB_REQUIRED` | multi-fab caller omitted `fabId` on a write |
| 400 | `CAMERA_FAB_AMBIGUOUS` | name resolves in more than one of the caller's fabs |
| 403 | `RESOURCE_FAB_NOT_AUTHORIZED` | fab named that the caller lacks, or caller holds none |
| 404 | `CAMERA_NOT_FOUND` | unknown name, **or** a camera in a fab the caller lacks |
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
