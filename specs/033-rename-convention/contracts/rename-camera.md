# Contract: Renaming a camera, and the convention behind it

**Feature**: `033-rename-convention` · 2026-08-24

Two contracts. The first is a rule the whole product is held to; the second is
one endpoint that follows from it.

---

## The convention

> **A name may be changed only where the aggregate is not addressed by it.**

Recorded as ADR-0120 and **enforced by a build check**, not by documentation.

### How it is checked

A context that binds a route parameter with **no type constraint** is addressed
by that value, and must not also offer a rename of it.

| Constrained — identifier-addressed | Unconstrained — addressed by the value |
|---|---|
| `{camera:guid}`, `{cameraIdentifier:guid}` | `{name}` — Automation, SystemVariables |
| `{layoutIdentifier:guid}`, `{overlayIdentifier:guid}` | `{integrationName}` — EventIngestion |
| `{auditIdentifier:guid}`, `{eventId:guid}` | `{clientId}` — Identity |
| `{revisionNumber:int}` | `{resourceIdentifier}`, `{resourceKind}` — AuditObservability lookup, not an aggregate address |

The check is deliberately **not** a list of today's aggregates. It fails for a
future context inventing `{slug}` or `{code}` and adding a rename — which is the
property that makes it worth having rather than a comment.

### The rulings that follow

| Aggregate | Addressed by | Renameable | Why |
|---|---|---|---|
| **Camera** | identifier | **yes** — built here | Layouts reference it by `CameraIdentifier`; nothing dangles |
| **Layout** | identifier | yes in principle, not built | — |
| **Overlay** | identifier | yes in principle, not built | — |
| **Rule** | **name** | **no** | The name is the address; a rename breaks every saved reference to it |
| **Variable** | **name** | **no, most strongly** | Also referenced **by name** from Automation, across a boundary ADR-0016 forbids a project reference across — so a rename breaks rules with **nothing able to detect it** |

`Variable` is the case worth stating separately. `Rule`'s exclusion costs a
bookmark. `Variable`'s costs automation that silently stops firing, with no
error raised anywhere in the system.

---

## `PATCH /cameras/{camera}` — the name

Extends the endpoint spec 029 built for the address. Same address, same
precondition discipline.

**Required scope**: `sse.cameras.write`
**Required header**: `If-Match` with the version from the read.

```json
{ "name": "line-4-inlet" }
```

A rename **is** version-checked, unlike retire — it changes an attribute other
writers may be looking at, which is exactly what ADR-0113's first layer is for.

### Responses

| Status | Title | When |
|---|---|---|
| **204** | — | Renamed, or already had that name. The new version is on the `ETag` |
| **400** | `CAMERA_INVALID_REQUEST` | Malformed identifier, or a name that is not usable |
| **400** | `CAMERA_FAB_REQUIRED` | The caller holds no usable fab |
| **403** | `RESOURCE_FAB_NOT_AUTHORIZED` | The caller named a fab they do not hold |
| **404** | `CAMERA_NOT_FOUND` | No such camera **in the caller's fabs** — including when it exists in another |
| **409** | `CAMERA_RETIRED` | The camera is retired. Terminal |
| **409** | `CAMERA_NAME_TAKEN` | Another **active** camera in this fab holds that name |
| **412** | `CAMERA_VERSION_STALE` | `If-Match` quoted a version that is no longer current |
| **428** | `IF_MATCH_REQUIRED` | No `If-Match` |

### The two conflicts must not be confusable

`CAMERA_NAME_TAKEN` and `CAMERA_VERSION_STALE` are the first pair of failures on
this camera that a caller must tell apart to know what to do:

| | `CAMERA_VERSION_STALE` | `CAMERA_NAME_TAKEN` |
|---|---|---|
| Meaning | someone changed this camera | someone holds that name |
| Re-read and retry? | **yes** | no — the version is fine |
| Ever succeeds unchanged? | after re-reading, yes | **not until the name is released** |

Per ADR-0119 the **code** is what a caller keys on and the status is not
authoritative. `CAMERA_NAME_TAKEN` **must not end `_STALE`** — spec 031's
architecture test enforces that much. It does not enforce that the two carry
different statuses, so the distinction is asserted directly.

### Ordering is part of the contract

**Fab → camera (within fab) → `If-Match` → name validity → terminal state →
uniqueness.**

Inherited from spec 029, and for the same reason: answering `428` or
`409 CAMERA_NAME_TAKEN` for a camera in another fab **confirms that camera
exists**, which is the enumeration FR-006 there exists to prevent. Uniqueness is
checked last because it is the only step that reads other rows.

---

## Announced to other contexts

`CameraRenamedV1` on the integration bus, mirroring `CameraAddressChangedV1`:

```
Camera        Guid
Fab           string
PreviousName  string
Name          string
RenamedAt     DateTimeOffset
RenamedBy     Guid
Metadata      EventMetadata
```

**Consumers**: `AuditObservability` records it (FR-012). Nothing else — no other
context persists a camera's name, so nothing goes stale.

`PreviousName` is on the event because the audit entry is worth nothing without
it: *"renamed to line-4-inlet"* does not say what was corrected.

Rides the outbox, so it survives a crash between commit and send.

### History is not rewritten

`CameraRegisteredV1` and `CameraRetiredV1` carry the name **as it was at that
moment** and are left alone (FR-013). The audit trail records what was true when,
not what is true now.

---

## Not in this contract

- **No rename for rules or variables.** The ADR rules them out; nothing here
  changes that.
- **No fab change.** Forbidden, not deferred (spec 015 FR-004, spec 029 FR-008).
- **No atomic swap** of two cameras' names. Each rename stands alone, so a swap
  needs a third name in between.
- **No name history on the camera.** The audit trail is the history.
- **No UI.** Whether an operator can reach this from the app is a separate
  question.
