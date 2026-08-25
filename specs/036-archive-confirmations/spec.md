# Feature Specification: Archiving asks before it happens

**Feature Branch**: `036-archive-confirmations`
**Created**: 2026-08-25
**Status**: Draft
**Input**: Issue 1866 — "Archiving a rule, overlay, layout or variable takes one click and asks nothing"

---

## Why this exists

Four surfaces in the management app archive on a **single click, asking nothing**:
rules, overlays, layouts and system variables.

Spec 032 added the product's first destructive confirmation, for retiring a
camera, and deliberately built its confirmation as a **shared** primitive so the
second one would copy it rather than diverge. This is the second one.

### The premise this was filed on turned out to be wrong

Issue 1866 asked whether archiving might be *recoverable*, in which case the
inconsistency with cameras would be justified rather than accidental.

**None of the four is recoverable**, and two are worse than a retired camera:

| Aggregate | Archiving is | What is left |
|---|---|---|
| Rule | terminal | clone it — a **new** rule, with its own history |
| Variable | terminal | its value is **cleared**, and it can never take another |
| **Layout** | **terminal, and stranding** | **nothing** — it can never be edited or published again |
| **Overlay** | **terminal, and stranding** | **nothing** |

So the inconsistency is accidental, and cameras are the one that got it right.

**Layouts and overlays are the sharp case.** Archiving the published revision
leaves no published revision to branch or revert from and no draft to edit or
publish. The record survives; the thing is dead. That is filed separately as
issue 1877, because fixing it is a domain decision — but it is *why* these two
confirmations must say more than the others.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nothing irreversible happens on one click (Priority: P1)

An operator clicking Archive is asked first, and told what they are about to
lose.

**Why this priority**: The whole feature. Four one-click irreversible operations
exist today; after this, none does.

**Independent Test**: Click Archive on each of the four surfaces and confirm
that nothing is archived until a second, deliberate confirmation.

**Acceptance Scenarios**:

1. **Given** any of the four archive controls, **When** the operator activates
   it, **Then** nothing is archived and a confirmation is presented.
2. **Given** a confirmation, **When** the operator dismisses it, **Then**
   nothing is archived.
3. **Given** a confirmation, **When** the operator confirms, **Then** the thing
   is archived exactly once.
4. **Given** a confirmation in flight, **When** the operator confirms again,
   **Then** no second request is made.

---

### User Story 2 - The confirmation names the thing, and its real cost (Priority: P1)

Each confirmation names what is being archived and states the consequences
specific to it — not a shared sentence that is true of nothing in particular.

**Why this priority**: Also P1, and inseparable. A confirmation that says
*"Are you sure?"* is the thing this feature replaces, not a smaller version of
it. And the four consequences genuinely differ.

**Independent Test**: Read all four confirmations. Each names its subject and
says something the others do not.

**Acceptance Scenarios**:

1. **Given** any confirmation, **Then** it names the thing being archived — the
   rule's name, the variable's name, the layout or overlay and which revision.
2. **Given** any confirmation, **Then** it says the action cannot be undone.
3. **Given** the **rule** confirmation, **Then** it says the rule cannot be
   published again and that authoring a replacement means cloning it.
4. **Given** the **variable** confirmation, **Then** it says the variable's
   current value is cleared and it can never be given another.
5. **Given** the **layout** or **overlay** confirmation, **Then** it says the
   layout or overlay **can never be edited or published again**.

---

### User Story 3 - An operator archiving something live is told so (Priority: P2)

Archiving a published layout or overlay changes what is on a wall of live video,
immediately. The operator is told before they confirm, not after.

**Why this priority**: P2 because it applies to two of the four rather than all
— but it is the consequence least visible from where the operator is standing,
and the most immediate.

**Independent Test**: Read the layout and overlay confirmations for a published
revision.

**Acceptance Scenarios**:

1. **Given** the **layout** confirmation for a published revision, **Then** it
   says kiosks showing that layout will be sent away from it.
2. **Given** the **overlay** confirmation for a published revision, **Then** it
   says kiosks using that overlay will stop showing it.

---

### Edge Cases

- **Archiving something already archived.** Not reachable — the controls are
  hidden once archived. If it were, the operation is idempotent and would
  succeed.
- **The confirmation is left open for a long time.** Dismissing archives
  nothing; there is no timeout that confirms.
- **The archive is refused.** The operator is told, and the confirmation is not
  treated as having succeeded.
- **A draft revision rather than a published one.** Archiving a draft does not
  strand the layout — a published revision may still exist to branch from — and
  no kiosk is showing a draft. The confirmation must not claim consequences that
  do not apply.
- **Two operators archiving the same thing at once.** One succeeds; the other is
  refused on the version and told so.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Archiving a rule, overlay, layout revision or system variable MUST
  require an explicit confirmation. A single action MUST NOT archive anything.
- **FR-002**: Dismissing or cancelling a confirmation MUST archive nothing.
- **FR-003**: Every confirmation MUST name the thing being archived.
- **FR-004**: Every confirmation MUST state that archiving cannot be undone.
- **FR-005**: The **rule** confirmation MUST state that the rule cannot be
  published again, and that a replacement means cloning it into a new rule.
- **FR-006**: The **variable** confirmation MUST state that its current value is
  cleared and that it can never be given another.
- **FR-007**: The **layout** and **overlay** confirmations MUST state that the
  layout or overlay **can never be edited or published again**. This sentence
  MUST NOT be softened to "cannot be undone", which is true of all four and
  understates these two.
- **FR-008**: When the revision being archived is **published**, the layout and
  overlay confirmations MUST state that kiosks currently showing it are
  affected immediately.
- **FR-009**: While an archive request is in flight, the confirming control MUST
  NOT be actionable again.
- **FR-010**: A refused archive MUST be reported to the operator, and MUST NOT
  be presented as success.
- **FR-011**: The four confirmations MUST use one shared confirmation
  behaviour, so that dismiss-does-nothing and no-double-submit hold identically
  in all four.
- **FR-012**: This feature MUST NOT change any archive operation itself — what
  archiving does, what it refuses, or what it announces.

### Key Entities

None. This feature adds no data and changes no stored shape. It puts a question
in front of four existing operations.

---

## Success Criteria *(mandatory)*

- **SC-001**: The number of one-click irreversible operations in the management
  app is **zero**. Today it is four.
- **SC-002**: Each of the four confirmations names its subject, verified by
  reading all four — not by observing that a confirmation appeared.
- **SC-003**: The layout and overlay confirmations state permanence in terms of
  editing and publishing, verified by reading for that specific claim rather
  than for a generic "cannot be undone".
- **SC-004**: Dismissing a confirmation results in **zero** archive requests,
  verified for all four.
- **SC-005**: Confirming twice while a request is in flight results in **one**
  request, verified for all four.
- **SC-006**: No archive operation's behaviour changes, verified by the four
  **backend** contexts' existing tests passing **unchanged**.

  > **Corrected at the Phase 2 gate.** This originally read *"the four contexts'
  > existing tests"*, which a reader would apply to the interface tests too —
  > and one of those **cannot** pass unchanged. `RulesPage`'s test clicks Archive
  > and expects the request to have been made; after this feature, clicking
  > Archive asks a question and makes no request.
  >
  > The requirement conflated the archive **operation** (unchanged — that is
  > FR-012, and it holds) with **how the interface reaches it** (changed, which
  > is the entire feature). That one test is updated to confirm first and then
  > keep its existing assertion, which proves both that the confirmation is
  > required and that confirming sends exactly what it sent before. Every other
  > frontend test and every backend suite is untouched. See
  > [research.md](./research.md) §5.

---

## Assumptions

- **All four confirm, because none is recoverable.** The alternative worth
  considering was confirming only the two stranding cases and leaving rules and
  variables alone. Rejected: a cloned rule and a redefined variable both cost an
  identity and a history, and "this one is only somewhat irreversible" is not a
  distinction to encode in whether a question is asked. It **is** a distinction
  worth encoding in what the question says, which FR-005 through FR-008 do.

- **This does not wait for the stranding fix (issue 1877).** The confirmation is
  worth having whichever way that resolves; only its wording would change, and
  only for two of the four. Waiting would leave four one-click irreversible
  operations in place while a domain decision is argued.

- **The shared confirmation behaviour already exists.** Spec 032 built it
  deliberately so a second destructive action would copy it rather than
  diverge — that was the whole argument for making it shared with one caller.
  This is that second caller, and FR-011 requires it be used rather than
  re-implemented.

- **The consequences stated are the ones verified.** Each was read from the
  code rather than assumed: the rule's own documentation says cloning is the
  path; archiving a variable clears its value and refuses later ones; a kiosk
  showing an archived layout is navigated away from it. Nothing is claimed that
  was not checked.

---

## Out of Scope

- **Fixing the stranding** (issue 1877). This feature warns about it; deciding
  whether it should be possible at all is a domain decision.
- **Any change to archiving itself** — what it does, refuses or announces.
- **Un-archive**, for anything.
- **Confirmations anywhere else.** Four surfaces, named. Publishing, reverting
  and deleting a draft are not in scope, and reverting in particular is
  recoverable.
- **Bulk archive.** Does not exist, and would need its own consideration.
