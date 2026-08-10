# ADR-0115: Overlays Are Fab-Neutral Templates; Variables Resolve in the Viewer's Fab

**Status:** **Accepted**
**Date:** 2026-08-10
**Supersedes:** —
**Superseded by:** —

## Context

Spec 014 fab-scopes system variables. Phases 2–5 gave the variable itself a
fab: the aggregate carries one, `(fab, name)` is the unique key, the
value-change consumer applies a change only within its own fab, and every
endpoint resolves the caller's fab.

Phase 6 was to do the same for the kiosk resolution path. Its tasks assume
an overlay has a fab:

> T032 — Record each overlay's fab when it is indexed.
> T033 — Key `IReverseIndex` on `(fab, variableName)`.

and `contracts/system-variables-api.md` states the intended behaviour as:

> `GET /system-variables/snapshot` — resolution is scoped to that overlay's
> own fab (FR-014).

**An overlay has no fab.** Verified three ways at the time of writing:

- `grep -ri fab src/OverlayDesigner/` returns nothing. The context has no fab
  concept in its domain, application, infrastructure or API.
- `OverlayRevisionPublishedDomainEventHandler` stamps
  `new EventMetadata(..., Fab: null, ...)` on the integration event that
  SystemVariables indexes from.
- `ReverseIndexSeederHostedService` seeds from `GET /overlays`, whose payload
  carries `overlayIdentifier` and `text` and nothing else.

Neither `research.md` nor `data-model.md` establishes where an overlay's fab
would come from; the plan simply assumed one existed.

Implementing T033 against that absence would key every overlay under a single
null-or-placeholder fab. That is the pre-existing global behaviour with a fab
column bolted on — while the code, the tests and the spec would all claim fab
isolation on the kiosk path. A wrong claim of isolation is worse than a
truthful absence of it, because the next person to touch the resolver would
have no reason to look.

## Decision

**An overlay is a fab-neutral template. A variable placeholder resolves in the
fab of whoever is viewing it, not in a fab belonging to the overlay.**

Concretely:

1. `IReverseIndex` stays keyed on `variableName`. It maps a name to the
   overlays whose label text references it, which is a statement about text
   and is genuinely fab-independent.
2. `GetOverlaySnapshotQuery` resolves within **the caller's** fabs, which the
   endpoint already resolves via `FabResolution` (ADR-0114).
3. A variable's domain events carry its fab, so the push fan-out resolves
   siblings within the fab that actually changed and stamps that fab on
   `ResolvedOverlayTextChangedV1`.
4. Delivery to screens is filtered on that fab. Kiosks already carry one —
   `Identity` has it on `EnrollKioskCommand` and the kiosk endpoints — so no
   new concept is introduced.

FR-014 is amended from "the overlay's own fab" to "the viewer's fab". FR-015
is unchanged in intent and now has a mechanism: the fab on the event.

## Rationale

The alternative was to fab-scope OverlayDesigner first: give the aggregate a
fab, migrate and backfill, resolve the fab on its endpoints, carry it on the
integration event, and teach the seeder to read it. That is a feature in its
own right, and it was rejected for two reasons beyond its size.

**It forces duplication of design work.** An overlay saying
`Line 1: {{oeeLine1}}` is the same design in every fab. Under a fab-owned
overlay, running it in two fabs means authoring it twice and keeping the two
copies in step by hand — the same drift problem ADR-0044 accepts for value
objects, but here with no compensating benefit and on content an operator
maintains rather than a developer.

**The template/instance split is the more honest model.** The overlay is a
layout of text and a reference to a name. What that name is worth is a
property of the plant looking at the screen. Making the reference itself
fab-bound conflates the design with one of its renderings.

The cost is that the same overlay renders differently on two screens, which is
the intended behaviour rather than a surprise, and that resolution now depends
on request context. The latter is why the fab appears on the events rather
than being looked up at the point of render.

## Consequences

- Spec 014's FR-014 and `contracts/system-variables-api.md` are amended.
  T032–T034 and T036 are superseded — there is no overlay fab to record, no
  key to widen and therefore no fake to bring into line. T035, T037 and T038
  are re-aimed at the viewer's fab.
- The phase gate that T031's baseline exists for is weakened but not wasted:
  the reverse-index key no longer changes, so the latency risk that motivated
  it is gone. The measurement stands on its own as the first thing ever to
  watch constitution §IV leg 4, which is the half of #749 it closes.
- The last mile of FR-015 — the hub declining to deliver a fab's update to
  another fab's screens — belongs to the broadcasting layer, not to
  SystemVariables. This ADR fixes the contract (the fab is on the event); the
  filter is a follow-up in that context.
- If OverlayDesigner later gains a fab for reasons of its own — ownership,
  access control over who may edit a design — this decision is not
  contradicted. An overlay could be *owned* by a fab while its placeholders
  still resolve in the viewer's. That would be a new decision, and this record
  should be revisited rather than assumed to cover it.
