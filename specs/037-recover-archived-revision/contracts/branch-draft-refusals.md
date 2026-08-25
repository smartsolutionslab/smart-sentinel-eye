# Contract: what branching a draft answers, per chain shape

**Feature**: `037-recover-archived-revision` · **Plan**: [plan.md](../plan.md)

The branch endpoint is unchanged — same route, same command, same success shape.
What changes is which chain shapes it accepts and what it says about the ones it
still refuses.

Applies identically to `POST /layouts/{id}/draft` and `POST /overlays/{id}/draft`.

---

## The four chain shapes

A chain always holds at least one revision, and every revision is Draft,
Published or Archived. That gives four reachable shapes.

| # | Shape | Today | After | Branch source |
|---|---|---|---|---|
| 1 | Has a **Published** revision | 200 | **200, unchanged** | The Published revision |
| 2 | No Published, has a **Draft** | 409 | **409, unchanged** — new message | — |
| 3 | Every revision **Archived** | 409 | **200** | The **newest** Archived revision |
| 4 | Every revision Archived, **name taken** by another live chain | 409 | **409** — new failure | — |

Shape 1 is unchanged in every respect, including when the chain *also* holds a
Draft or Archived revision. A Published revision always wins as the branch source.

---

## Shape 2 — the refusal that stays

**Status**: `409 Conflict` — unchanged
**Code**: `LAYOUT_NO_PUBLISHED_REVISION` / `OVERLAY_NO_PUBLISHED_REVISION` — **unchanged**

The code stays because the condition it names is still true, and because an error
code is a contract a client may switch on. Changing it would be a breaking change
bought for nothing.

**Message changes** (FR-007). Today:

> Layout {id} has no Published revision to branch from.

That describes the situation without naming the reason. After this feature it is
also actively unhelpful: the operator now knows some chains without a Published
revision *can* be branched, so being told this one cannot leaves them nowhere.
The real reason is that a draft is already open, and the way forward is to edit
it.

Required:

> Layout {id} already has a Draft revision {n}. Edit that draft rather than
> branching another.

Same shape for the overlay, with `Overlay` and the overlay's revision number.

**Assert**: the code and status are the ones above, *and* the message names the
open draft. Asserting the code alone passes against the old message, which is the
half FR-007 is about.

---

## Shape 3 — the recovery

**Status**: `200 OK`, body is the new revision number — identical to shape 1.

No new status, no distinguishing field, no flag saying "this was a recovery".
Recovery is the same action becoming available again (spec Assumptions), and a
client that had to branch on *how* the draft was produced would be a client
coupled to something it does not need to know.

**The source is the newest Archived revision** — the highest revision number, the
last thing the operator saw. Not the first, and not an arbitrary one.

**The new draft carries the source's full configuration** (FR-002): the layout's
grid and complete tile set including every camera and overlay binding; the
overlay's label. This is the requirement the feature exists for — a recovery that
mints an empty draft has recovered nothing while satisfying any assertion that a
draft exists.

**The new draft's number is the chain's maximum plus one**, by the same
`MaxRevisionNumber().Next()` the published path uses. Archived revisions count
toward the maximum, so numbering never reuses a value.

---

## Shape 4 — FR-009, the collision this feature would otherwise create

**Status**: `409 Conflict`
**Code**: `LAYOUT_NAME_TAKEN` / `OVERLAY_NAME_TAKEN` — **the code strings that
already exist** for this condition on the create path
(`CreateLayoutDraftErrors.cs`, `CreateOverlayDraftErrors.cs`)

Reusing the create path's code string is deliberate. It is the same condition —
this name is spoken for — reached by a different route, and a client that already
handles the create-path collision handles this one for free.

The *record* is new, because `BranchDraftRevisionError` is its own closed
hierarchy and generics are invariant; only the code string is shared. Construct it
through `BranchDraftRevisionFailures`, not the variant, like every other failure
here.

**Message**:

> Layout {id} cannot be recovered: the name '{name}' is now used by another
> layout in this fab.

The overlay's omits the fab, because overlay names are global.

### Why this shape exists at all

A chain becomes recoverable exactly when every revision is Archived — and that is
the same condition under which its name is released, because both name lookups
exclude fully-archived chains. So between archiving and recovering, the name is
free and another chain may legitimately have taken it.

Recovering the first would then leave two live chains sharing a name, and
**nothing downstream would catch it**: uniqueness is enforced only when a chain is
created, and the database index over the name is not unique in either context.

### Where the check runs, and why that is not an optimisation

**Only inside the recovery branch.** The chain being recovered is fully archived
at the instant of the check, so it is excluded from its own name lookup by the
repository's predicate — any hit is necessarily a different chain, and no
`excluding` parameter is needed.

Hoist the same check onto the shape-1 path and it inverts: a live chain matches
its own name, and every branch of every healthy chain is refused. The narrow
placement is a correctness condition, and the code should say so where it sits.

---

## What does not change

- The route, the command shape, the success body.
- The `If-Match` expected-version check (ADR-0113), which runs **before** any of
  the above so a stale caller is refused first, on every shape.
- The fab scoping on the identifier lookup (spec 017 FR-006) — a chain in another
  fab and one that never existed still leave identically.
- The `BranchedDraftRevision` log line. Recovery is not a distinct operation.
- Archiving: what it does, what it refuses, what it announces (FR-010).
