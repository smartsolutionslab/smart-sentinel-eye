# Feature Specification: Rename a camera from the management app

**Feature Branch**: `035-rename-camera-ui`
**Created**: 2026-08-25
**Status**: Draft
**Input**: Issue 1873 — "A camera cannot be renamed from the management app — the endpoint has had no client since spec 033"

---

## Why this exists

Spec 033 made a camera's name correctable, backed by ADR-0120. **Nothing calls
it.** Correcting a misnamed camera still means `curl` or the database, which is
most of what the original request was about.

### This is the third time, and the pattern is fine

| Spec | Endpoint | Client arrived in |
|---|---|---|
| 028 | retire | spec 032 |
| 029 | read one, correct the address | spec 030 |
| **033** | rename | **this** |

Each gap was filed rather than forgotten, and each was closed by a later spec.
Backend and frontend are separately specified, and bundling them would make
every backend feature wait on interface decisions. Named here so it reads as a
rhythm rather than a recurring oversight.

### It is more mechanical than the issue claims

Issue 1873 argued the operator-facing wording for a taken name was new work.
Checking the codebase says otherwise, twice:

- **The wording exists.** The overlay editor already refuses a taken name with
  *"That overlay name is already taken. Choose a different one."*, keyed on the
  code rather than the status, with a comment recording why.
- **The camera dialog may already handle it without changing.** Its refusal
  banner recognises two refusals and then deliberately falls back to the
  server's own explanation — *"because an unrecognised refusal is precisely
  where the server knows more than we do."* For a taken name the server says:
  *"Another camera in fab 'munich' is already called 'line-4-inlet'. Names are
  unique per fab, ignoring case."* That is actionable, names only the caller's
  own fab, and states the rule.

So the open question in the issue — whether a new shared refusal predicate is
needed — is answered **no**, and FR-008 requires that answer be *tested* rather
than assumed.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Correct a misnamed camera (Priority: P1)

An operator sees a camera registered as `line-3-inlet` that is on line 4. They
correct the name from the camera's page. It is the same camera afterwards.

**Why this priority**: The entire feature.

**Independent Test**: Open a camera, rename it, and see the new name — without
touching the database or an API client.

**Acceptance Scenarios**:

1. **Given** an active camera's page, **When** the operator renames it, **Then**
   the page shows the new name without a full reload.
2. **Given** a renamed camera, **When** the listing is opened, **Then** the row
   shows the new name.
3. **Given** a renamed camera, **When** its own address is reopened, **Then** it
   is the same camera — same identifier, same history.
4. **Given** the rename dialog, **When** it opens, **Then** it is pre-filled with
   the camera's current name, so a correction is an edit rather than a retype.

---

### User Story 2 - Three refusals, three different answers (Priority: P1)

A rename can be refused three ways, and the operator is told which.

**Why this priority**: Also P1. Two of the three are actively harmful if
confused — a taken name told as a lost update sends the operator to reload
something that will not change.

**Independent Test**: Provoke each refusal and read the rendered text.

**Acceptance Scenarios**:

1. **Given** a name another active camera in the fab holds, **When** the operator
   submits it, **Then** they are told the name is taken and to choose another.
2. **Given** a camera someone else changed since it was read, **When** the
   operator submits a rename, **Then** they are told to reload — not to choose a
   different name.
3. **Given** a camera retired since the page loaded, **When** the operator
   submits a rename, **Then** they are told it is retired and cannot be changed.
4. **Given** any of the three, **When** the operator reads the message, **Then**
   it does not offer either of the other two remedies.

---

### User Story 3 - The controls tell the truth about what is possible (Priority: P2)

A retired camera offers no rename, and a camera the operator may not see is
indistinguishable from one that does not exist.

**Why this priority**: P2 because it constrains rather than delivers — but the
second half is a security property three earlier specs depend on.

**Independent Test**: Open a retired camera and look for the control. Open
another fab's camera and compare the whole rendering to a never-registered one.

**Acceptance Scenarios**:

1. **Given** a retired camera's page, **When** the operator looks for a way to
   rename it, **Then** there is none — the control is **absent**.
2. **Given** an active camera's page, **Then** the control **is** offered.
3. **Given** a camera in a fab the operator does not hold, **When** they open its
   address, **Then** they see exactly what a never-registered identifier
   produces — including the absence of the rename control.

---

### Edge Cases

- **A correction that changes only letter case.** `Line-4-Inlet` to
  `line-4-inlet` is a real change to what an operator reads, and the two
  normalise identically. It MUST reach the server (FR-010) — spec 033 found this
  same trap in three separate layers, and a client that trims or normalises
  before sending would make it a fourth.
- **Submitting the name the camera already has.** Succeeds and changes nothing;
  the server treats it as a no-op.
- **The dialog is open when someone else renames the camera.** The submission is
  refused on the version, and the operator is told to reload.
- **The camera is retired while the dialog is open.** Refused as retired, which
  is a different answer from both of the above.
- **A name that is not usable at all** — empty, or too long. Refused as invalid,
  distinguishably from being taken.
- **The operator's typing when a rename is refused.** Kept. A refusal must not
  cost them their input.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to correct a camera's name from that
  camera's page, without database or API-client access.
- **FR-002**: The rename MUST be offered **separately** from the
  address correction, not as a second field on one form. The two are applied
  under separate version checks and cannot be submitted together (see
  Assumptions).
- **FR-003**: The rename control MUST be pre-filled with the camera's current
  name.
- **FR-004**: The rename MUST carry the version the operator was shown, so a
  camera changed since they read it is refused rather than overwritten.
- **FR-005**: A **taken name** MUST be reported in words that say to choose a
  different one.
- **FR-006**: A taken name MUST NOT be reported as a lost update. It MUST NOT
  tell the operator to reload to see another version, because nobody changed
  their camera and reloading will not release the name.
- **FR-007**: A **stale version** MUST still be reported as a lost update,
  telling the operator to reload — and MUST NOT tell them to choose a different
  name.
- **FR-008**: The three refusals MUST be distinguishable in the rendered text.
  Satisfying this MUST NOT require a new shared refusal predicate unless the
  spec's assumption proves wrong; **a test MUST establish which**.
- **FR-009**: Once a camera is retired the rename control MUST be **absent** from
  its page — not present-and-disabled, and not present-and-failing.
- **FR-010**: The name the operator typed MUST be sent as typed. It MUST NOT be
  case-normalised, and MUST NOT be silently altered beyond removing surrounding
  whitespace.
- **FR-011**: A refused rename MUST leave the operator's typing intact.
- **FR-012**: After a successful rename the page MUST show the new name without
  a full reload, and the listing MUST show it too.
- **FR-013**: A camera in a fab the operator does not hold MUST be reported
  exactly as one that does not exist — same rendered output, with the rename
  control absent in both.
- **FR-014**: The rename MUST NOT provide any path to changing the camera's fab
  or identifier.

### Key Entities

- **Camera** — already modelled. This feature reads its **name** (to pre-fill),
  its **version** (to send) and its **status** (to decide whether the control
  exists), and changes only its name, by asking the existing endpoint to.

---

## Success Criteria *(mandatory)*

- **SC-001**: An operator can correct a misnamed camera using only the
  management app — **zero** database or API-client access. Today it is one; it
  must be zero.
- **SC-002**: Each of the three refusals renders text that names its own remedy
  and **neither** of the other two, verified by reading all three renderings —
  not by observing that each showed an error.
- **SC-003**: A correction differing only in letter case reaches the server and
  is stored, verified end to end.
- **SC-004**: A camera another operator may not see renders **identically** to a
  never-registered identifier, compared as whole output rather than by status.
- **SC-005**: An end-to-end test renames a camera through the app and observes
  the new name — in the page and in the listing — driving the application rather
  than the API.
- **SC-006**: No new shared refusal predicate is added, **or** the spec records
  why one proved necessary.

---

## Assumptions

- **A separate dialog, because the endpoint takes one change at a time.** The
  correction endpoint accepts either an address or a name per request, each
  applied under its own version, so a combined form would have to make two
  requests and reconcile two version checks — and would refuse a version that
  its own first request had just advanced. One dialog per change matches what
  the server actually does.

- **The existing refusal handling already covers a taken name**, because the
  camera dialog deliberately falls back to the server's explanation for
  refusals it does not recognise, and the server's explanation here is a good
  one. FR-008 requires this be **tested** rather than trusted: if it turns out
  the fall-back reads badly, the fix is wording at the call site, following the
  overlay editor's precedent, not a fourth shared predicate.

- **Success needs no announcement**, and for a different reason than
  retirement's. Spec 032 forbade claiming authorship because retiring is
  idempotent and succeeds whether or not the operator caused it. A rename is
  version-checked, so a success genuinely *is* this operator's change — but
  there is still nothing to announce that the changed name on the page does not
  already say. The absence of a message is the same; the reasoning is not, and
  conflating them would be a rule applied without its reason.

- **No backend change.** The endpoint exists, is version-checked, and refuses a
  retired camera and a taken name with distinct codes. If a change proves
  necessary that contradicts spec 033's contract and is a **finding to raise**.

---

## Out of Scope

- **Renaming rules or variables.** ADR-0120 rules them out: their names are
  their addresses, so renaming one is an identity change rather than an edit.
- **Changing a camera's fab.** Forbidden, not deferred, and FR-014 keeps it so.
- **Bulk rename.** One camera per correction.
- **Renaming from the listing.** The detail page is where the operator has
  already established which camera they mean.
- **Any backend change.**
