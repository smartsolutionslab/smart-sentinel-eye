# Contract: Layouts API

**Feature**: `017-layout-fab-scoping` | **Date**: 2026-08-16

Eight endpoints exist. **All eight change** — six writes and two reads. This is
the opposite of spec 016, where nothing was authored and the whole ADR-0114
decision table was irrelevant.

## The rule that applies to seven of the eight

> A layout in a fab the caller does not hold is reported **exactly** as a
> layout that never existed — **404**, byte-identical body — on the read-one
> and on all six writes (FR-006).

Stated per endpoint below rather than once, because spec 013 shipped this
wrong on one endpoint and only a review caught it.

**404, not 403**, and the distinction is not stylistic: the caller addressed a
*layout*, so the answer is about that layout, and "forbidden" would confirm it
exists. 403 is reserved for the one case where the caller names a *fab* —
`?fabId=` — because then the answer is about the fab and hides nothing.

## `POST /layouts` — create a draft

| | |
|---|---|
| Gains | `?fabId=` (optional) |
| Fab resolution | `FabResolution.ResolveForWriteAsync` — full ADR-0114 table |
| New statuses | **400** `LAYOUT_FAB_REQUIRED`, **403** |

| Caller holds | `?fabId=` | Outcome |
|---|---|---|
| exactly one fab | omitted | created in that fab (inferred) |
| several fabs | omitted | **400** `LAYOUT_FAB_REQUIRED` — naming is the caller's to do |
| any | a fab they hold | created in that fab |
| any | a fab they do not hold | **403** |
| no fab at all | either | **403** |

Additionally **400** when any tile's camera is not in the resolved fab
(FR-014), or resolves to no camera at all (FR-015). The problem detail names
the offending tile's position — a layout has up to four tiles and "one of them
is wrong" is not actionable.

**409 `LAYOUT_NAME_TAKEN` becomes fab-scoped** (FR-019). It fires only when the
name is taken *in the resolved fab*. Today the check is global, so a name held
by another plant answers 409 to a caller who cannot see that layout — an
enumeration oracle on the write path. After this change, a name held only in
another fab is indistinguishable from a name nobody holds.

## `GET /layouts` — list

| | |
|---|---|
| Gains | `?fabId=` (optional, narrows), `fab` on each row |
| Fab resolution | `FabResolution.ResolveForReadAsync` — a read spans all the caller's fabs |
| New statuses | **403** (naming a fab not held, or holding none) |

A read does not have to choose, which is the deliberate asymmetry with the
write path. `fab` on each row so a multi-fab operator can tell two plants'
layouts apart without a second request.

## `GET /layouts/{layoutIdentifier}` — read one

| | |
|---|---|
| Gains | `fab` in the response body |
| Scoping | 404 for a layout outside the caller's fabs — **indistinguishable** from one that never existed |
| New statuses | **403** (naming a fab not held, or holding none) |

## The five remaining writes

`POST /{id}/revisions/{n}/publish`, `POST /{id}/revisions/{n}/archive`,
`POST /{id}/draft`, `PATCH /{id}/revisions/{n}`, `POST /{id}/revisions/{n}/revert`

| | |
|---|---|
| Gains | nothing in the request — the fab comes from the layout, not the caller |
| Scoping | **404** for a layout outside the caller's fabs, identical to one that never existed |
| New statuses | **403** only for a caller holding no fab at all |

**None of these takes `?fabId=`.** The layout already has a fab; asking the
caller for one would let them name a fab that disagrees with it. They resolve
the caller's fabs and check the layout is inside them — a read-shaped check on
a write-shaped endpoint.

`PATCH .../revisions/{n}` additionally re-validates tiles against FR-014, since
an edit can introduce a cross-fab camera that creation refused.

### The ordering that matters

**Resolve the fab before reading any precondition.** Publishing a revision that
does not exist, on a layout in another fab, must answer 404-for-the-layout, not
404-for-the-revision — the second confirms the layout exists. Spec 015's
contract flagged the same trap on its edit path.

## `fab` on the wire

`LayoutDto` gains `fab` (additive). Existing clients ignore it; no version bump
is needed under ADR-0073.

## Not changing

Nothing. Unlike spec 016 there is no endpoint here that is deliberately left
unscoped — every one of the eight is operator-facing. The unscoped surface in
this feature is the **hub**, and it has its own contract
([hub-frames.md](./hub-frames.md)).
