# Feature Specification: Losing the uniqueness race is a refusal, not a fault

**Feature Branch**: `034-uniqueness-refusal`
**Created**: 2026-08-25
**Status**: Draft
**Input**: Issue 1869 — "A unique-index violation reaches the caller as an unhandled 500"

---

## Why this exists

Every uniqueness rule in this product is enforced twice: an application-level
check that produces an answer an operator can act on, and a unique index that
guarantees the invariant. **The check and the write are not atomic.** Between
them, another writer can take the name.

When that happens the database refuses the write, nothing translates the
refusal, and the caller gets a **500**. They did nothing wrong: they asked for a
name that was free when they asked, and lost a race by milliseconds. What they
are told is that the server broke.

**Twelve unique indexes across nine contexts** are in this position —
AuditObservability, Automation, CameraCatalog, EventIngestion, Identity,
LayoutComposition (2), OverlayDesigner (2), StreamDistribution (2),
SystemVariables. It is not one context's quirk.

### The window is small, and that is the point

This is a **presentation** failure, not a correctness one. The invariant holds —
the database refused the write, which is exactly its job. Nobody's data is
wrong. What is wrong is the sentence the loser reads.

That is also why this is a small feature rather than a large one: it is not
closing a hole, it is finishing an answer.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Losing the race says so (Priority: P1)

Two operators register a camera under the same name at the same moment. One
succeeds. The other is told the name is taken and to choose another — not that
the server failed.

**Why this priority**: The whole feature. Everything else is a constraint on it.

**Independent Test**: Provoke a genuine collision at the database and observe
the response.

**Acceptance Scenarios**:

1. **Given** two writers creating the same uniquely-named thing concurrently,
   **When** both are answered, **Then** exactly one succeeds and the other is
   refused — and **neither** receives a server fault.
2. **Given** the refused writer, **When** they read the refusal, **Then** it
   tells them the name is already in use and that they need a different one.
3. **Given** the refused writer, **When** they retry the identical request,
   **Then** they are refused again — the refusal is honest that retrying alone
   will not help.

---

### User Story 2 - The refusal cannot be mistaken for a lost update (Priority: P1)

A caller who may also receive a stale-version refusal can tell the two apart
without guessing.

**Why this priority**: Also P1, and inseparable. Both are conflicts, and only
one is resolved by re-reading. A caller who conflates them re-reads and retries
forever against a name that belongs to somebody else.

**Independent Test**: Provoke both refusals against the same resource and
compare them.

**Acceptance Scenarios**:

1. **Given** a uniqueness refusal and a stale-version refusal, **When** a caller
   inspects both, **Then** they are distinguishable without reading prose.
2. **Given** the uniqueness refusal, **Then** nothing about it marks it as a
   lost update.
3. **Given** the uniqueness refusal, **Then** its wording does not tell the
   caller to re-read and reapply — that is the other refusal's advice and it
   does not work here.

---

### User Story 3 - The refusal tells the caller nothing else (Priority: P2)

The response says a name is taken. It does not say what the storage looks like,
and it does not confirm the existence of anything the caller cannot already see.

**Why this priority**: P2 because it constrains rather than delivers — but one
half of it is a security property several contexts already depend on.

**Independent Test**: Read the refusal and look for anything a caller could not
have learned by asking legitimately.

**Acceptance Scenarios**:

1. **Given** a uniqueness refusal, **When** it is read, **Then** it names no
   constraint, table, column or index.
2. **Given** a caller who cannot see a resource in another fab, **When** they
   provoke a uniqueness refusal, **Then** the refusal does not reveal that a
   colliding resource exists there.

---

### Edge Cases

- **The same writer retries immediately.** Refused again, identically. Nothing
  about the refusal implies waiting will help — that is the holder's decision,
  not a timeout.
- **A collision on a constraint that is an internal invariant rather than a
  name.** Not every unique index guards something an operator named; some
  guarantee a structural rule. Those must not be reported as "choose a different
  name", because there is nothing for the caller to choose.
- **A write that is not driven by a request at all.** Message-driven writes have
  no caller to answer, and their failure handling belongs to the message
  pipeline, not here.
- **A violation with no constraint information attached.** The refusal still has
  to be a refusal rather than a fault.
- **The application-level check is removed because "the database catches it
  now".** That is a regression, not a simplification — see FR-009.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A write refused by the storage layer for violating a uniqueness
  rule MUST be reported to the caller as a **refusal**, not as a server fault.
- **FR-002**: The refusal MUST be reported the same way for every uniqueness
  rule in the product, in every context.
- **FR-003**: The refusal MUST carry a machine-readable code that a caller can
  key on, distinct from every other refusal code in the product.
- **FR-004**: The code MUST NOT be one that identifies a **lost update**. This
  is not one: the caller's version is current, nobody changed their resource,
  and re-reading would show them exactly what they already had.
- **FR-005**: The refusal MUST be distinguishable from a stale-version refusal
  by something other than its prose, because a caller acts differently on each.
- **FR-006**: The refusal's wording MUST NOT advise re-reading and reapplying.
  That advice resolves a stale version and does nothing here.
- **FR-007**: The refusal MUST NOT disclose any storage detail — no constraint,
  index, table or column name — in anything a caller receives.
- **FR-008**: The refusal MUST NOT reveal the existence of a resource the caller
  is not otherwise permitted to know about.
- **FR-009**: Every existing application-level uniqueness check MUST remain.
  This feature adds a **backstop for a race**, not a replacement for a check.
  A context that dropped its check would lose the specific, actionable message
  it gives today and answer the generic one for **every** duplicate rather than
  for the rare race.
- **FR-010**: A violation carrying no identifying detail MUST still be reported
  as a refusal rather than falling through to a fault.
- **FR-011**: The refusal MUST be produced for request-driven writes. Writes with
  no caller to answer are out of scope (see Out of Scope).

### Key Entities

None. This feature adds no data and changes no stored shape. It converts one
failure into an answer.

---

## Success Criteria *(mandatory)*

- **SC-001**: A caller losing a uniqueness race receives a refusal naming what
  went wrong. Today they receive a server fault; the count of paths producing a
  fault for this cause must be **zero**.
- **SC-002**: Concurrent writers asking for the same name produce **exactly one
  success and no server faults**, however the race resolves.
- **SC-003**: A uniqueness refusal and a stale-version refusal differ in their
  machine-readable code, verified by comparing the two — not by reading them.
- **SC-004**: No response for this cause contains any storage identifier,
  verified by inspecting the response for the names of the twelve constraints.
- **SC-005**: Every application-level uniqueness check that exists today still
  exists and still produces its own specific message, verified by those
  contexts' own tests passing **unchanged**.

---

## Assumptions

- **The generic answer is the right one, because the specific answer already
  exists.** Every context with a user-facing uniqueness rule already refuses
  duplicates with its own code — `CAMERA_NAME_TAKEN`, `RULE_NAME_TAKEN`,
  `VARIABLE_NAME_TAKEN`, `LAYOUT_NAME_TAKEN`, `OVERLAY_NAME_TAKEN`,
  `WEBHOOK_CLIENT_ALREADY_EXISTS`. Those fire on the common path. The storage
  layer speaks only when one of them checked, was told the name was free, and
  lost the race in between.

  A per-constraint mapping would restate seven messages that already exist,
  in a place that has to know all nine contexts' vocabulary, maintained for
  twelve constraints — to improve wording on a path that fires in a race window.
  Recorded as an assumption rather than a conclusion because it is the spec's
  central judgement and the one most worth overturning if it is wrong.

- **The storage layer genuinely does not know what collided.** It knows a
  constraint was violated. Turning that into a domain sentence requires
  knowledge it does not have and should not acquire.

- **Not every unique index guards a name.** Some guarantee structural rules
  rather than operator-chosen values. The wording has to be true for both, which
  is a constraint on how specific it can be — and another argument against
  promising more than "something already exists".

- **Nothing about the invariant changes.** The database already refuses these
  writes correctly. This feature changes only what the caller is told.

---

## How this is tested, and why that is enough

Called out here rather than left to the plan, because the honest answer is
uncomfortable and would otherwise be discovered late.

**The path only fires when two writers interleave.** A test that reliably forces
that interleaving is either impossible or so contrived that it stops resembling
the thing it tests. So the evidence is in two parts, and neither is sufficient
alone:

1. **The mapping is proved directly.** Given a uniqueness violation, the
   response is the refusal this spec requires — its code, its status, its
   wording, and the absence of storage detail. This is deterministic and covers
   every requirement about *what the answer is*.

2. **The reachability is proved by an invariant, not by forcing the race.**
   Concurrent writers asking for the same name must produce exactly one success
   and **never a server fault** (SC-002). Whether the race actually fires on any
   given run is not asserted — only that no outcome is ever a fault.

**The accepted limitation**: part 2 may pass without exercising the new path at
all, on a run where the race does not occur. That is deliberate. A test that
demanded the race occur would fail intermittently for reasons unrelated to the
code, and a flaky test in this repository has already cost a merge. This one
cannot produce a **false green** — it can only fail to add information — which
is the property worth having.

---

## Out of Scope

- **Making the check and the write atomic.** They are not, by design. A lock
  held across the check and the write would cost more, on every request, than
  the race costs on the rare one that loses it.
- **Changing any application-level uniqueness check.** FR-009 requires they stay
  exactly as they are.
- **Changing any index, constraint or threshold.**
- **Writes with no caller.** Message-driven writes fail into the message
  pipeline, which has its own retry and dead-letter behaviour. Answering an
  absent caller is not a thing this feature can do.
- **Other storage failures.** Foreign keys, check constraints and deadlocks are
  each a different conversation with the caller. This feature is about
  uniqueness, and widening it would mean deciding all of them at once.
