# Feature Specification: Retire a camera from the management app

**Feature Branch**: `032-retire-camera-ui`
**Created**: 2026-08-24
**Status**: Draft
**Input**: Issue #1860 — "A camera cannot be retired from the management app — the endpoint has had no client since spec 028"

---

## Why this exists

Spec 028 built retirement. Nothing calls it.

An operator who replaces a camera cannot take the old one out of the
catalogue. The only routes are `curl` or the database, so every hardware
replacement in a 250-camera fab needs someone with API or database access —
and until they get to it, the catalogue describes hardware that is not there.
That is the complaint #1433 was filed about, still unaddressed from where an
operator sits.

This is the **third** endpoint to ship without a client. Spec 028 built retire,
spec 029 built read-one and edit; #1854 was the return trip for spec 029's
pair. This is the return trip for spec 028's.

Spec 030 built the camera detail page, so the control now has a home that did
not exist before. That page already knows the camera's status, marks a retired
one, and **omits** the address-correction control when retired. The terminal
state is already expressed; this feature adds the way to enter it.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Retire a replaced camera (Priority: P1)

An operator has replaced the hardware at `line-3-inlet`. They open that
camera's page, retire it, and it leaves the catalogue.

**Why this priority**: This is the entire operator-facing gap. Everything else
in this spec exists to make this safe.

**Independent Test**: Register a camera, open its page, retire it, and confirm
it is gone from the default listing while still readable at its own address.

**Acceptance Scenarios**:

1. **Given** an active camera's detail page, **When** the operator retires it,
   **Then** the page shows the camera as retired without a full page reload.
2. **Given** a camera was just retired, **When** the operator opens the camera
   listing, **Then** that camera is not in it.
3. **Given** a camera was just retired, **When** the operator returns to its
   own address, **Then** the record still opens and is marked retired.
4. **Given** a retired camera's detail page, **When** the operator looks for a
   way to retire it, **Then** there is none — the control is **absent**.

---

### User Story 2 - Understand what retiring costs before confirming (Priority: P1)

Before the operator commits, they are told what retirement does — including
the two consequences that are invisible from the camera's own page.

**Why this priority**: Also P1, and inseparable from US1. Retirement is
terminal with no un-retire, so a confirmation that fails to say so ships a
one-click unrecoverable action. Shipping US1 without US2 would be worse than
shipping neither.

**Independent Test**: Open the confirmation and read it. It must name the
camera, say retirement is permanent, and state both side effects.

**Acceptance Scenarios**:

1. **Given** the confirmation is open, **When** the operator reads it,
   **Then** it names **this** camera — not "this camera" or "the selected
   item".
2. **Given** the confirmation is open, **Then** it says retirement is
   permanent and cannot be undone.
3. **Given** the confirmation is open, **Then** it says the live stream stops.
4. **Given** the confirmation is open, **Then** it says the name becomes
   available for reuse in that fab.
5. **Given** the confirmation is open, **When** the operator dismisses it,
   **Then** nothing is retired and the camera is unchanged.

---

### User Story 3 - A refusal tells the operator something true (Priority: P2)

When the request is refused, the operator gets words matching what actually
happened — and a camera in another fab is refused exactly as one that never
existed.

**Why this priority**: P2 because the refusals are narrow — retirement is
idempotent and unversioned, so most of the failure modes an edit has do not
exist here. It is not P3 because one of them is a security property.

**Independent Test**: Open a camera belonging to another fab by pasted address
and compare the rendered output, field for field, with a camera identifier
that was never registered.

**Acceptance Scenarios**:

1. **Given** an operator holding only Dresden, **When** they open a Munich
   camera's address, **Then** they see **exactly** what a never-registered
   identifier produces.
2. **Given** the request fails for a transport or server reason, **When** the
   operator reads the message, **Then** it does not claim the camera was
   retired.

---

### Edge Cases

- **The camera was retired by someone else between the page loading and the
  operator confirming.** The request succeeds — retirement is idempotent — and
  the operator is told the camera is retired. The app must not report this as
  an error, and must not claim the operator was the one who retired it.
- **The operator opens the confirmation and leaves it open for a long time.**
  Dismissing must retire nothing. There is no timeout that confirms.
- **The camera is already retired when the page loads.** The control is absent
  (FR-004), so the confirmation is unreachable.
- **The request is in flight.** The confirm control must not be actionable
  twice; a double submit must not produce two requests.
- **The operator's session lost the required permission.** The refusal is
  reported as a refusal, not as a successful retirement.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to retire a camera from that camera's
  detail page, without database or API access.
- **FR-002**: Retiring MUST require an explicit confirmation step. A single
  action MUST NOT retire a camera.
- **FR-003**: The confirmation MUST name the camera being retired.
- **FR-004**: Once a camera is retired, the retire control MUST be **absent**
  from its page — not present-and-disabled, and not present-and-failing. This
  matches how spec 030 FR-007 treats the address-correction control, and it is
  asserted as absence, not as a failed submission.
- **FR-005**: The confirmation MUST state that retirement is permanent and
  cannot be undone.
- **FR-006**: The confirmation MUST state that the camera's live stream stops.
- **FR-007**: The confirmation MUST state that the camera's name becomes
  available for reuse within its fab.
- **FR-008**: Dismissing or cancelling the confirmation MUST retire nothing.
- **FR-009**: After a successful retirement the page MUST show the camera's new
  state without requiring a full page reload.
- **FR-010**: After a successful retirement the camera MUST no longer appear in
  the default camera listing.
- **FR-011**: After a successful retirement the camera's own address MUST still
  open its record, marked as retired.
- **FR-012**: The success message MUST NOT assert that this operator's action
  is what retired the camera. Retirement is idempotent and answers success
  either way, so the app cannot distinguish "I retired it" from "it was already
  retired" and MUST NOT claim to.
- **FR-013**: A camera in a fab the operator does not hold MUST be refused
  exactly as a camera that does not exist — same rendered output, compared
  field by field, with no distinguishing message added by the app.
- **FR-014**: Refusals MUST be turned into operator-facing words using the
  existing shared refusal vocabulary settled by ADR-0119. A new refusal
  predicate MUST NOT be added unless this spec records why the existing ones do
  not fit.
- **FR-015**: While a retirement request is in flight, the confirming control
  MUST NOT be actionable again.
- **FR-016**: The retirement request MUST NOT send an expected-version
  precondition. The endpoint is idempotent rather than version-checked
  (confirmed against the endpoint's declared contract — see Assumptions), so
  sending one would invent a failure mode the backend does not have.

### Key Entities

- **Camera** — already modelled. This feature reads its **name** (for the
  confirmation), its **status** (to decide whether the control exists), and
  changes only its status, by asking the existing endpoint to.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can take a replaced camera out of the catalogue using
  only the management app — **zero** database or API-client access required.
  Today this number is one; it must be zero.
- **SC-002**: Retiring a camera takes **at most three deliberate actions** from
  its detail page: open the confirmation, read it, confirm. No more, so it is
  not tedious; no fewer, so it is not a one-click unrecoverable action.
- **SC-003**: The confirmation states **all three** consequences — permanence,
  stream loss, name reuse. Verified by reading the rendered text for each,
  individually, not by asserting that a confirmation appeared.
- **SC-004**: A camera another operator may not see produces output **identical
  field-for-field** to a never-registered identifier. Verified by comparing the
  two renderings, not by observing that both showed an error.
- **SC-005**: An end-to-end test retires a camera, observes it leave the
  default listing, and observes it still readable at its own address — all
  three, in one run, driving the app rather than the API.
- **SC-006**: The camera's page reflects retirement with **no full page
  reload**.

---

## Assumptions

- **The retire endpoint is unchanged and needs no expected-version header.**
  Confirmed against its declared contract rather than assumed: it advertises
  `204`, `400`, `403`, `404` and declares no `409`, `412` or `428`, and its own
  comment records that retiring an already-retired camera answers `204`. This
  is what FR-016 rests on. If implementation shows otherwise, that contradicts
  spec 028's contract and is a **finding to raise, not to absorb**.
- **Retirement's side effects are already built.** Spec 028 retires the stream
  and removes the streaming path (FR-008 there), and frees the name for reuse
  within the fab (FR-006 there, with the repository defect found and fixed
  during that spec's US2). This feature *describes* those effects to the
  operator; it does not implement them and does not re-verify them.
- **The detail page is the right and only home for this control.** The listing
  is deliberately excluded — see Out of Scope.
- **No backend change is expected.** If one proves necessary it is a finding to
  raise.
- **The operator holds the write permission** already required to register and
  correct cameras. This feature introduces no new permission.

---

## Out of Scope

- **Bulk retire.** A fab-wide sweep has a different blast radius and deserves
  its own consideration of confirmation and undo.
- **Un-retire.** Terminal by decision (spec 028). This spec does not reopen it.
- **Retiring from the listing.** A destructive, unrecoverable action reached
  from a dense row of many cameras is a misclick waiting to happen; the detail
  page is where the operator has already established which camera they mean.
- **Any backend change.** The endpoint exists and is idempotent.
- **Telling the operator what the replacement camera should be**, or offering
  to register one under the freed name. The confirmation *mentions* reuse
  because it is a consequence; acting on it is a different feature.

---

## Dependencies

- Spec 028 (`028-retire-camera`) — the endpoint, its terminality and its side
  effects.
- Spec 030 (`030-camera-detail-view`) — the detail page, its routing, and the
  absent-control precedent this feature follows.
- Spec 031 (`031-stale-version-convention`) / ADR-0119 — the refusal vocabulary
  FR-014 requires.
