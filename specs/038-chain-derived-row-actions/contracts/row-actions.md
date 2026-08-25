# Contract: what a row offers, per chain shape

**Feature**: `038-chain-derived-row-actions` · **Plan**: [plan.md](../plan.md)

Applies identically to the layout row and the overlay row. The one difference is
noted at the end and is not about which actions appear.

---

## The descriptor

Every row computes this once from its chain's revisions:

| Field | Meaning |
|---|---|
| `live` | The **Published** revision, or none. At most one exists — the aggregate enforces it. |
| `draft` | The **newest Draft** revision, or none. **A chain may hold several**; the row acts on the newest. |
| `newest` | The highest-numbered revision. Always exists. |
| `summarised` | `live ?? draft ?? newest` — the revision the row **describes**. |
| `fullyArchived` | `!live && !draft`. Not a separate rule: it is what having neither *means*. |

`summarised` is separate from every action target on purpose. What the row *says
about* a chain and what its buttons *do to* it are different questions, and
collapsing them is a smaller version of the defect this feature removes.

---

## The five actions

| Action | Offered when | Targets | Confirms? |
|---|---|---|---|
| **Publish** | `draft` exists | `draft` | no |
| **Discard draft** | `draft` exists | `draft` | **yes** |
| **Edit (new draft)** | `live` exists **or** `fullyArchived` | branches; opens from `live ?? newest` | no |
| **Revert** | `live` exists | `live` | no |
| **Archive** | `live` exists | `live` | **yes** |

**Every target is a revision number sent to the service.** Sending the wrong one
succeeds exactly as readily as sending the right one — which is why the current
defect went unnoticed — so each target is asserted by the number it sends, never
by the request succeeding.

---

## Every reachable shape

Written out per shape rather than as a rule, because the defect being fixed is
precisely a shape nobody enumerated. `D` = Draft, `P` = Published, `A` = Archived.

| Shape | Reached by | Offers | Changed? |
|---|---|---|---|
| `{D}` | create | Publish, Discard draft | Archive **renamed** to Discard draft — it was always archiving the draft |
| `{P}` | publish | Edit, Revert, Archive | unchanged |
| `{A}` | archive a draft-only chain | Edit | unchanged *(spec 037)* |
| `{P, D}` | publish, branch | Publish, Discard draft, Edit, Revert, Archive | **Archive now targets the live revision**; discard is separate |
| `{P, A}` | publish, branch, discard the draft | Edit, Revert, Archive | **was nothing at all** — the filed defect |
| `{A, D}` | publish, branch, archive the published — or branch a stranded chain | Publish, Discard draft | Archive renamed; it was already hitting the draft |
| `{D, D}` | publish, branch, **revert** | Publish, Discard draft | as above, on the newest draft |
| `{P, D, D}` | `{D, D}`, publish one, branch | all five | as `{P, D}` |

**`{D, D}` deserves its own note.** Two open drafts and nothing published,
reachable in two clicks from a published chain — branch, then revert — both
offered by the row. It is why `draft` is *the newest draft* rather than *the
draft*, and why a model that assumes at most one is wrong rather than merely
simplified.

**`{P, D}` offers all five, which is a lot of buttons.** Accepted: each is a real,
distinct operation on a chain that genuinely supports all five. FR-003 requires
Edit to remain offered even with a draft open, which is also the app's route to
`{P, D, D}` — recorded in research §8 as observed, not fixed.

---

## What the row says

| Shape | Badge | Summary describes |
|---|---|---|
| `live`, no draft | `v{live} · Published` | `live` |
| `live` and `draft` | `v{live} · Published · draft v{draft}` | `live` |
| No `live`, has `draft` | `v{draft} · Draft` | `draft` |
| Fully archived | `v{newest} · Archived` | `newest` |

The layout row's summary is its tile count and grid; the overlay row's is its
label preview text. Both follow `summarised`.

**`Published` appears in the badge exactly when a live revision exists**, which
keeps `e2e/layouts.spec.ts:56` and `:130` matching — both assert `/Published/` on
a row that is shape `{P}` at that moment. It also makes those assertions honest:
today a chain with a live revision under an archived newer one renders `Archived`,
and an assertion like theirs would have been reading a false state.

---

## What does not change

- Any service behaviour: what an operation does, refuses or announces (FR-013).
  Every action here already exists and already accepts the revision number the
  row will now send.
- The recovery of a fully-archived chain (spec 037, ADR-0121). It is the
  `!live && !draft` branch of the model rather than a case beside it.
- The optimistic-concurrency version each mutation carries.
- The list's state filters, the error banner, or anything outside a row.

## The one difference between the twins

The layout's **Edit** branches and then opens the designer pre-loaded from the
branch source. The overlay's branches and opens nothing.

That difference exists today and is not introduced here. Every question this
contract answers — which actions appear, what each targets, what the row says —
is answered identically for both.
