# Implementation Plan: A name is mutable exactly when it is not an address

**Branch**: `033-rename-convention` · **Spec**: [spec.md](./spec.md) · **Date**: 2026-08-24
**Issue**: #1850

## Summary

Record the convention as an ADR, enforce it with a build check, and make a
camera renameable.

The convention is one sentence — *a name may be changed only where the
aggregate is not addressed by it* — and it is mechanically checkable, which is
what makes this more than documentation.

**The rename itself is small. What is not small is that a rename is the first
camera operation that can fail two different conflict ways at once** — the
version moved, or the name is taken — and those two must never be mistaken for
each other, because retrying helps with exactly one of them.

## Technical Context

**Language**: C# / .NET 10
**Storage**: PostgreSQL via EF Core. CameraCatalog is plain CRUD (not
event-sourced)
**Messaging**: Wolverine + Postgres outbox (ADR-0088)
**Testing**: xUnit + Shouldly + hand-written fakes; integration via the Aspire
fixture (ADR-0103); NetArchTest and source-scanning architecture tests
**Concurrency**: two-layer optimistic (ADR-0043/0113) — a rename is a mutating
request and **needs `If-Match`**, unlike retire
**Refusal vocabulary**: ADR-0119 — a code ending `_STALE` is a lost update, and
a name collision is not one

**No migration.** The `name_normalized` generated column, the partial unique
index and `CameraName`'s normalisation all already exist
([research.md](./research.md) §2).

**No UI.** Out of scope by the spec.

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV Latency budget** | **N/A** — nothing on the event-to-overlay path |
| **§IX No speculative generality** | The ADR rules generally rather than on a closed list of five, because research §5 found the list of five was already short. Generality here is *describing what exists correctly*, not building for a need that does not exist |
| **DDD / value objects** | `CameraName` already exists and normalises. A rename adds one aggregate behaviour; no primitive crosses a boundary |
| **No cross-context references** | One line is added to `AuditObservability`'s handler — via `Shared.Contracts`, which is the sanctioned route (research §4). No project reference is introduced |
| **Smallest possible change** (ADR-0036) | The repository contract change is the one unavoidable widening, and research §1 shows why the smaller alternative does not work |
| **Coverage gates** | Domain ≥ 90%, Application ≥ 80% — a new aggregate behaviour and a new handler, both directly tested |

**No violations.**

## Phases

Five phases. Phase 1 is independent of everything else and can go first or last;
Phases 2–4 are a chain.

### Phase 1 — The convention, and its enforcement

The part that outlives the feature.

- `docs/adr/0120-name-mutability.md` — a name may be changed only where the
  aggregate is not addressed by it. States the rule generally, enumerates
  today's surfaces as evidence, and records why `Variable` is the sharpest
  exclusion: `Automation` references it **by name** across a boundary ADR-0016
  forbids a project reference across, so a rename would break rules with nothing
  able to detect it.
- `tests/Architecture.Tests/NameMutabilityConventionTests.cs` — source-scanning,
  following `StaleCodeConventionTests`. Fails when a context binds an
  unconstrained route parameter **and** exposes a rename.

**It must fail for a violation a future context would invent**, not only for a
hardcoded list — research §5 established that unconstrained-route-parameter is
the detectable signal, so the check generalises.

### Phase 2 — Asking the right question

`ExistsByNameAsync` cannot express what a rename needs (research §1), and this
is the feature's central obstacle.

- Extend `ICameraRepository` so the existence question can **exclude one
  camera**.
- Change `CameraRepository` and
  `tests/.../Fakes/InMemoryCameraRepository.cs` **in the same commit**. Their
  divergence is precisely how spec 028's defect happened, on this same
  predicate.

### Phase 3 — The rename, and the two conflicts

- `Camera.Rename(...)` on the aggregate, refusing when retired (FR-009) and
  raising no event when the name is unchanged (FR-010).
- `RenameCameraCommand` + handler + `RenameCameraErrors`.
- `PATCH`-style endpoint requiring `If-Match`, with the fab resolved **before**
  every other precondition — the ordering spec 029's contract makes part of the
  contract, because answering a precondition failure for another fab's camera
  confirms it exists.

**The two conflicts are this phase's substance:**

| Failure | Distinguishable because | Retrying helps? |
|---|---|---|
| version moved | code ends `_STALE` (ADR-0119) | yes, after re-reading |
| name taken | code does **not** end `_STALE` | **no** — the name is someone else's |

A caller that cannot tell them apart retries a rename that will never succeed.

### Phase 4 — Announcing it

- `CameraRenamedV1` in `Shared.Contracts`, mirroring `CameraAddressChangedV1`.
- Domain event + handler publishing it through the outbox.
- One `Handle` line in `AuditObservability`'s `IntegrationEventAuditHandler` —
  a different context, reached via `Shared.Contracts` as the other sixteen
  events are (research §4).

**FR-013 needs no work**: past events carry the old name as a record of what was
true then, and a rename appends rather than revisiting.

### Phase 5 — Proving it end to end

- Integration tests against the real stack: rename, collide, case-collide,
  cross-fab success, retired refusal, freed-name reuse.
- Update spec 029's FR-012 to point here (FR-015).

## Sizing

| Phase | Risk |
|---|---|
| 1 | The check generalising rather than hardcoding |
| 2 | **Highest** — a shared predicate with a history of layer disagreement |
| 3 | The two-conflict distinction |
| 4 | Low — mirrors an existing event exactly |
| 5 | Aspire fixture time |

## Three things most likely to go wrong

1. **Renaming a camera collides with itself.** `ExistsByNameAsync` finds the
   camera being renamed, and the rename is refused as "name taken" against its
   own name. Confirmed reachable in research §1. The tempting fix — short-circuit
   when the new name equals the current one — **passes the obvious test and
   fails the case-only rename**, which is a real change that normalises to the
   same value.

2. **The name collision is reported as a lost update.** Both are conflicts, and
   the nearest existing failure is `CAMERA_VERSION_STALE`. If the collision ends
   up sharing a status, or worse a `_STALE` suffix, a caller re-reads and retries
   forever against a name that belongs to someone else. ADR-0119 exists for
   exactly this and the architecture test from spec 031 will catch a `_STALE`
   suffix — but not a shared *status*, so the distinction has to be asserted.

3. **The repository and its in-memory double drift.** They already did once, on
   this predicate, and the unit tests passed throughout because the double was
   the thing under test. Changed together, or the feature ships a rule that
   holds only in tests.

## Findings to raise, not absorb

- **Nothing translates a unique-index violation into a usable response**
  (research §3). A rename losing the race between check and commit yields a 500.
  The window is small and the invariant still holds, so this feature does not
  close it.
- ~~**Publishing an integration event is never a one-context change** (research
  §4). Every new event needs a line in `AuditObservability`'s overload list, and
  omitting it means the event is silently never audited.~~
  **Retracted 2026-08-25.** The "silently" is false —
  `BoundaryTests.Every_integration_event_has_an_audit_handler` has caught a
  missing overload since spec 009, verified by deleting one and watching it fail.
  Research §4 records how the mistake was made. Issue 1870 closed as invalid.
- **The spec's inventory of five aggregates was short** (research §5).
  `{integrationName}` and `{clientId}` are also non-identifier addresses. Handled
  by ruling generally rather than by extending the list — but worth knowing that
  the list was wrong, since the next reader may trust it.

## Out of scope

Renaming rules, variables, layouts or overlays; changing a camera's fab
(forbidden, not deferred); a user interface; atomic name swaps.
