# ADR-0121: Archived means out of service, not unreachable

**Status:** **Accepted**
**Date:** 2026-08-25
**Supersedes:** —
**Superseded by:** —

## Context

`Layout` and `Overlay` are revisioned aggregates: a named chain owning an ordered
sequence of revisions, each in `Draft → Published → Archived`. Six public
behaviours act on a chain, and every one of them is guarded:

| Behaviour | Requires |
|---|---|
| `BranchDraft` | a currently-Published revision to copy from |
| `Revert` | Published |
| `Publish` | Draft |
| `EditDraft` → `ReplaceTiles` | Draft |
| `ArchiveRevision` | anything; idempotent on Archived |
| `CreateDraft` | `static` — mints a **new** chain with a new identity |

Nothing in that table admits an `Archived` revision as a source. So a chain whose
every revision is archived matched no behaviour at all: it kept its identifier,
kept its rows, and could never be edited or published again. The management app
reached that state in one click, and in the ordinary case — a wall published and
not since branched — the newest revision *is* the published one, so the common
path through the Archive button was the one that ended the chain.

Nothing appears to have decided this. Spec 003 specifies `Published → Archived`
as a real operation, with kiosks force-disconnecting, and spec 004 names
`Draft → Archived` "Abandon". Neither says whether archiving is meant to be the
end. The behaviour fell out of six guards written independently, each correct on
its own terms.

Two facts bound the answer.

**A stranded chain already releases its name.** Both repositories' name lookups
exclude chains whose every revision is archived, so recreating under the same name
works today. The chain is not unreachable in the sense of *nothing can be done* —
it is unreachable in the sense that **the work is lost**: the identity, the
revision history, and the grid and tiles, which have to be entered again.

**The archived *event* has never meant "dead".** `Revert` — which moves a
Published revision back to Draft, and is plainly not terminal — raises
`LayoutRevisionArchivedDomainEvent` anyway, purely so kiosks stop showing the
revision. That event has always carried *stop showing this*, never *this is
finished*. Any decision here should agree with a distinction the design was
already making.

## Decision

**An archived revision takes a chain out of service. It does not take it out of
reach.**

Concretely: **a chain with no Published revision and no Draft revision may branch
a new Draft from its newest Archived revision**, carrying that revision's
configuration. The operator edits it and publishes it, and the chain is live again
— same identifier, same history, one more revision on the end.

Three things follow, and each is part of the decision rather than a detail of it.

**1. The fallback is narrow, and the narrowness is load-bearing.** It applies only
when the chain has no Published *and* no Draft revision. Every revision is Draft,
Published or Archived, so that condition is exactly equivalent to *every revision
archived* — the stranded set, and the same set whose name is already free.

A chain holding an open Draft is still refused. Branching *the newest revision
whatever its state* would let it mint a second competing draft, which is a worse
defect than the one this fixes. A chain holding a Published revision still
branches from the Published one, whatever else it holds.

**2. Recovery is the same action, not a new one.** There is no un-archive, no
reinstate, no separate command, endpoint, event or confirmation. The existing
branch operation becomes available again on a chain where it was refused. The
operator's intent is identical in both cases, and a second way to do one thing is
surface bought for nothing.

**3. Recovery is refused when the name has been taken.** Because a stranded chain
releases its name, another chain may legitimately have claimed it in the meantime.
Recovering the first would leave two live chains sharing a name, and nothing
downstream would catch it — uniqueness is enforced only when a chain is created,
and the database index over a chain's name is not unique in either context. So the
recovery path checks, and refuses with the same code the create path uses.

**This is applied to both aggregates.** ADR-0104 keeps `Layout` and `Overlay` as
deliberate twins and instructs that a lifecycle change in one be checked against
the sibling; that instruction was followed and both carry this change. Nothing was
extracted — ADR-0104's rule-of-three revisit trigger needs a *third* revisioned
aggregate, and there is not one.

## Consequences

**Positive:**

- An archive made in error costs a click to undo instead of an afternoon of
  re-entering a grid, its cameras and its overlay bindings.
- The chain keeps its identifier and its revision history, so the audit trail
  reads as what happened — archived, then recovered — rather than as two unrelated
  layouts with similar names.
- The archive confirmations can now tell the truth about consequences that are
  real and immediate (kiosks are sent away) without overstating one that is not.
- The distinction the design was already making — `Revert` raising the archived
  event without archiving — is now stated rather than implied.

**Negative:**

- **"Archived" no longer means "settled".** Anyone reasoning about a chain's final
  state must now read the whole revision set, not the newest row. That is already
  true for other reasons, but this makes it load-bearing.
- **The recovery path carries a name check the branch path never had**, which is
  one extra query, and which is correct *only* while it sits inside the recovery
  branch. Hoisted onto the ordinary published-branch path it inverts: a live chain
  matches its own name and every branch is refused. That is a sharp edge, and it
  lives in a comment at the call site because the code does not show it.
- **A chain taken out of service deliberately can be brought back deliberately.**
  Archiving is no longer a way to guarantee something never returns. Nothing in
  the product needed that guarantee, but if something later does, it needs a
  different mechanism and a different record — not this one quietly re-tightened.
- **Two places to keep in step**, as ADR-0104 already accepted for this lifecycle.

## Alternatives Considered

**Option B — an explicit un-archive / reinstate command — REJECTED.** More honest
about intent, and much more surface: a command, an endpoint, a domain event, a
contract, kiosk handling for the reinstate push, and a UI action, in each of two
contexts. It also forces two questions this decision does not have to answer —
whether reinstating targets the revision or the chain, and what a kiosk that
already received the archived event should do about a reinstatement. Rejected as
disproportionate to a problem solved by relaxing one guard, and because it would
make recovery a *different* action when the operator's intent is the same one.

**Option C — accept the stranding and say so — REJECTED.** No code change. Write
down that archived is terminal for a revisioned aggregate, assert it, and extend
the archive confirmations to name the recreate path — matching the story `Rule`
already tells with "clone the rule to author a new one". Genuinely defensible, and
cheapest. Rejected because the recreate path loses the grid and tiles, which is
the expensive part of a wall and the part an operator cannot reconstruct from
memory; and because nothing had actually decided the stranding, so codifying it
would have been ratifying an accident.

**Option D — branch from the newest revision whatever its state — REJECTED.** The
shorter code, and it reads equivalent to the decision above. It is not: a chain
holding only a Draft would branch from that draft, minting a second competing
draft on one chain. Four existing tests — a domain and an application test in each
twin — are built on exactly that chain and assert the refusal; under Option D all
four would have had to be edited, which is how the difference was noticed.

## Implementation Notes

The rule this ADR relaxes is written in **three layers per aggregate**, and all
three have to move together:

1. `Layout.BranchDraft` / `Overlay.BranchDraft` — the domain guard.
2. `BranchDraftRevisionCommandHandler` in each context — a pre-check that refuses
   **before** the domain is reached, so changing the domain alone changes nothing
   observable through the API.
3. `LayoutsPage.tsx` / `OverlaysPage.tsx` — the edit action's gate, which decides
   whether the path is reachable from the management app at all.

The frontend gate tests the **chain** (`revisions.every(r => r.state === 'Archived')`),
not its newest row. A chain can hold a Published revision under an abandoned newer
draft, and that chain is not stranded.

No migration, no new dependency, no new endpoint, no new event. Existing stranded
chains become recoverable by the same rule as any other; nothing needs rewriting.

Spec `037-recover-archived-revision` implements this. Issue 1877 raised it.
