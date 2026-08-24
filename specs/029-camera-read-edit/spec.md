# Feature Specification: Read a single camera, and correct one

**Feature Branch**: `029-camera-read-edit`

**Created**: 2026-08-24

**Status**: Draft

**Input**: Issue #1435 — "Cameras cannot be read individually or edited: CameraCatalog has only register and list". Spec 028 delivered the retire third of that issue's sibling (#1433); this is the read-one and edit halves.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask about one camera without asking about all of them (Priority: P1)

An operator opens a single camera in the management app — to check which address it pulls from, when it was registered, or whether it is still in service. Today the app cannot ask that question. It fetches the entire catalogue for the operator's fabs and picks the row out client-side, because a single-camera read does not exist.

**Why this priority**: It is the whole of the issue's first half, it is the cheapest of the three stories, and every other story here needs it. At the 250-camera target (constitution §Scale) answering a one-row question costs a full-catalogue page today, and that cost falls on the most common interaction in the app.

**Independent Test**: Register a camera, then ask for that one camera and get its details back. Ships alone and is immediately useful — the app can stop over-fetching before anything becomes editable.

**Acceptance Scenarios**:

1. **Given** a camera registered in the operator's fab, **When** the operator asks for that camera, **Then** its fab, name, address, registration time, status and current version are returned.
2. **Given** a camera that has been retired, **When** the operator asks for it, **Then** it is returned with its retired status rather than reported missing — the record outlives the hardware (spec 028 FR-008).
3. **Given** an identifier that names no camera, **When** the operator asks for it, **Then** the request is refused as not found.

---

### User Story 2 - Correct a camera that was recorded wrongly (Priority: P2)

A camera's address changes or was wrong from the start — a subnet renumbering, a replaced NVR, a typo at registration. Today there is no repair path: the address cannot be changed, and until spec 028 the row could not even be retired, so the catalogue accumulated entries describing nothing reachable.

**Why this priority**: It is the story with a real failure behind it, but it depends on US1 both for the version a caller must echo and for a way to confirm the result. Retiring and re-registering is a workaround that exists today; over-fetching to read one camera has no workaround at all.

**Independent Test**: Register a camera, correct its address, then read it back and see the new address and a changed version. Requires US1.

**Acceptance Scenarios**:

1. **Given** a camera in the operator's fab and the version from reading it, **When** the operator submits a corrected address with that version, **Then** the change is stored and the camera's version advances.
2. **Given** a camera that another operator has changed since it was read, **When** the operator submits a change quoting the now-stale version, **Then** the change is refused as a conflict and nothing is stored.
3. **Given** a camera in the operator's fab, **When** the operator submits a change without quoting any version, **Then** the change is refused — a blind write is not an accident this system absorbs (ADR-0113).
4. **Given** a retired camera, **When** the operator submits any change, **Then** the change is refused: retirement is terminal (spec 028 FR-001).
5. **Given** a change that would leave the camera with an unusable address, **When** it is submitted, **Then** it is refused and the stored address is unchanged.

---

### User Story 3 - Another plant's cameras stay invisible, not merely forbidden (Priority: P3)

Two fabs share one deployment. An operator in Dresden must not be able to learn anything about Munich's cameras — including whether a given camera exists at all.

**Why this priority**: A containment property rather than a capability, and it only becomes expressible once a single-camera read exists — which is exactly why spec 015 had to withdraw it. It is listed third because it is a constraint on US1 and US2 rather than a separate journey, but it carries its own test and its own value, and it is the reason a read-one endpoint is not simply a convenience.

**Independent Test**: Ask for a camera belonging to another fab, and ask for an identifier that never existed, then compare the two refusals field by field. Requires US1.

**Acceptance Scenarios**:

1. **Given** a camera registered in Munich, **When** a Dresden-only operator asks for it, **Then** the refusal is identical — same status and same body, compared field by field — to the refusal for an identifier that never existed.
2. **Given** a camera registered in Munich, **When** a Dresden-only operator submits a change to it, **Then** the refusal is likewise indistinguishable from one for a camera that does not exist.
3. **Given** a camera registered in Munich, **When** an operator holding both Munich and Dresden asks for it, **Then** it is returned.

---

### Edge Cases

- **A camera retired between the read and the change.** The caller holds a valid version for a camera that has since become terminal. Refused as terminal, not as a conflict — the caller's version is current; what changed is that the camera no longer accepts changes at all.
- **A change that alters nothing.** The submitted values equal the stored ones. Accepted as a success, and whether the version advances is settled in the contract; a no-op that reports failure would make retry logic wrong.
- **An operator holding several fabs.** A single-camera read need not choose a fab — the identifier already determines one, and the operator either holds it or does not. This is the asymmetry spec 015 established for reads (FR-005) reaching its natural conclusion.
- **An operator holding no fab at all.** Refused for lack of any fab, before the camera is looked up — the same order US2 requires generally.
- **A malformed identifier**, as opposed to a well-formed one naming nothing. Refused as a bad request, which is distinguishable from "not found" and safely so: it reveals nothing about what exists.
- **A camera whose fab the operator lost between reading and changing.** Refused as not found, matching US3 — authorisation is re-resolved per request and is never inherited from an earlier read.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to retrieve one camera by its identifier, receiving its fab, name, address, registration time, status, and the version needed to change it.
- **FR-002**: The single-camera read MUST return retired cameras as well as active ones, reporting the status of each. Retirement removes a camera from the default *listing* (spec 028 FR-007); it does not make its record unreadable.
- **FR-003**: An operator MUST be able to change a camera's address.
- **FR-004**: A change MUST be refused unless the caller quotes the version they read, and MUST be refused when that version is no longer current. No change is retried automatically on conflict (ADR-0113).
- **FR-005**: A change to a retired camera MUST be refused. Retirement is terminal (spec 028 FR-001), and a corrected address for hardware that is gone describes nothing.
- **FR-006**: A camera in a fab the caller does not hold MUST be reported **identically to a camera that does not exist** — the same status and the same body, verifiable field by field rather than by status alone. A camera's record carries its address, so a distinguishable refusal lets an operator enumerate another plant's cameras one request at a time. *(Reinstates spec 015's withdrawn FR-006, which was unimplementable without a single-camera read.)*
- **FR-007**: Both operations MUST resolve the caller's fab **before** any other precondition — version, existence, or validity — is evaluated. The reverse order answers a precondition failure to a request that was never the caller's to make, which is itself a disclosure.
- **FR-008**: A camera's fab MUST NOT be changeable. A stream's fab is its camera's (spec 016 FR-002) and a camera cannot move plants (spec 015 FR-004), so the guarantee is that the operation has no way to express it.
- **FR-009**: A camera's identifier MUST NOT be changeable, and its registration time and registering operator MUST NOT be changeable. They record what happened.
- **FR-010**: A rejected change MUST leave the camera exactly as it was. No partial application.
- **FR-011**: Changing a camera MUST be recorded in the audit trail, naming the operator, as registering and retiring are (spec 028 FR-010).
- **FR-012**: A camera's **name** MUST NOT be changeable by this feature. The change operation carries no name, so the refusal is that there is nothing to express rather than a validation that could be relaxed by accident. Tracked as #1850, which has to settle whether names are mutable *anywhere* in this product before Camera gets an exception.

### Key Entities

- **Camera**: gains no new state. It becomes readable individually and its address becomes changeable. Its identifier, fab, registration time and registering operator are immutable; its status is governed by spec 028.
- **Camera version**: already maintained per camera and already used to detect concurrent writes, but not currently visible to any caller. This feature is the first that must expose it, because a caller cannot quote what it cannot read.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator viewing one camera causes exactly one camera's data to be transferred, independent of how many cameras their fabs contain — at the 250-camera target, replacing a 250-row transfer with a 1-row one.
- **SC-002**: The management app no longer fetches the catalogue to answer a single-camera question; no code path remains that filters a full listing client-side to find one camera.
- **SC-003**: 100% of single-camera requests for another fab's cameras are refused, and the refusals are **byte-identical** to those for identifiers that never existed — asserted by field-by-field comparison, not by status code. *(Reinstates spec 015's withdrawn SC-003.)*
- **SC-004**: A camera whose address was recorded wrongly can be corrected without retiring it, without re-registering it, and without losing its identifier, its registration record, or its audit history.
- **SC-005**: Two operators changing the same camera concurrently result in exactly one stored change; the loser is told its version was stale, and no change is silently discarded.
- **SC-006**: Every stored change is attributable to an operator in the audit trail, with no gaps.

## Assumptions

- **Keyed by identifier, not by name.** Issue #1435 asked for name-keyed operations; it predates spec 028, which made a name reusable, so a name now identifies at most one *active* camera per fab but any number over time. A name-keyed read could not address a retired camera (FR-002) and a name-keyed change inherits the defect that got spec 015's `{name}/decommission` entry withdrawn. Verified rather than assumed on the cost side: the listing already returns each camera's identifier and the management app's own camera type already carries it, so nothing needs to start holding identifiers — it already does. **Recorded as a decision to overturn rather than a consensus to cite.**
- **Spec 015's withdrawn FR-010 is superseded, not reinstated.** It refused a name that resolved in two of the caller's fabs, naming the candidates. An identifier is never ambiguous, so with the decision above the requirement has nothing to describe. Should a name-keyed lookup ever land, FR-010 becomes live again and this supersession must be revisited — it is a consequence of the keying choice, not a judgement that the property does not matter.
- **A retired camera is readable but not changeable** (FR-002, FR-005). Terminal means terminal, and reading is not changing.
- **The version is carried the way this repo already carries it.** Three other contexts already require callers to echo a version to mutate, and a camera already keeps one; this feature follows that pattern rather than inventing a second (ADR-0113).
- **The change replaces the editable attributes it carries.** Partial-update semantics, where an omitted attribute means "leave alone", are not assumed; the contract pins this.
- **Renaming is out of scope (FR-012).** Decided rather than answered: the question was put to the user and the work was told to continue, so the recommendation was adopted and is recorded here as **a decision to overturn rather than a consensus to cite**. If it is overturned, FR-012 inverts and US2 grows a second acceptance path; nothing else in this spec moves.

  Two reasons for excluding it. First, the issue's motivation is entirely about the *address* — a subnet renumbering, a replaced NVR, a device re-addressed — and #1435's own acceptance criteria ask only for "at minimum the RTSP URL editable". Second, a rename is not one more editable field: it needs a per-fab case-insensitive uniqueness re-check (#1434), its own conflict answer distinct from the version conflict in FR-004, and a decision on whether a name freed by renaming is reusable the way one freed by retirement is (spec 028 FR-006). That is a second feature's worth of semantics attached to a one-line request shape.

  A misnamed camera is not stranded meanwhile: it can be retired and re-registered under the right name, which spec 028 made cheap and which now releases the wrong name for reuse. That workaround did not exist when #1435 was written, which is part of why the issue treated naming as urgent.

  Filed as **#1850** so the gap is tracked rather than implied — the same treatment #1435 itself got when spec 015 withdrew these endpoints. Writing it up turned up something that strengthens the exclusion: **no aggregate in this product supports renaming.** `Camera` has `Register`/`Retire`, `Rule` has `Create`/`Publish`/`Archive`, `Variable` has `Define`/`SetValue`/`Archive`. Names are immutable everywhere, and every context answers "this is named wrong" with create-new-and-archive-old. Adding a rename here would be the product's first, so it sets a convention rather than following one — which is an ADR-shaped decision, not something to settle inside a feature that is about correcting addresses.

- **No bulk change.** One camera per request. A fab-wide sweep has a different blast radius and is a different feature.
- **Retire (#1433) and the case-insensitivity defect (#1434) are out of scope.** Both are delivered — #1433 by spec 028, #1434 before it.
- **No new state, no migration expected.** Nothing here adds a column or a lifecycle value. If implementation finds a schema change is needed, that contradicts this assumption and is a finding for the planning phase, not a silent scope increase. *(Spec 028's research made the mirror-image mistake — it verified the schema and inferred that no code was needed — so the check to make here is not only "is the schema enough" but "is the schema the only place this rule lives".)*
