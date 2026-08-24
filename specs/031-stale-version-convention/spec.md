# Feature Specification: One way to say a version is stale

**Feature Branch**: `031-stale-version-convention`

**Created**: 2026-08-24

**Status**: Draft

**Input**: Issue #1857. Spec 030's Phase 0 research found that the refusal codes spec 029 shipped map to the wrong words through the shared frontend helper, and that both HTTP statuses involved are overloaded. Spec 030 shipped a workaround carrying a `Provisional, pending #1857` note in code three contexts share.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator is told to reload, never to retry (Priority: P1)

Two operators change the same thing. One wins. The other must be told their view was stale and that they should look again before reapplying — because trying again, unchanged, replays their edit over the winner's and quietly destroys it.

Today that advice is correct in six aggregates and wrong in the seventh, and the seventh is wrong in the way that costs the most: the operator is told to try again.

**Why this priority**: It is the whole point of the optimistic-concurrency scheme. A system that detects a lost update and then advises the action that causes one has spent the mechanism and kept the bug.

**Independent Test**: Provoke a stale-version refusal in every context that has one, and read what the operator is shown. Each must say re-read; none may say retry.

**Acceptance Scenarios**:

1. **Given** a stale version in any context, **When** the refusal reaches the operator, **Then** they are told the thing changed and to look again before reapplying.
2. **Given** a stale version in any context, **When** the refusal reaches the operator, **Then** they are **not** told to try again, whether by our wording or the server's.
3. **Given** a refusal that is *not* a stale version — a name collision, a validation failure — **When** it reaches the operator, **Then** they are not told to reload, which would send them somewhere useless.

---

### User Story 2 - A terminal refusal reads as terminal (Priority: P2)

Some things cannot be changed because they are finished — a retired camera, an archived revision. That is not a race, and telling the operator someone else got there first is simply false: nobody did, and reloading will not help.

**Why this priority**: Smaller than US1 and currently reachable in one context, but it is the reason status alone cannot carry the meaning — the terminal refusal and the lost update share a status today.

**Independent Test**: Provoke a terminal refusal and read the words. They must name the terminal state and must not describe someone else's edit.

**Acceptance Scenarios**:

1. **Given** a refusal because the thing is in a terminal state, **When** it reaches the operator, **Then** they are told what that state is.
2. **Given** the same refusal, **When** it reaches the operator, **Then** they are not told that someone else changed it, nor asked to reload.

---

### User Story 3 - The next context has one convention to follow (Priority: P3)

Someone adding optimistic concurrency to the eighth aggregate should find one answer to "how do I say the version was stale", not two that disagree and a shared helper that half-knows about both.

**Why this priority**: Nothing an operator sees, and the reason this is worth doing once rather than patching per context. The current state is not just inconsistent — it is a shared helper carrying a note saying it is provisional, which is a decision deferred rather than made.

**Independent Test**: Follow the recorded convention to add a stale refusal to a context that has none, and have it reach an operator correctly with no change to shared code.

**Acceptance Scenarios**:

1. **Given** the convention is recorded, **When** a new context adds a stale refusal following it, **Then** the operator gets the right advice with no change to the shared helper.
2. **Given** the convention is recorded, **When** someone reads the shared helper, **Then** it no longer describes itself as provisional.

---

### Edge Cases

- **A status that means several things.** Both statuses in play already carry other meanings — one also covers name collisions and terminal refusals, the other also covers preconditions that were wrong about existence. Anything keying on status alone will be wrong for one of them.
- **A refusal the system does not recognise.** It must still reach the operator with whatever the server said, rather than being flattened into a generic message — an unrecognised refusal is precisely where the server knows more than the client.
- **A context that changes its mind later.** The convention must be checkable, not merely documented, or the eighth aggregate rediscovers this issue.
- **Existing consumers.** Three contexts already depend on the current behaviour of the shared helper. Their operator-facing wording must not change as a side effect.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST have exactly one way to express "the version you held is no longer current", and every context that can refuse for that reason MUST use it.
- **FR-002**: A stale-version refusal MUST be recognisable to a client **without depending on the HTTP status**, because both statuses in use carry other meanings.
- **FR-003**: A refusal because something is in a terminal state MUST be distinguishable from a lost update, and MUST NOT inherit its wording.
- **FR-004**: The operator-facing advice for a lost update MUST be to re-read before reapplying, and MUST NOT be to retry — in every context, whether the words come from the server or the client.
- **FR-005**: Refusals the system does not recognise MUST still surface what the server said.
- **FR-006**: The existing operator-facing behaviour of the contexts that already refuse correctly MUST NOT change. This feature corrects an inconsistency; it must not be observable to anyone using those contexts today.
- **FR-007**: The convention MUST be recorded where a decision of this kind is recorded in this project, and MUST say which of the code or the status is authoritative and why.
- **FR-008**: The shared client helper MUST stop describing itself as provisional, because the thing it was waiting for will have happened.

### Key Entities

- **A refusal**: carries a machine-readable code and an HTTP status. This feature is about which of the two carries the meaning, and the answer must be one of them rather than both.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every context that can refuse a write for a stale version is recognised as such by the shared client, with a test per context — so a seventh, eighth or ninth cannot silently fall outside it.
- **SC-002**: No operator-facing message for a lost update contains an instruction to retry, in any context.
- **SC-003**: A terminal-state refusal and a lost-update refusal produce visibly different advice, and neither borrows the other's.
- **SC-004**: The contexts that behave correctly today behave identically afterwards — verifiable because their existing tests pass without modification.
- **SC-005**: The convention is written down as a decision, with its reasoning, rather than inferred from the code.

## Assumptions

- **The code is authoritative, not the status.** The shared helper's own comment already says so — *"anything that changes the advice has to key on the code rather than the status"* — and only half-applies it. Both statuses in play are overloaded in both directions, so neither can carry the meaning alone. **Recorded as a decision to overturn rather than a consensus to cite.**

- **The outlier changes, not the sixteen.** One context uses a different status and a differently-shaped code; six use the same status across **16 declaration sites**. Changing the sixteen is a breaking change to six contexts' contracts to earn nothing an operator can see; changing the one is a rename.

  This deliberately does **not** follow correctness alone. The outlier's status is the more correct one — RFC 9110 specifies it for a failed precondition — which is why the status is being made irrelevant to the advice rather than standardised. Both spellings stay legal; only the code has to conform.

- **This is a correction, not a feature.** Nothing an operator can do changes. What changes is that an existing mechanism stops giving one context's users advice that destroys their work.

- **Scope is the refusal vocabulary and the client that reads it.** Not the concurrency mechanism itself, which works: versions are compared, conflicts are detected, nothing is retried automatically (ADR-0113). This is about what the system *says* when that mechanism fires.

- **Depends on spec 030 landing first.** That feature added the provisional branch this replaces, in the same shared file. Sequencing them the other way would mean writing the workaround and removing it in the same breath.
