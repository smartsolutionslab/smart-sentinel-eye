# Feature Specification: A layout or overlay archived by mistake can be recovered

**Feature Branch**: `037-recover-archived-revision`

**Created**: 2026-08-25

**Status**: Draft

**Issue**: 1877 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Archiving a layout's or overlay's published revision strands the
aggregate permanently. The decision taken on 1877 is that this is a **defect**,
and the fix is the smallest one that restores the operator's work: branching a
new draft falls back to the newest archived revision when the chain has nothing
else to branch from.

---

## Why this exists

A wall is archived. It was the wrong wall, or it was the right wall and the shift
changed its mind. Today the operator has one recourse: build it again from
nothing — retype the grid, re-pick every camera, re-bind every overlay — under a
new identity, with the original's history left behind as a tombstone.

The record survives. The work does not.

This feature gives the work back. Archiving stays exactly as consequential as it
was — it still takes the wall out of service, it still sends kiosks away — but it
stops being a way to destroy an afternoon's configuration with one click.

### What is actually broken, stated precisely

Issue 1877 says *"there is no path out"*. That is **not quite right**, and the
correction matters because it sets the size of the fix.

A chain whose every revision is archived releases its name — the name-uniqueness
rule ignores fully-archived chains. So the operator **can** recreate the layout
under the same name today. What they cannot do is keep the identity, the revision
history, or the grid and tiles.

**The defect is the loss of the work, not the total absence of a path.** A fix
that gave back a path but not the tiles would fix nothing.

### The exact condition

Every revision is Draft, Published or Archived. So:

> **no Published revision and no Draft revision** ⟺ **every revision Archived**

Those are the same set of chains. That set is the stranded one, and it is also
exactly the set whose name is already free. This equivalence is the spine of the
feature: it defines when the new behaviour applies, and it is why the new
behaviour cannot accidentally apply to a chain that has an open draft.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Recovering an archived wall, with its tiles (Priority: P1)

An operator archived a published wall. They open Layouts, filter to Archived,
find it, and choose to edit it. A new draft appears carrying the grid and every
tile exactly as the archived revision held them. They change what they meant to
change, publish, and the wall is live again — same identifier, same history, one
more revision on the end.

**Why this priority**: This *is* the feature. Everything else in this spec either
makes this reachable or stops it from breaking something else.

**Independent Test**: Archive a published layout, then branch, edit and publish
it. The layout is live, carries the recovered tiles, and its identifier is
unchanged.

**Acceptance Scenarios**:

1. **Given** a layout whose only revision was published and then archived,
   **When** the operator branches a new draft,
   **Then** a draft revision numbered one higher than the archived one is added
   to the **same** chain, carrying the archived revision's grid and its complete
   tile set.
2. **Given** that recovered draft,
   **When** the operator edits and publishes it,
   **Then** the layout has a Published revision again and kiosks can show it.
   *Branching alone is not the outcome — a draft nobody can publish is not a
   recovery.*
3. **Given** the same situation for an overlay,
   **When** the operator branches a new draft,
   **Then** it carries the archived revision's label.
4. **Given** a recovered chain,
   **When** the operator inspects it,
   **Then** its identifier and every prior revision are unchanged — recovery adds
   history, it does not replace it.

---

### User Story 2 — The chain with an open draft is still refused (Priority: P1)

An operator has a layout with no published revision and one draft still being
worked on. They ask for a new draft. The system refuses, exactly as it does
today, and says why: there is already a draft to edit.

**Why this priority**: Equal to US1, because this is the guard the feature is
most likely to destroy. The obvious implementation — *branch from the newest
revision, whatever state it is in* — makes this case succeed, minting a second
competing draft. Two open drafts on one chain is a worse defect than the one
being fixed.

**Independent Test**: A chain holding only a draft refuses to branch, and the
refusal is observable through the same route an API caller takes.

**Acceptance Scenarios**:

1. **Given** a chain whose only revision is a Draft,
   **When** a new draft is requested,
   **Then** it is refused, and no revision is added.
2. **Given** the same chain,
   **When** the refusal is read,
   **Then** it names the real reason — a draft is already open — rather than
   reporting the absence of a published revision as though nothing were editable.

---

### User Story 3 — Archiving still means something, and the warning says what (Priority: P1)

An operator clicks Archive on a live wall. They are still asked to confirm, and
the confirmation still tells them kiosks showing it will be sent away
immediately. What it no longer tells them is that the layout is finished forever,
because after this feature that is false.

**Why this priority**: The confirmation shipped in spec 036 asserts the opposite
of what this feature makes true. Leaving it is not a cosmetic lapse — it is the
product actively lying to the operator at the moment of the decision.

**Independent Test**: The archive confirmation for a published layout still warns
about kiosks and no longer claims the layout can never be edited or published
again.

**Acceptance Scenarios**:

1. **Given** an operator archiving a published layout or overlay,
   **When** the confirmation appears,
   **Then** it still names the subject and still states the kiosk consequence.
2. **Given** the same confirmation,
   **When** its text is read,
   **Then** it does **not** claim the layout or overlay can never be edited or
   published again.
3. **Given** the same confirmation,
   **When** its text is read,
   **Then** it does not collapse into *"This cannot be undone"* either — that
   becomes false in the other direction, and a warning that overstates is one
   operators learn to click through.

---

### User Story 4 — The recovery is reachable from the app (Priority: P2)

The operator finds the archived layout in the listing and there is something to
click. Today the edit action is offered only while a revision is Published, so an
archived chain offers nothing at all.

**Why this priority**: P2 only because US1–US3 are separable and this depends on
US1. But a recovery that exists in the domain and cannot be reached from the
management app is not a feature — it is a fact about the code.

**Independent Test**: A fully-archived layout's row in the management app offers
the edit action, and using it produces the recovered draft.

**Acceptance Scenarios**:

1. **Given** a layout whose every revision is archived,
   **When** the operator views the layouts listing,
   **Then** the row offers the same edit action a published layout offers.
2. **Given** a layout that has a published revision *and* an open draft,
   **When** the operator views the row,
   **Then** the actions offered are unchanged from today.

---

### Edge Cases

- **The name was taken while the chain sat archived.** A stranded chain's name is
  free, so another chain may legitimately have claimed it. Recovering the first
  one would then leave two live chains sharing a name in the same fab — and
  nothing would catch it, because name uniqueness is enforced only when a chain
  is created and the database index over the name is **not** unique in either
  context. This must be refused, and the refusal must say that the name is now in
  use. Covered by FR-009.
- **A chain with several archived revisions.** It branches from the **newest** of
  them — the last thing the operator saw — not the first, and not an arbitrary
  one.
- **Publishing already archives a sibling.** When a draft is published, the
  previously-published revision is archived in the same breath. That path never
  strands anything, because a new revision becomes published at the same moment.
  It is untouched by this feature.
- **Archiving a draft.** Abandoning a draft while a published revision stands
  leaves the chain perfectly healthy. Untouched.
- **Re-archiving an already-archived revision.** Idempotent and silent today.
  Untouched.
- **Reverting a published revision.** Sends kiosks away without archiving
  anything. Untouched — and it is the existing precedent that "kiosks were told
  to stop showing this" does not mean "this is dead".
- **A recovered chain is archived again.** It strands no more than the first time:
  it becomes recoverable again by the same route. There is no once-only budget.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A layout or overlay chain with **no Published revision and no Draft
  revision** MUST be able to produce a new Draft revision, branched from its
  **newest Archived** revision.
- **FR-002**: The branched draft MUST carry the archived revision's full
  configuration — for a layout its grid and its complete tile set including every
  camera and overlay binding; for an overlay its label. Recovering an empty draft
  is not recovering anything.
- **FR-003**: The recovered draft MUST be added to the **same** chain, keeping the
  chain's identifier, name, fab and every prior revision, and taking the next
  revision number in sequence.
- **FR-004**: A recovered draft MUST be editable and publishable by the ordinary
  routes, with no special case anywhere downstream of its creation.
- **FR-005**: A chain that **has a Draft revision** MUST continue to be refused a
  new draft, whether or not it also has a Published revision. This is the
  behaviour that exists today and it MUST NOT change.
- **FR-006**: That refusal MUST be observable at the boundary an API caller
  actually meets, not only deep in the domain. A rule enforced in one layer and
  not another is the failure mode this feature is most exposed to.
- **FR-007**: The refusal MUST keep its present classification and error code, and
  its message MUST state the real reason — that a draft is already open — rather
  than reporting the absence of a published revision.
- **FR-008**: A chain that **has a Published revision** MUST continue to branch
  from that published revision, unchanged, whatever else the chain contains.
- **FR-009**: Recovering a chain MUST be refused when the chain's name is held by
  another non-archived chain in the same scope, and the refusal MUST say so. The
  name became free when the chain was stranded; recovery must not silently
  reintroduce a duplicate.
- **FR-010**: Archiving MUST continue to do exactly what it does today — take the
  revision out of service and announce it so kiosks stop showing it. This feature
  changes what can happen **afterwards**, never what archiving itself does.
- **FR-011**: The archive confirmation for a layout and for an overlay MUST no
  longer claim the aggregate can never be edited or published again, MUST continue
  to name the subject, and MUST continue to state the kiosk consequence when the
  revision being archived is published.
- **FR-012**: The confirmation MUST NOT be reduced to a generic irreversibility
  warning. Both the old sentence and *"this cannot be undone"* become false; the
  replacement must state what is true.
- **FR-013**: The management app MUST offer the edit action on a fully-archived
  chain, and MUST leave the actions offered on every other chain shape unchanged.
- **FR-014**: Every change MUST be applied to **both** the layout and the overlay
  aggregate. They are deliberate twins, and a fix applied to one is a divergence.

### Key Entities

- **Chain**: the logical layout or overlay an operator names and sees as one
  thing. Owns an ordered sequence of revisions. Has an identity that outlives any
  individual revision — which is the whole reason recovery is worth more than
  recreation.
- **Revision**: one edit of a chain, in exactly one of Draft, Published or
  Archived, carrying the configuration the operator authored. Archived means
  *out of service*; after this feature it stops also meaning *unreachable*.
- **Stranded chain**: a chain whose every revision is Archived. Equivalently, a
  chain with no Published and no Draft revision. The set this feature is about.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The number of ways to permanently strand a layout or overlay from
  the management app is **zero**. Every reachable chain shape either offers a way
  forward or is refused with a reason that names another way forward.
- **SC-002**: An operator who archives a wall by mistake recovers it — live again,
  with its original tiles — **without re-entering any configuration**.
- **SC-003**: A recovered chain keeps its identifier and every prior revision.
  Nothing about recovery mints a new identity.
- **SC-004**: A chain with an open draft is refused a second one, and the refusal
  is demonstrated at the boundary an API caller meets, in both aggregates.
- **SC-005**: The four existing tests that assert the present refusal — the domain
  and application refusal tests in each aggregate — **pass unchanged**. They are
  built on draft-only chains, which this feature deliberately leaves refused; if
  any of them has to be edited, the change went wider than intended.
- **SC-006**: No archive confirmation in the management app states anything that
  is false after this feature, and none has stopped stating the kiosk
  consequence.
- **SC-007**: Both aggregates behave identically. Any behavioural assertion in
  this spec holds for the layout and for the overlay.

---

## Assumptions

- **Recovery is the same action, not a new one.** Branching a draft off an
  archived revision is the existing edit action becoming available again, with no
  separate command, no distinct label and no extra confirmation. The operator's
  intent is identical in both cases, and a second way to do one thing is surface
  without benefit. This was preferred explicitly over introducing a distinct
  *un-archive* or *reinstate* action, which was considered and rejected on 1877 as
  the larger-surface option.
- **Recovering is not itself destructive**, so it needs no confirmation of its
  own. It adds a draft; it publishes nothing and disturbs no kiosk until the
  operator publishes, which already confirms nothing today.
- **The archived revision's configuration is intact.** Archiving changes a
  revision's state and stamps a time; it does not clear the payload. Verified
  against both aggregates.
- **The listing already shows archived chains**, so the operator can find what
  they are recovering. This spec does not add a way to preview an archived
  revision's contents before branching; the branch itself surfaces them in the
  editor. If that proves insufficient in use it is a **finding to raise, not
  absorb**.
- **A decision record is required.** This settles what *archived* means for a
  revisioned aggregate — out of service, not unreachable — which is a domain
  decision that outlives the code implementing it, and which should be consistent
  with the existing decision to keep the two aggregates as deliberate twins.
- **No data migration.** Existing stranded chains become recoverable by the same
  rule as any other; nothing needs rewriting to make that true.

---

## Out of Scope

- An explicit **un-archive** or **reinstate** command. Considered on 1877 and
  rejected in favour of the smaller change.
- Any change to what **Publish**, **Revert**, **EditDraft** or **Archive**
  themselves do.
- **Rules** and **system variables**. Both are terminal on archive by a different
  route and are recoverable by cloning or redefining. A separate conversation.
- **Promoting name uniqueness to a database constraint.** FR-009 closes the hole
  this feature could otherwise open; the broader question of enforcing chain-name
  uniqueness in the database was already deferred as a separate decision and stays
  deferred.
- Previewing an archived revision's contents from the listing without branching.
