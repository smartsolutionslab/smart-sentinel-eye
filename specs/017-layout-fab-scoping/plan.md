# Implementation Plan: Fab-scope layout composition

**Branch**: `017-layout-fab-scoping` | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-layout-fab-scoping/spec.md`

## Summary

Give `Layout` a fab resolved from its author, scope the reads and all six
writes to the caller's fabs, and supply the four `Clients.All` call sites with
the fab(s) their frame belongs to. Layout frames go to the layout's own fab;
overlay frames go to the fabs that reference the overlay, answered by a join
over this context's own tables so no overlay ever gains a fab.

This is the fifth fab-scoping feature and the **last context to get one**. It
is also the first with two halves that are different problems, and the first
where a tile can reach across a boundary the other four specs built.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (ADR-0070), EF Core +
Npgsql, SignalR (the existing `LayoutLifecycleHub`), Wolverine (ADR-0042),
`ServiceDefaults.Authorization` — **both** `FabClaims` and `FabResolution`,
write half included. This is the opposite of spec 016: six operator-driven
writes exist here, so ADR-0114's decision table applies in full.

**Storage**: PostgreSQL, `layout-composition-db` (ADR-0009). Tables `layouts`,
`layout_revisions`, `layout_revision_tiles` — all three participate.

**Testing**: xUnit + Shouldly + Moq; integration on the Aspire fixture
(ADR-0052, ADR-0103). SignalR frames are asserted with a real hub connection,
as spec 014's push tests already do.

**Target Platform**: Linux containers, Aspire → k3s (ADR-0024, ADR-0025)

**Project Type**: Web service (bounded context) **plus** a cross-context HTTP
client. No UI work — the management app's layout surface needs no change,
because the fab is resolved from the caller rather than chosen in a form.

**Performance Goals**: Reads gain one `IN` term on an indexed column. The
overlay-frame query runs once per overlay publish/archive — an authoring
action, not a per-event push.

**Constraints**: FR-014 requires knowing a camera's fab, which this context
cannot answer alone. That is the one open mechanism and §III records it.

**Scale/Scope**: 250-camera target; ≤4 tiles per layout (ADR-0112). One
aggregate, eight endpoints, four broadcast call sites, one migration.

## The surface, verified before planning

Specs 015 and 016 both taught this the hard way — 015 by drafting against
analogy and withdrawing three requirements, 016 by checking first and losing
none. What exists:

| | |
|---|---|
| Endpoints | `POST /layouts`, `GET /layouts/{id}`, `GET /layouts`, `POST .../publish`, `POST .../archive`, `POST /{id}/draft`, `PATCH .../revisions/{n}`, `POST .../revert` — **eight**, six of them writes |
| Aggregate | `Layout` — `Name`, `Revisions`, `CreatedAt`, `CreatedBy`. **No fab.** |
| Name uniqueness | **Global, and enforced in the handler** (`GetByNameAsync`), not by a unique index — `ix_layouts_name` is plain. Invisible in the schema, so it must become fab-scoped (FR-019) or it leaks. |
| Sub-entity | `Revision` — `Number`, `State` (Draft/Published/Archived), `Grid`, `Tiles` |
| Value object | `Tile` — required `CameraIdentifier`, **optional** `OverlayIdentifier`, `GridPosition` |
| Tables | `layouts`, `layout_revisions`, `layout_revision_tiles` (`camera_id`, `overlay_id`) |
| Hub | `LayoutLifecycleHub.FabGroup(fab)`, joined per fab on connect from the `groups` claim |
| Frames scoped already | 2 of 6 — `ResolvedOverlayTextChanged` (#1396), `OverlayHighlightChanged` (#1398) |
| Frames on `Clients.All` | 4 — layout published/archived, overlay published/archived |
| Cross-context messaging | Already consumes **four** integration events |
| Cross-context HTTP | **None.** This context calls no other today. |

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. On-Prem First | ✅ | No new infrastructure. No new credential — see §III. |
| II. DDD with Value Objects | ✅ | `FabIdentifier` as this context's own copy per ADR-0044 — the **seventh**. Grammar must match the other six; a test asserts it. |
| III. Bounded Context Isolation | ⚠️ **See below** | Adds the **first synchronous HTTP call** from LayoutComposition to another context. |
| IV. Latency Budget | ✅ | N/A with reason. The one latency-critical frame, `ResolvedOverlayTextChanged`, was scoped in #1396 and is untouched. The FR-014 call is on an authoring write. |
| V. Spec-Driven Development | ✅ | This plan; tasks follow. |
| VI. Aspire Is the Composition Root | ✅ | The camera-catalog client is registered by name and resolved by service discovery, as SystemVariables and StreamDistribution already do. |
| VII. Observability | ✅ | A refused cross-fab tile is recorded — it is an attempt to cross a boundary, not a typo. |
| VIII. Safe by Default at Trust Boundaries | ✅ | FR-015 fails closed: an unresolvable camera is refused, so FR-014 cannot be bypassed with an unknown identifier. |
| IX. Forward-Compatible Strategy Interfaces | ✅ | None introduced. |

### §III — the one thing to argue about

LayoutComposition already **listens** to four other contexts' events. What it
has never done is **ask one a question and wait**. FR-014 makes it do that: on
every layout create and draft edit, it asks CameraCatalog whether the tiles'
cameras are in the layout's fab.

Three things bound it, and one distinguishes it from spec 016's exception:

1. **No new credential.** The call carries the **operator's own token**,
   narrowed to the layout's fab. Spec 016 needed a cross-fab service account
   (ADR-0116) because it had to read every fab's cameras with no operator
   present; here the operator is the caller and holds exactly the fab in
   question. **This exception is strictly smaller than the one already
   accepted.**
2. **Write path only.** Reads, hub pushes and video are untouched. A
   CameraCatalog outage stops layout *authoring* and nothing else.
3. **It answers a question this context must not answer itself.** The
   alternative — a local projection of camera fabs — re-implements a rule
   CameraCatalog owns and needs a standing cross-fab credential to seed
   ([research.md](./research.md) §1).

**Gate: PASS**, with §III recorded as a justified, bounded exception. It does
not breach ADR-0016: published HTTP API, no value objects across the boundary,
no project reference.

## Why the two halves stay separate in the code

The spec keeps them apart and so does this plan, because the failure mode is
that Half B's novelty leaks into Half A's routine.

**Half A is a fifth application of ADR-0114.** It should look like specs
013–015: a `FabIdentifier`, a column, `FabResolution.ResolveForWriteAsync` on
create, `ResolveForReadAsync` on the two reads, 404-not-403 for another fab's
layout, and a backfill.

**Half B touches no aggregate at all.** No new state, no new column, no domain
change — a join and two handlers. If Half B starts wanting a column, that is
the signal it has drifted into giving an overlay a fab, which ADR-0115 forbids.

## Project Structure

### Documentation (this feature)

```text
specs/017-layout-fab-scoping/
├── plan.md              # This file
├── spec.md
├── research.md          # Phase 0 — the FR-014 mechanism and four findings
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1
├── checklists/
└── tasks.md             # /speckit-tasks — NOT created here
```

### Source Code (repository root)

```text
src/LayoutComposition/
├── Domain/Layout/
│   ├── FabIdentifier.cs          # NEW — this context's own copy (ADR-0044), the 7th
│   └── Layout.cs                 # + Fab, required by CreateDraft, no setter
├── Application/
│   ├── Commands/                 # + Fab on CreateLayoutDraftCommand; the other five resolve it
│   ├── Queries/                  # + Fabs on both reads
│   ├── EventHandlers/            # overlay handlers resolve the referencing fabs
│   └── Tiles/                    # NEW — ICameraFabGuard, the FR-014 seam
├── Infrastructure/
│   ├── Persistence/Migrations/   # fab column, backfill, NOT NULL, index
│   ├── Cameras/                  # NEW — CameraCatalogFabGuard (HTTP, caller's token)
│   └── Broadcasting/             # 4 call sites: Clients.All -> Clients.Group
└── Api/LayoutEndpoints*.cs       # fab resolution on all eight

tests/
├── LayoutComposition.Domain.Tests/
├── LayoutComposition.Application.Tests/
└── Integration.Tests/LayoutComposition/
```

**Structure Decision**: existing per-context layout. Two new folders, each for
one seam — `Application/Tiles/` for the FR-014 port and
`Infrastructure/Cameras/` for its HTTP adapter. No new projects. No `apps/`
work: the fab is resolved from the caller, so no form gains a field.

## Phase 0: Research

Complete — [research.md](./research.md). One decision (the FR-014 mechanism)
and four findings, of which two changed the design:

- **§1** settles FR-014 as a synchronous call with the caller's token, and
  records why a local projection was rejected.
- **§2** establishes that Half B is a join, not an open question — the reason
  closing #1397 in one feature is reasonable.
- **§4** and **§5** are why §III is a small step and why no delivery mechanism
  is needed.

## Phase 1: Design & Contracts

- `data-model.md` — the `Layout` change, the column and index, the backfill,
  and the Half B query written out.
- `contracts/layouts-api.md` — eight endpoints, what each gains, and the
  404-not-403 rule stated per endpoint rather than once.
- `contracts/hub-frames.md` — six frames, which fab(s) each is addressed to,
  and the two that were already done.
- `quickstart.md` — including a two-fab kiosk session, which is the only way to
  observe SC-003 and SC-004.

## Constitution Check — re-evaluated after Phase 1

Design added three things worth re-testing against the gates, and **no gate
outcome changed**:

- **`ICameraFabGuard`** (§II, §IX). A port with one implementation and a caller
  that needs it today — not a speculative strategy interface. It exists so the
  FR-014 rule can be tested without CameraCatalog, which is the same reason
  `ICameraFabLookup` exists in spec 016.
- **`ix_layout_revision_tiles_overlay_id`** (§IV). New index on a column that
  has never been queried. It supports the Half B join; without it the query is
  a sequential scan of every tile in the product on each overlay publish.
- **Notification records gain `Fab` / `Fabs`** (§III). In-process records
  inside this context, not `Shared.Contracts` — no wire contract changes, so
  no ADR-0073 versioning question arises.

The §III exception is unchanged and remains **strictly smaller than
ADR-0116's**: the caller's own token, one route, write path only, no standing
credential.

**Gate: PASS.**

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| First synchronous cross-context HTTP call from this context (§III) | FR-014 must know a camera's fab, and only CameraCatalog knows it | A local `camera → fab` projection needs a standing cross-fab credential to seed and re-implements a rule CameraCatalog owns ([research.md](./research.md) §1) |
| Seventh copy of `FabIdentifier` | ADR-0044 forbids sharing value objects across contexts | Sharing one would create the project reference §III exists to prevent; a grammar test keeps the copies in step |
