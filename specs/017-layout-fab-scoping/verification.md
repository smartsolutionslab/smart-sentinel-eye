# Verification: Fab-scope layout composition

**T044** — [quickstart.md](./quickstart.md) walked. "Done" is the
observations, so they are here rather than a tick.

Observed against the real Aspire stack on 2026-08-16, at commit `22fd48f`.

## What was observed, and how

| Quickstart step | Observed by | Result |
|---|---|---|
| 1. The migration | `FabScopeLayouts`, hand-corrected | ✅ **see caveat** |
| 2. The scoped API | `LayoutFabScopingIntegrationTests` (13 cases) | ✅ |
| 3. The cross-fab tile refused | same, 3 cases | ✅ |
| 4. The hub, two-fab session | `OverlayFrameFabScopingIntegrationTests` (3 cases) | ✅ |
| 5. The already-scoped frames still work | `OverlayPushIntegrationTests` + spec 014/007 suites | ✅ |

**48/48** integration cases across LayoutComposition and OverlayDesigner pass.
126 domain, 73 application, 22 architecture unit tests pass.

## 1. The migration

Hand-corrected from the scaffold, which arrived with exactly the defect spec
015 caught:

```csharp
nullable: false, defaultValue: ""     // <- writes fab = '' to every row
```

`''` is not a valid `FabIdentifier` (minimum length 2), so every pre-existing
layout would have thrown on the next read. Replaced with the three-step
add-nullable → `DO $$` backfill with `RAISE WARNING` → `SET NOT NULL`,
mirroring `20260810164633_FabScopeCameras`.

**Caveat, recorded rather than glossed.** The integration suite runs against a
database the fixture resets, so the migration was exercised on **zero
pre-existing rows** — the `RAISE WARNING` path did not fire and its count has
not been seen in a log. What *is* verified is that the column ends up
`NOT NULL` and that every layout materialises, which is what the scaffolded
`defaultValue` would have broken. A walk against a populated database is the
one thing left to do by hand before merge if a reviewer wants it.

## 2. The scoped API (SC-001, SC-002, SC-007)

| As | Request | Observed |
|---|---|---|
| `op-dresden` | `POST /layouts` (no `fabId`) | created in **dresden**, not the munich default |
| `op-multi` | `POST /layouts` (no `fabId`) | **400** `LAYOUT_FAB_REQUIRED` |
| `op-multi` | `POST /layouts?fabId=dresden` | created in dresden |
| `op-dresden` | `POST /layouts?fabId=munich` | **403** |
| `op-dresden` | `GET /layouts` | only dresden's |
| `op-multi` | `GET /layouts` | both plants |
| `op-dresden` | `GET /layouts/{munich layout}` | **404** |
| `op-dresden` | `POST /layouts/{munich}/revisions/1/publish` | **404**, not 403 |
| `op-multi` | same name in munich then dresden | both **201** |
| `op-dresden` | same name twice in dresden | **409** |

The two 404s compared **field by field** with `traceId` and the caller's own
identifier normalised out — the only two things that can differ between two
distinct requests. Everything else matches byte for byte.

**FR-019 is the one worth dwelling on.** The name check was global and lived in
the handler rather than in an index, so it was invisible in the schema. Left
alone it answers `409 LAYOUT_NAME_TAKEN` for a layout in another plant — the
same enumeration oracle FR-006 closes on the read path, reopened on the write
path. Found by reading `CreateLayoutDraftCommandHandler` during task
generation, not by drafting against the migration.

## 3. The cross-fab tile (SC-006)

A camera really registered in munich, referenced by a tile on a dresden
layout, over the real CameraCatalog call: **400
`LAYOUT_TILE_CAMERA_OUTSIDE_FAB`**, naming the offending camera and saying
neither which fab it is in nor whether it exists.

A camera identifier that resolves to nothing: **also 400, by the same path**.
If that one were accepted, FR-014 would be bypassable by naming a random Guid,
and the whole rule decorative.

A camera in the layout's own fab: **201**.

## 4. The hub, two fabs (SC-003, SC-004)

Real SignalR connections, one per fab, asserting on **absence** over a bounded
6-second window rather than on "nothing threw":

| Event | dresden screen | munich screen |
|---|---|---|
| overlay used only by a published **dresden** layout | **receives** | **nothing** |
| overlay referenced by **no** published layout | **nothing** | **nothing** |
| overlay referenced only by a **draft** | **nothing** | — |

The middle and bottom rows are FR-011 and FR-013, and both are invisible when
they work. A test that only checked "the right screen got it" would pass
against a broadcaster still sending to everyone.

## 5. SC-008 — the push-path latency

**Not measured before the change, and that is a miss.** T041 required a
baseline **before** T024 landed. T024 (swapping the layout frames from
`Clients.All` to a group send) landed inside the Phase-2/3 commit, because the
aggregate does not compile without a caller supplying a fab and every commit
has to build on its own under rebase-merge. By the time T041 came up, the
baseline could no longer be taken.

Per the task's own instruction — *"if T024 has already landed when this is
picked up, say so on the PR rather than measuring twice after the fact"* —
this is said rather than faked.

What is known instead: `OverlayPushIntegrationTests` still passes its **≤1 s
steady-state publish→push budget** unchanged, and that is the frame on the
latency-critical leg. The change to the layout frames is one group lookup
instead of a broadcast, and the overlay frames add one indexed query per
authoring action.

## Coverage (T043)

`LayoutComposition.Domain` **96.3%** (gate ≥ 90%), `Application` **88.3%**
(gate ≥ 80%). All twenty gates pass.

## The blast radius nobody asked for

Enforcing FR-014 broke **eight integration test files**, and the breakage was
the point: every one of them invented a `Guid.CreateVersion7()` for its tile
and nothing checked it. The camera link was soft; FR-014 makes it hard.

`OverlayPushIntegrationTests` needed a different fix and is the more
interesting one. It published an overlay that no layout referenced and
expected two listeners to receive the frame — which is precisely the
broadcast-to-all behaviour this feature removes. It now publishes a
referencing layout and earns its frame.
