# Feature Specification: A name is mutable exactly when it is not an address

**Feature Branch**: `033-rename-convention`
**Created**: 2026-08-24
**Status**: Draft
**Input**: Issue #1850 — "A camera cannot be renamed — and nothing in the product can, so this is a convention decision before it is a feature"

---

## Why this exists

A camera's name cannot be corrected. Spec 029 made the *address* correctable and
deliberately left the name alone (its FR-012), filed as #1850 so the deferral was
tracked rather than implied.

A typo in the name is at least as likely as one in the address — the name is the
human-authored field, the address is usually copied off a device. The workaround
since spec 028 is to retire the misnamed camera and register a replacement under
the corrected name, which works and is cheap. **What it costs is the
identifier.** Registration record, audit history and every external reference to
the old identifier stay pointed at the retired row, so one physical camera's
history is split across two records. For a typo caught minutes later that is
nothing; for a name corrected a year in it is the difference between one
camera's history and two fragments.

### This is a convention decision before it is a feature

No aggregate in this product supports renaming. #1850 reads that as evidence
that names are immutable by convention. **The evidence says something different,
and sharper.**

---

## The finding that decides it

How each aggregate is **addressed**, and who **references** its name:

| Aggregate | API address | Name referenced by | Renaming it |
|---|---|---|---|
| **Camera** | `{camera:guid}` — identifier | Layouts, by `CameraIdentifier` (a Guid) | breaks nothing |
| **Layout** | `{layoutIdentifier:guid}` — identifier | — | breaks nothing |
| **Overlay** | `{overlayIdentifier:guid}` — identifier | — | breaks nothing |
| **Rule** | **`{name}` — the name is the address** | nothing outside Automation | breaks saved links |
| **Variable** | **`{name}` — the name is the address** | **Automation, by name** | **breaks rules, silently** |

So renaming is not one thing:

- Where the name is an **attribute**, changing it is an ordinary edit.
- Where the name is the **address**, changing it is an identity change — every
  existing reference to the old name stops resolving.

**Variable is the sharp case.** `RuleAction.SetVariableValue` carries a
`VariableName` string, persisted with the rule and read at evaluation time. That
reference crosses a bounded-context boundary, where ADR-0016 forbids a project
reference — so there is no referential integrity, and nothing in the system could
detect the break. A renamed variable would leave rules that silently stop
working, with no error anywhere.

**Camera is the safe case, and it is safe for a structural reason** — spec 028
and spec 029 keyed its endpoints on the identifier precisely because names are
reusable. That decision, made for a different purpose, is what makes a camera
rename an ordinary edit.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Correct a misnamed camera without losing its history (Priority: P1)

An operator notices a camera was registered as `line-3-inlet` when it is on
line 4. They correct the name. It is the same camera afterwards — same
identifier, same audit trail, same registration record.

**Why this priority**: The whole point. Correcting the name by retire-and-
re-register already works; what it cannot do is keep the identifier.

**Independent Test**: Register a camera, rename it, and confirm its identifier
and registration timestamp are unchanged and its history is continuous.

**Acceptance Scenarios**:

1. **Given** an active camera, **When** the operator renames it, **Then** the
   new name is stored and the identifier is unchanged.
2. **Given** a camera was renamed, **When** its record is read, **Then** its
   registration record and prior history are still its own — not a second
   record's.
3. **Given** a camera was renamed, **When** the audit trail is read, **Then**
   the rename appears, naming the operator who made it.
4. **Given** a camera was renamed, **When** the operator renames it to the same
   name again, **Then** the request succeeds and records no new change.

---

### User Story 2 - A rename cannot take a name that is in use (Priority: P1)

Renaming to a name another active camera in the same fab already holds is
refused, and the refusal says which problem it is.

**Why this priority**: Also P1. A rename that can duplicate a name breaks the
uniqueness rule the whole catalogue rests on, and does it through the one path
nobody has tested.

**Independent Test**: Register two cameras in a fab, rename one to the other's
name in different letter case, and confirm the refusal — then confirm the
refusal is distinguishable from a stale-version refusal.

**Acceptance Scenarios**:

1. **Given** two active cameras in a fab, **When** one is renamed to the
   other's name, **Then** the rename is refused and nothing changes.
2. **Given** the same, **When** the new name differs only in letter case,
   **Then** it is still refused.
3. **Given** a camera in another fab holds the name, **When** the rename is
   attempted, **Then** it **succeeds** — uniqueness is per fab.
4. **Given** a retired camera holds the name, **When** the rename is attempted,
   **Then** it **succeeds** — retirement releases the name.
5. **Given** a refusal for a taken name, **When** a caller inspects it, **Then**
   it is distinguishable from a refusal for a stale version, so the caller can
   tell whether re-reading and retrying would help.

---

### User Story 3 - The name a rename frees becomes usable (Priority: P2)

Renaming `line-3-inlet` to `line-4-inlet` frees `line-3-inlet` for immediate
reuse in that fab.

**Why this priority**: P2 because it follows from the uniqueness rule rather
than adding capability — but it is asserted rather than assumed, because it is
currently a side effect of an index predicate and nobody has chosen it.

**Independent Test**: Rename a camera, then register a new camera under its old
name in the same fab.

**Acceptance Scenarios**:

1. **Given** a camera renamed away from `line-3-inlet`, **When** a new camera is
   registered as `line-3-inlet` in that fab, **Then** it succeeds.
2. **Given** the same, **When** the original camera is read, **Then** it carries
   only its new name.

---

### Edge Cases

- **Renaming a retired camera** is refused. Retirement is terminal (spec 028)
  and spec 029 FR-005 already refuses every change to a retired camera; renaming
  hardware that no longer exists changes nothing but the historical record.
- **Renaming to the current name** succeeds and records nothing — the same
  idempotency-as-no-event spec 029 chose for the address.
- **Two operators renaming the same camera at once.** The second is refused on
  the version, not the name, and the refusal says which.
- **Renaming to a name freed by the same request** — i.e. a swap between two
  cameras — is **not** supported as an atomic operation. Each rename is
  independent, so a direct swap requires a third name in between.
- **A name that is invalid rather than taken** is refused as invalid, and
  distinguishably so.

---

## Requirements *(mandatory)*

### The convention

- **FR-001**: The product MUST record a decision, as an ADR, stating when an
  aggregate's name may be changed. The rule MUST be **one sentence and
  checkable**: *a name may be changed only where the aggregate is not addressed
  by it.*
- **FR-002**: The ADR MUST state the ruling for **all five** aggregates —
  `Camera`, `Layout`, `Overlay`, `Rule`, `Variable` — not `Camera` alone, and
  MUST give the reason the line falls where it does.
- **FR-003**: The ADR MUST record why `Variable` is the sharpest exclusion:
  `Automation` references a variable **by name**, across a context boundary that
  ADR-0016 forbids a project reference across, so a rename would break rules
  with nothing able to detect it.
- **FR-004**: The convention MUST be enforced by an automated check, not left to
  documentation. A future aggregate that addresses itself by name and also
  offers a rename MUST fail the build.

### The camera rename

- **FR-005**: An operator MUST be able to change an active camera's name, and
  the camera's identifier MUST NOT change.
- **FR-006**: A rename MUST be refused when the new name is held by another
  **active** camera in the **same fab**, compared **case-insensitively**.
- **FR-007**: That refusal MUST be enforced consistently by **every** layer that
  holds the uniqueness rule — the storage constraint and the application-level
  existence check both. Neither alone is sufficient.
- **FR-008**: A refusal for a name already in use MUST be distinguishable by the
  caller from a refusal for a stale version, so a caller can tell whether
  re-reading and retrying would help. It MUST NOT be reportable as a lost
  update.
- **FR-009**: A rename MUST be refused for a **retired** camera, consistent with
  retirement being terminal.
- **FR-010**: Renaming to the camera's current name MUST succeed and MUST record
  no change — idempotency as no event.
- **FR-011**: The name a rename **frees** MUST become immediately available for
  reuse within that fab. This is a chosen behaviour and MUST be tested as such,
  not inherited from a storage constraint's shape.
- **FR-012**: A rename MUST reach the audit trail naming the operator who made
  it, as registration, retirement and address correction already do.
- **FR-013**: A rename MUST NOT rewrite history. Records of what the camera was
  called at an earlier time MUST remain as they are.
- **FR-014**: A camera's **fab** MUST remain unchangeable. This feature MUST NOT
  provide a path to changing it, directly or as a side effect.
- **FR-015**: Spec 029's FR-012 MUST be updated to point at this feature rather
  than reading as a permanent exclusion.

### Key Entities

- **Camera** — gains one changeable attribute, its name. Identity, fab and
  registration record are unaffected.

---

## Success Criteria *(mandatory)*

- **SC-001**: An operator can correct a misnamed camera and the camera keeps its
  identifier — verified by comparing the identifier before and after. Today this
  is impossible; the workaround produces a **different** identifier.
- **SC-002**: After a correction, the camera has **one** history, not two.
  Verified by reading its record and its audit trail and finding registration,
  the rename, and any earlier changes on the same camera.
- **SC-003**: A rename that would duplicate an active name in the same fab is
  refused — verified for an exact match **and** for a case-only difference, and
  verified for **both** places the uniqueness rule lives.
- **SC-004**: A caller can tell a name collision from a stale version without
  guessing — verified by asserting the two refusals are not interchangeable.
- **SC-005**: The convention has an automated check that **fails** for a
  plausible violation a future context would introduce, demonstrated by
  introducing one and observing the failure.
- **SC-006**: The ruling for every one of the five aggregates is stated, so no
  future reader has to infer it from what happens to exist.

---

## Assumptions

- **Camera is renameable under the convention this spec adopts**, so this
  feature delivers both the ADR and the capability. Had the evidence gone the
  other way — had cameras been addressed by name, as rules and variables are —
  the honest outcome would have been an ADR alone with #1850 closed as
  *answered, not built*. It is recorded here because the spec was written
  prepared for that outcome rather than assuming this one.
- **Rules and variables are not renamed by this feature**, whatever the ADR
  says about them in principle. Their names are their addresses, so renaming
  them is a different and larger piece of work.
- **The uniqueness rule already exists and is already enforced in two places**,
  and spec 028 found those two disagreeing. A rename is a third caller of that
  rule, which is why FR-007 requires both rather than trusting either.
- **Retirement already releases a name for reuse** (spec 028), so US2's
  scenario 4 asserts existing behaviour survives rather than adding it.
- **No other context stores a camera's name.** Published announcements carry the
  name as a record of what it was at that moment, which is history and stays as
  it is (FR-013).

---

## Out of Scope

- **Renaming rules, variables, layouts or overlays.** The ADR rules on them;
  building any of it is separate. For rules and variables it is not a rename at
  all but an identity change with a migration story attached.
- **Changing a camera's fab.** Forbidden, not merely deferred (spec 015 FR-004,
  spec 029 FR-008), and FR-014 keeps it that way.
- **A user interface for renaming.** Whether the capability exists and how an
  operator reaches it are separate questions; this settles the first.
- **Atomically swapping two cameras' names.** Each rename stands alone.
- **Retrospectively correcting the name in past records.** FR-013 forbids it —
  the audit trail is what was true then.
