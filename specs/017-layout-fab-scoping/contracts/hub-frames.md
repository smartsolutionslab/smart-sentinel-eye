# Contract: Layout lifecycle hub frames

**Feature**: `017-layout-fab-scoping` | **Date**: 2026-08-16

`LayoutLifecycleHub` at `/hubs/layouts`. Six frames. **Two are already
scoped; this feature does the other four.**

The delivery mechanism is unchanged and untouched. A connection joins
`fab:<name>` for each fab in its `groups` claim on connect, from the same
source `FabClaims` reads — so the hub and the server-side guard cannot
disagree about what a caller holds. This feature supplies only the missing
input: which group(s) a frame is addressed to.

## The six frames

| Frame | Addressed to | Status |
|---|---|---|
| `ResolvedOverlayTextChanged` | the changed variable's fab | ✅ #1396 |
| `OverlayHighlightChanged` | the requesting fab | ✅ #1398 |
| `LayoutRevisionPublished` | **the layout's fab** | this feature |
| `LayoutRevisionArchived` | **the layout's fab** | this feature |
| `OverlayRevisionPublished` | **every fab referencing the overlay** | this feature |
| `OverlayRevisionArchived` | **every fab referencing the overlay** | this feature |

Singular for the layout frames, plural for the overlay frames. That difference
is the whole of the two-halves split: a layout belongs to one fab, an overlay
is *used by* however many.

## Half A — the layout frames

Addressed to exactly one group, `fab:<the layout's fab>`.

The fab travels on the notification record, filled by the Application-layer
domain-event handler that already builds it. Nothing queries anything: the
layout is in hand at that point and its fab is a property.

## Half B — the overlay frames

Addressed to **zero or more** groups: every fab with a **published** layout
whose tiles carry that overlay ([data-model.md](./data-model.md)).

- Referenced by one fab → one group.
- Referenced by published layouts in several fabs → several groups. **All of
  them** — FR-010 is "no fab missing" as well as "no fab extra".
- Referenced by nobody → **no group, and therefore no send at all**. Not a
  send to an empty group; the loop simply has nothing to iterate.
- Referenced only by a **draft** → does not count (FR-013).

**The overlay never gains a fab** (FR-012, ADR-0115). The answer is derived
per frame from what references it, so it tracks usage without anything being
re-recorded.

### Where the resolution happens

In `OverlayRevisionPublishedV1Handler` / `OverlayRevisionArchivedV1Handler`,
not in the broadcaster. The broadcaster maps a notification to a hub message
and sends it; giving it a database query would make it the only piece of
`Infrastructure/Broadcasting` that reads state.

## The connection with no fab

Joins no group, so receives **none** of the six frames. Intended: a screen
showing nothing is recoverable; one showing another plant's overlay text is
not. Already true for the two scoped frames; this feature extends it to all
six.

## Payload

**Unchanged, in all six.** No frame gains a `fab` field. The fab decides *who*
receives a frame, not what it says — and putting it in the payload would tell a
recipient about a fab boundary they have no use for.

## What a Dresden screen sees after this feature

| Event in Munich | Dresden screen |
|---|---|
| layout published / archived | nothing |
| overlay published / archived, overlay unused in Dresden | nothing |
| overlay published / archived, **same overlay also used by a published Dresden layout** | **receives it** — correct: that overlay is on Dresden's screens too |
| variable value changed | nothing (#1396) |
| overlay highlighted | nothing (#1398) |

The third row is the one worth reading twice. It is not a leak: the overlay is
a shared template (ADR-0115), and Dresden is being told about content it is
itself displaying.
