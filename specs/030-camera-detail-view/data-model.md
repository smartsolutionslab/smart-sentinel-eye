# Data Model: Open one camera, and fix it

**Feature**: `030-camera-detail-view` · 2026-08-24

**No server-side model changes.** Spec 029 shipped everything on the wire. What
follows is the client's view of it, and the two shared types that are out of
date with what the API already returns.

---

## Camera, as the client sees it

### `CameraDetail` (new client type)

Mirrors what `GET /cameras/{camera}` returns.

| Field | Type | Note |
|---|---|---|
| `cameraIdentifier` | `string` | |
| `version` | `number` | Quoted back via `If-Match` to correct the address |
| `fab` | `string` | |
| `name` | `string` | Displayed, never editable (FR-010, #1850) |
| `rtspUrl` | `string` | The one editable field |
| `registeredAt` | `string` | ISO-8601 |
| `status` | `string` | `Registered` or `Decommissioned` — drives FR-007 |

### `CameraSummary` (existing, out of date)

Gains **`version`** and **`status`**. Spec 029 returns both on every listing row
and the TypeScript interface was never updated; it is a plain interface with no
runtime validation, so nothing broke — it is simply no longer describing the
wire.

`version` on a row is what lets a correction be made without a read-one
round-trip, which is why spec 029 put it there.

---

## Refusals, as words

The client's real model here is not a record but a mapping. Each refusal has to
become something an operator can act on (FR-006).

| Server | Status | Code | What the operator is told |
|---|---|---|---|
| Stale version | **412** | `CAMERA_VERSION_MISMATCH` | Someone else changed this; here is the current state — reload, do not retry |
| Retired | **409** | `CAMERA_RETIRED` | This camera is retired and cannot be changed |
| Not found / not yours | **404** | `CAMERA_NOT_FOUND` | No such camera. **Identical for both causes** (FR-008) |
| Bad address | **400** | `CAMERA_INVALID_REQUEST` | Should not reach the operator — caught client-side first (FR-009) |
| No `If-Match` | **428** | `IF_MATCH_REQUIRED` | **Unreachable by design** — the client always holds a version |

Two of these do not fit the existing shared helpers, which is
[research.md](./research.md) §5:

- `isStaleConflict` is **409-only**, so the 412 falls through to *"Try
  again"* — the advice `LayoutEditorDialog` documents as wrong.
- `isConflict` is **status-only**, so `CAMERA_RETIRED` matches it and would be
  described as someone else's edit.

So `problemDetail.ts` gains the 412 case and a terminal-refusal predicate. The
change is **additive**: `*_STALE`-on-409 must keep behaving exactly as it does
for layouts, overlays and system variables.

---

## Client state

None persisted. RTK Query holds server state; the `Camera` tag is already
per-identifier with a `LIST` tag, so a correction invalidates that camera and
the listing without new cache machinery.

Form state is React Hook Form's, per ADR-0079, and lives only while the dialog
is open. **A refused correction keeps what the operator typed** (FR-004 says
what is *displayed* is what is stored — that is about the camera, not the form)
so they are not made to retype it.

---

## Explicitly not modelled

- **No client-side camera cache beyond RTK Query.** Reload must re-read; that is
  what makes a pasted link work.
- **No optimistic update.** The correction's whole point is that the server may
  refuse it on a version conflict; showing it as applied and then taking it back
  is worse than waiting.
- **No name field in the edit form.** The API does not accept one (FR-010).
