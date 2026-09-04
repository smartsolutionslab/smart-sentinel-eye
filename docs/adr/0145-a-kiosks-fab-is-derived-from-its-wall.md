# ADR-0145: A Kiosk's Fab Is Derived From Its Wall

**Status:** **Accepted**
**Date:** 2026-09-04
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0114 gave a single-fab caller their fab by inference and refused to
tie-break for a multi-fab one. Among the alternatives it rejected was a
per-session "current fab", and it **deferred rather than closed** the question:

> If multi-fab operation becomes common, this is worth revisiting as its own
> decision rather than arriving by accretion.
> — `docs/adr/0114-fab-inferred-for-single-fab-operators.md:101-103`

Issue #2069 is that question arriving. A kiosk session held by
`op-multi@smart-sentinel-eye.test` joins **one SignalR group per fab in its
token** (`LayoutLifecycleHub.OnConnectedAsync`), and its opening label resolves
across **every fab the caller holds** (ADR-0115 §2, via
`FabResolution.ResolveForReadAsync`). Both are right for an operator console
and wrong for a wall: a wall shows one plant, and nothing in `apps/kiosk-web`
had a notion of which — `grep -i fab apps/kiosk-web/src` returned only comments.

A later reader looking for the answer will look in ADR-0114, so it is recorded
here and ADR-0114 is left as it stands.

## Decision

**On the kiosk read path, the fab is derived from the wall being displayed —
`Layout.Fab` — and is never chosen, never inferred from the token, and never
held as session state.**

Concretely, and only on that path:

1. A resolved-text or highlight frame whose fab is not the displayed layout's
   fab is discarded by the client, **before any other per-frame state is
   touched** — in particular before the per-overlay version high-water mark, so
   a foreign frame cannot suppress a later legitimate one.
2. `GET /system-variables/snapshot` is called with `?fabId=` set to the
   displayed layout's fab, so the opening label resolves in that fab rather
   than in whichever of the caller's fabs sorts first.

**This decides reads only.** ADR-0114's deferral covered *writes*, where a fab
must genuinely be chosen because the effect lands somewhere. That stays
deferred and unchanged.

**This was decided by the maintainer in session on 2026-09-04**, on the
reconnaissance recorded in #2069. It is written down because it is a decision,
not because a spec derived it.

## Consequences

- The kiosk stops depending on "a kiosk holds exactly one fab" — a premise
  `op-multi` has falsified since the realm seeded it, and which two comments in
  `GetOverlaySnapshotQueryHandler` asserted as fact.
- A wall is single-fab by construction. Rendering two fabs' tiles on one wall
  is no longer merely unbuilt; for the kiosk it is decided against.
- Deriving is only as good as `Layout.Fab`. Spec 017 FR-018 exempts
  pre-existing layouts from retro-validation, so a legacy chain could hold
  tiles whose cameras belong elsewhere. `Layout.Fab` stays the single
  authoritative answer to "whose wall is this" regardless; this ADR does not
  claim the tile set is uniform and does not fix it.
- Management-web is untouched. It is a console, not a wall, and ADR-0115's
  resolve-in-the-caller's-fabs behaviour remains correct there.
- The client filter fails **closed**: a frame carrying no fab does not match a
  wall's fab and is dropped. That is the same direction the server already
  takes for an event with no fab, and it means the client half must not ship
  ahead of the server half.

## Alternatives Considered

**A fab picker on the kiosk, or a per-session current fab.** The alternative
ADR-0114 deferred. Rejected for the reason it was deferred: it adds session
state to answer a question the layout already answers, and it lets an operator
put the wrong plant's fab on a wall — the exact failure this removes.

**Filter server-side, per connection.** The hub does not know which layout a
connection is displaying, and telling it would make the wall server-side
session state — the alternative above, relocated. Frames stay addressed per fab
group; the client, which does know its wall, refuses what is not its own.

**Give kiosk accounts one fab and stop there.** Realm configuration is not an
invariant. `op-multi` exists, holds two fabs, and can sign into kiosk-web
today — the kiosk uses the ordinary Keycloak form flow. A decision resting on
nobody doing that is not a decision.
