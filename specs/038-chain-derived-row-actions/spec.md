# Feature Specification: A row offers the actions its chain actually supports

**Feature Branch**: `038-chain-derived-row-actions`

**Created**: 2026-08-25

**Status**: Draft

**Issue**: 1879 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Both layout and overlay rows decide every action from the chain's
**newest** revision. A chain is not its newest revision, and the gap between
those two produces one dead end and one false warning. The decision taken on
1879 is to derive the row from the **chain**: every action targets the revision
it logically acts on, and the row says which revision is live.

---

## Why this exists

An operator publishes a wall. They start an edit, think better of it, and
discard the draft. The wall is still up on every kiosk in the fab.

Its row in the management app now offers **nothing at all**. No edit, no revert,
no archive. The layout is live and unmanageable, and the only route back is an
API call.

Nothing is broken underneath. The service will happily revert that layout,
archive it, or branch a new draft from it — every one of those is a request it
accepts. The app simply stopped offering them, because it decides what a row can
do by looking at the chain's newest revision, and on this chain the newest
revision is the one that was thrown away.

### The same mistake, pointed the other way

The reverse case is worse, because it does not look broken.

A wall is live and has an open draft. The row offers **Archive**. An operator
who clicks it is asked to confirm, and told:

> This takes the layout out of service. You can bring it back later by editing
> it, and the tiles are kept.

None of that happens. The button archives the *draft*; the wall stays live and
untouched. The operator has been told they are taking a wall off the kiosks at
the exact moment they are not. A missing button is a dead end an operator can
report. A warning that describes the wrong thing is one they act on.

Both are the same root cause, and neither is fixed by adding a condition.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The live wall under a discarded draft can be managed (Priority: P1)

An operator discarded a draft on a published wall. The row still shows the wall
as live, and offers what a live wall offers: edit it, revert it, take it out of
service. Editing it starts from the published wall, not from the draft that was
thrown away.

**Why this priority**: It is what 1879 was filed for, and until it is fixed there
is a class of live layout the management app cannot manage at all.

**Independent Test**: Publish a layout, branch a draft, discard the draft. The
row offers actions, and editing opens the published configuration.

**Acceptance Scenarios**:

1. **Given** a chain with a published revision under a discarded newer one,
   **When** the operator views its row,
   **Then** the row offers the same actions a plainly published chain offers.
2. **Given** that chain,
   **When** the operator chooses to edit it,
   **Then** the new draft starts from the **published** revision's configuration,
   not from the discarded one.
3. **Given** that chain,
   **When** the operator reverts or takes it out of service,
   **Then** the action applies to the **published** revision.

---

### User Story 2 — Taking a wall out of service means the wall (Priority: P1)

An operator with a live wall and an open draft decides to take the wall out of
service. They are asked to confirm, told the kiosks showing it will be sent away,
and that is what happens. Separately, they can discard the draft — a different
action, a different word, and a confirmation that does not claim the wall is
going anywhere.

**Why this priority**: Equal to US1. This is the false warning, and it is shown
at the moment of an irreversible decision. It is the only defect here an operator
can act on wrongly rather than merely be blocked by.

**Independent Test**: On a chain with a live revision and an open draft, the two
actions target different revisions and say different things.

**Acceptance Scenarios**:

1. **Given** a chain with a published revision and an open draft,
   **When** the operator takes the layout out of service,
   **Then** the action applies to the **published** revision, and the
   confirmation states the kiosk consequence.
2. **Given** the same chain,
   **When** the operator discards the draft,
   **Then** the action applies to the **draft**, and the confirmation does
   **not** claim the layout goes out of service or that kiosks are affected —
   because neither happens.
3. **Given** the same chain,
   **When** the operator reads either confirmation,
   **Then** it names the revision it is about, so the two cannot be confused.

---

### User Story 3 — The row says which revision is live (Priority: P2)

An operator scanning the list sees, for each chain, the revision that is on
kiosks — and, when there is one, that a draft is in progress. Today the row
describes the newest revision, so a live wall under a discarded draft reads as
*Archived* while it is playing on the floor.

**Why this priority**: P2 because US1 and US2 restore correct behaviour and this
makes it legible. But a row that misreports which revision is live is how an
operator picks the wrong wall, and it is the same root cause.

**Independent Test**: A chain whose newest revision is not the live one reports
the live one.

**Acceptance Scenarios**:

1. **Given** a chain with a published revision under a newer archived one,
   **When** the operator views its row,
   **Then** the row identifies the **published** revision as the live one.
2. **Given** a chain with a published revision and an open draft,
   **When** the operator views its row,
   **Then** the row identifies the published revision as live **and** shows that
   a draft exists.
3. **Given** a chain with no live revision at all,
   **When** the operator views its row,
   **Then** the row says so rather than naming a revision as live.

---

### Edge Cases

- **A chain with no revisions.** Cannot occur — a chain is created with its
  first revision — but the row must not depend on that being true in a way that
  fails loudly if a listing ever returns one.
- **A chain with a published revision, an open draft, *and* older archived
  revisions.** Behaves as US2's chain. Archived history never changes what is
  offered.
- **Every revision archived.** Offers the recovery shipped in spec 037, and
  nothing else. That behaviour is unchanged and this feature must not disturb it.
- **A draft-only chain.** Publish and discard, as today. There is no live
  revision, so nothing offers to revert or take it out of service.
- **Two open drafts on one chain.** Reachable today: branching is permitted while
  a draft is open whenever a published revision exists, which can leave two.
  **Observed, not fixed** — this feature does not change what the service
  permits, and the row must simply behave sensibly rather than assume one draft.
- **The overlay twin has no designer step.** Its edit action branches directly
  rather than opening an editor, so US1's second scenario is about which revision
  the branch copies rather than what a dialog is pre-filled with.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every reachable chain shape MUST offer at least one action. A row
  that offers nothing is the defect this feature exists to remove.
- **FR-002**: Each action MUST target the revision it logically acts on:
  publishing acts on the draft; reverting and taking out of service act on the
  live revision; editing branches a new draft.
- **FR-003**: A chain with a live revision MUST offer to edit, revert and take it
  out of service, **regardless of what newer revisions it holds** — an archived
  or draft revision above the live one MUST NOT remove any of them.
- **FR-004**: Editing a chain with a live revision MUST start the new draft from
  the **live** revision, never from a newer archived one.
- **FR-005**: Discarding a draft MUST be a **separate action with its own name**,
  distinct from taking the layout out of service. One word meaning two things on
  one row is what produced the false warning.
- **FR-006**: The confirmation for discarding a draft MUST NOT claim the layout
  goes out of service, that kiosks are affected, or that the chain becomes
  recoverable-by-editing. None of those happen while a live revision remains.
- **FR-007**: The confirmation for taking a layout out of service MUST state the
  kiosk consequence whenever the revision being acted on is live — which, once
  the action targets the live revision, is every time it is offered.
- **FR-008**: Each confirmation MUST name the revision it is about, so the two
  cannot be mistaken for one another.
- **FR-009**: The row MUST identify the **live** revision, and MUST indicate when
  a draft exists, without hiding either.
- **FR-010**: The row's summary of a chain's contents MUST describe the **live**
  revision when there is one, for the same reason as FR-009.
- **FR-011**: A chain with no live revision MUST NOT present any revision as
  live.
- **FR-012**: The recovery behaviour for a fully-archived chain MUST be
  unchanged. It is the same rule seen from the chain rather than a special case,
  and it MUST keep working.
- **FR-013**: No service behaviour changes — not what any operation does, what it
  refuses, or what it announces. This is entirely a change to what the app offers
  and says.
- **FR-014**: Every change MUST apply to **both** the layout and the overlay row.
  They are deliberate twins, and a fix applied to one is a divergence.

### Key Entities

- **Chain**: the logical layout or overlay an operator sees as one named thing,
  owning an ordered sequence of revisions. **The subject of a row.**
- **Live revision**: the chain's published revision, the one kiosks are showing.
  At most one exists at a time; a chain may have none.
- **Open draft**: a draft revision, work in progress, invisible to kiosks.
- **Newest revision**: the highest-numbered revision. **Not** necessarily either
  of the above — which is the whole substance of this feature.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The number of reachable chain shapes that offer no action is
  **zero**, demonstrated shape by shape rather than in aggregate.
- **SC-002**: No confirmation in either row states a consequence that does not
  apply to the revision being acted on.
- **SC-003**: Every action's target is demonstrated by the **revision it was
  applied to**, not by the request succeeding. Acting on the wrong revision
  succeeds just as readily as acting on the right one, which is why today's
  defect went unnoticed.
- **SC-004**: For every chain shape holding a live revision, the row names that
  revision as live.
- **SC-005**: A layout that is live on kiosks can be edited, reverted and taken
  out of service from the app in every shape it can reach — no shape requires an
  API call.
- **SC-006**: No service-side behaviour changes. The tests covering layout and
  overlay operations pass untouched.
- **SC-007**: Both rows behave identically. Every behavioural claim above holds
  for the layout and for the overlay.

---

## Assumptions

- **This needs no architectural decision record.** It decides no domain question,
  reverses no recorded decision, and changes no contract — it corrects an app
  that was asking the wrong question about data it already had. ADR-0121 already
  settled what an archived chain means, and this feature reads that decision
  rather than revisiting it. Recorded here explicitly so a later reader does not
  go looking for the ADR that would explain it.
- **The recovery affordance added for fully-archived chains is the first instance
  of this model, not an exception to it.** Deriving the row from the chain
  subsumes it, and the feature should absorb it rather than leaving it beside the
  new rule.
- **Discarding a draft is destructive enough to confirm**, because the draft's
  work is lost and there is no way back to it. It is not destructive enough to
  warn about kiosks, because none are affected.
- **The row keeps its existing vocabulary wherever the meaning is unchanged.**
  Publish, edit, revert and archive already mean what operators expect on the
  shapes where they are correct today; only the draft-discarding case needs a new
  word, because it is a distinct action that has been wearing another's name.
- **Existing checks that read a row's state text must keep passing.** At least
  one end-to-end check publishes from a row and then asserts the row reports
  itself as published; a change to what the row says must not break that, and if
  it does the check is to be updated deliberately rather than by convenience.
- **No new data.** Everything needed to identify the live revision, a draft and
  the archived history is already present in what the app is given.

---

## Out of Scope

- Any service, endpoint or domain change. The service is already correct
  (**FR-013**), and if a change there proves necessary that is a **finding to
  raise, not absorb**.
- The recovery of a fully-archived chain, which shipped in spec 037.
- Any way to view or edit an arbitrary historical revision. The row acts on the
  live revision and the open draft; the rest of the chain is history.
- **Preventing two open drafts on one chain.** Reachable today and left alone —
  changing it means changing what the service permits, which this feature does
  not do.
- Bulk actions, sorting, or any other change to the listing beyond what each row
  offers and reports.
