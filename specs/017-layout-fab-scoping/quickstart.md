# Quickstart: Fab-scope layout composition

**Feature**: `017-layout-fab-scoping`

"Done" is the observations, not the walk. Record them on the PR.

## 1. The migration, on layouts that have no fab

Unlike spec 016 there **is** a SQL backfill to watch, because layouts are in
this context's own database.

```sh
# Stack up against a database that already has layouts, then read the
# migration-runner log:
```

```
WARNING:  FabScopeLayouts attributed N pre-existing layout(s) to fab 'munich'.
          If this database belongs to another fab, those layouts are now
          invisible to every operator of it.
```

**Record N.** Then confirm the constraint actually landed — this is the check
that vindicates a hand-corrected migration:

```sql
SELECT count(*) FROM layouts WHERE fab IS NULL;              -- expect 0
\d layouts                                                   -- fab: not null
```

If the scaffolded `AddColumn(nullable: false, defaultValue: "")` shipped
instead of the three-step add/backfill/tighten, every row would carry
`fab = ''` — not a valid `FabIdentifier`, so every layout would fail to
materialise on the next read. Spec 015 hit exactly this.

## 2. The scoped API

| As | Do | Expect |
|---|---|---|
| `op-dresden@dresden.test` | `GET /layouts` | only dresden's |
| `op-dresden` | `GET /layouts/{a munich layout}` | **404** |
| `op-dresden` | `POST /layouts/{a munich layout}/revisions/1/publish` | **404**, not 403 |
| `op-dresden` | `POST /layouts` (no `fabId`) | created in **dresden**, not munich |
| `op-multi@smart-sentinel-eye.test` | `POST /layouts` (no `fabId`) | **400** `LAYOUT_FAB_REQUIRED` |
| `op-multi` | `POST /layouts?fabId=dresden` | created in dresden |
| `op-dresden` | `POST /layouts?fabId=munich` | **403** — a named fab, so the answer is about the fab |

Compare the two 404s — a munich layout, and a layout identifier that never
existed — **field by field** with `traceId` removed. A difference in `title` or
`type` lets an operator confirm another plant's layout exists.

The `op-dresden` create is the one that cannot be faked: everything else in the
system defaults to munich, so a broken inference that fell back to the default
would pass against a munich operator and only fail here.

## 3. The cross-fab tile is refused

```sh
# As op-dresden, create a layout whose tile names a MUNICH camera.
```

Expect **400**, naming the offending tile's position. Then the same with a
camera identifier that resolves to nothing at all — also **400**, by the same
path (FR-015). If the unknown camera is *accepted*, FR-014 is bypassable and
the whole rule is decorative.

Then stop `camera-catalog` and try again: layout **authoring** fails, while
`GET /layouts` and the hub keep working. That is the §III bargain being
observed rather than assumed.

## 4. The hub — a two-fab kiosk session

**This is the step that cannot be faked, and the only way to observe SC-003
and SC-004.** Connect two screens and record every frame each receives.

```sh
# Screen A: token for op-dresden   (holds /fabs/dresden)
# Screen B: token for op-multi     (holds munich + dresden)
```

Then, in order:

| Action | Screen A (dresden) | Screen B (multi) |
|---|---|---|
| publish a **munich** layout | **nothing** | receives |
| publish a **dresden** layout | receives | receives |
| archive the munich layout | **nothing** | receives |
| publish an overlay used only by a **dresden** published layout | receives | receives |
| publish an overlay used only by a **munich** published layout | **nothing** | receives |
| publish an overlay used by **no published layout** | **nothing** | **nothing** |
| publish an overlay used only by a **draft** | **nothing** | **nothing** |

The last two rows are FR-011 and FR-013, and both are invisible when they work
— nothing arrives either way. Assert on the *absence* over a bounded wait, not
on "no exception was thrown".

Then a third screen with a token holding **no** fab group: it must receive
**none** of the six frames.

## 5. The frames that were already scoped still work

Change a system variable referenced by an overlay, and highlight an overlay.
`ResolvedOverlayTextChanged` (#1396) and `OverlayHighlightChanged` (#1398)
must behave exactly as before — this feature touches the other four call sites
and must not disturb these two.

`ResolvedOverlayTextChanged` is the one frame on the latency-critical leg
(event → overlay state ≤ 200 ms). If it regresses, something was changed that
should not have been.
