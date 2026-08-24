# Feature Specification: Open one camera, and fix it

**Feature Branch**: `030-camera-detail-view`

**Created**: 2026-08-24

**Status**: Draft

**Input**: Issue #1854 — "The single-camera read and edit endpoints have no client". Spec 029 built `GET /cameras/{camera}` and `PATCH /cameras/{camera}`; nothing calls either. This is the half spec 029 did not deliver, and the fifth acceptance criterion of #1435.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open one camera (Priority: P1)

An operator picks a camera out of the list and looks at it: which address it pulls from, when it was registered, whether it is still in service. Today the list is all there is — a row of columns, and a live-video panel that already has everything it needs handed to it. There is no way to open a camera.

**Why this priority**: It is the whole of the issue, and the other two stories are things you do *once a camera is open*. It is also what makes spec 029's read endpoint reachable by anyone other than curl.

**Independent Test**: Open a camera from the list, see its details. Ships alone and is immediately useful — an operator can answer "what address is this on?" without a database or an API client.

**Acceptance Scenarios**:

1. **Given** a list of cameras, **When** the operator opens one, **Then** its fab, name, address, registration time and status are shown.
2. **Given** an open camera, **When** the operator copies the location from the address bar and opens it again later, **Then** the same camera opens directly.
3. **Given** an open camera, **When** the operator goes back, **Then** they return to the list they came from rather than out of the application.
4. **Given** an identifier that names no camera the operator may see, **When** it is opened, **Then** the app says so plainly rather than showing an empty shell or a raw error.

---

### User Story 2 - Fix a camera that is on the wrong address (Priority: P2)

A camera's address changed or was wrong from the start. Spec 029 made it correctable; nothing in the app can correct it, so the repair path today is curl or the database.

**Why this priority**: It is the operator-visible payoff of spec 029, and it depends on US1 both for somewhere to put the control and for the version the correction has to quote.

**Independent Test**: Open a camera, correct its address, see the new address without reloading the page. Requires US1.

**Acceptance Scenarios**:

1. **Given** an open camera, **When** the operator submits a corrected address, **Then** it is saved and what is shown updates.
2. **Given** an address that is not usable, **When** the operator submits it, **Then** they are told before anything is sent, and nothing is sent.
3. **Given** another operator changed the same camera since this one opened it, **When** the correction is submitted, **Then** the operator is told the camera changed underneath them and offered the current state — not shown a status code.
4. **Given** the correction fails for any reason, **When** the operator looks at the camera, **Then** what is shown is what is stored, never the value they typed.

---

### User Story 3 - A retired camera opens honestly (Priority: P3)

Retired cameras leave the default listing but their records outlive them, and spec 029 made them readable on purpose — the audit trail refers to them.

**Why this priority**: Smaller than the other two and only reachable deliberately, but it is where the app is most likely to lie: a retired camera that offers an edit control and then fails on submit is worse than one that never offered it.

**Independent Test**: Open a retired camera. It opens, it says it is retired, and it does not offer to edit.

**Acceptance Scenarios**:

1. **Given** a retired camera, **When** it is opened, **Then** it is shown with its retired status made obvious.
2. **Given** an open retired camera, **When** the operator looks for the edit control, **Then** there is none — the refusal is visible before the attempt, not after it.

---

### Edge Cases

- **The camera is retired while open.** The next correction fails as terminal. The operator is told the camera was retired, not given a raw conflict.
- **A camera in a fab the operator does not hold**, opened by pasting a location. Indistinguishable from one that does not exist — spec 029 FR-006 made the API answer that way, and the app must not undo it by saying "not yours".
- **The operator's session expires while a camera is open.** The existing session handling applies; a correction must not be silently lost.
- **A correction submitted twice** — double-clicked, or retried. The second is either prevented or harmless; it must not produce two audit entries or an error the operator has to interpret.
- **The list is filtered or paged** when a camera is opened and the operator returns. They come back to what they were looking at, not to page one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to open a single camera from the camera list and see its fab, name, address, registration time and status.
- **FR-002**: An open camera MUST have its own location, so it can be linked to, bookmarked, reloaded, and returned from with the browser's back control.
- **FR-003**: Opening one camera MUST NOT require retrieving the whole catalogue.
- **FR-004**: An operator MUST be able to correct an open camera's address, and MUST see the stored result rather than what they typed.
- **FR-005**: A correction MUST carry the version the operator was shown, so a camera changed underneath them is refused rather than overwritten.
- **FR-006**: A refused correction MUST be explained in terms an operator can act on — *the camera changed, here is the current state*; *this camera is retired* — never a bare status code or an untranslated error body.
- **FR-007**: A retired camera MUST open, MUST be visibly marked as retired, and MUST NOT offer an edit control.
- **FR-008**: A camera the operator may not see MUST be reported exactly as one that does not exist. The app MUST NOT add a distinction the API deliberately withholds (spec 029 FR-006).
- **FR-009**: An address the operator has typed MUST be validated before it is sent, using the same rules the API enforces, so a predictable rejection does not need a round trip.
- **FR-010**: The name MUST NOT be presented as editable. It is not editable by the API (spec 029 FR-012, tracked as #1850), and offering it would fail on submit.
- **FR-011**: The camera list MUST remain usable as it is today; opening a camera is an addition to it, not a replacement.

### Key Entities

- **Camera (as shown)**: identifier, fab, name, address, registration time, status, and the version a correction must quote. All of it already on the wire from spec 029.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can go from the camera list to one camera's details in a single action.
- **SC-002**: Opening one camera transfers one camera's data, independent of how many cameras the operator's fabs contain.
- **SC-003**: An operator can correct a wrong address entirely within the application — no database access, no API client, no assistance.
- **SC-004**: A camera opened by location resolves to the same camera for any operator entitled to see it, and to the same "no such camera" for anyone else.
- **SC-005**: When two operators correct the same camera at once, exactly one correction is stored and the other operator is told what happened in words, without losing what they typed.
- **SC-006**: No refusal reaches an operator as a status code, a stack trace, or an untranslated error body.

## Assumptions

- **The detail view gets a real location, and the shell gets a router.** FR-002 needs one, and the cost is far lower than it looks: `react-router-dom` is **already a dependency of this app** and **already in use in kiosk-web** (`createBrowserRouter`, `RouterProvider`, `useNavigate`, `useParams`), so this follows an in-repo pattern rather than setting one.

  The shell also asked for this itself. Its comment reads *"A real router lands when more than three surfaces exist; for spec 004 we toggle between cameras, layouts, and overlays"* — there are now **six** surfaces, so the stated trigger passed two surfaces ago and the hand-rolled `useState` toggle stayed.

- **The whole shell converts, not just the cameras surface.** One routed surface beside five toggled ones is worse than either — the back button would work on one page and not the others, which is harder to explain to an operator than no back button at all. Converting six nav buttons to six routes leaves the pages themselves untouched.

  **Recorded as a decision to overturn rather than a consensus to cite.** The alternative — route only the cameras surface — is smaller and defensible if the shell conversion is judged too wide for this feature; FR-002 survives either way, and only the coherence argument is lost.

- **This is a frontend feature.** Spec 029 left the wire in the right shape: the read returns the version and status, every listing row already carries a version so a correction can be made without a read-one round trip, and retired cameras are readable. **If a backend change turns out to be needed, that contradicts spec 029's contract and is a finding to raise, not absorb.**
- **Live video is untouched.** The existing viewer panel keeps its job. A camera's details and its picture are different questions, and merging them widens this feature into a redesign.
- **Retiring from the app is out of scope.** The retire endpoint has been unused since spec 028 and deserves its own issue rather than being folded in here quietly — the same treatment #1854 gave this gap.
- **Renaming is out of scope**, because the API does not offer it (#1850).
- **No new API surface, so no new authorisation.** The app calls two endpoints spec 029 already shipped, under scopes an operator already holds.
