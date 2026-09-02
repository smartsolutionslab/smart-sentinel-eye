# Feature Specification: Properties that travel together become one value object

**Feature Branch**: `058-properties-that-travel-together`

**Created**: 2026-09-02

**Status**: Draft

**Input**: User description: "Group properties that are always written together into per-context composite value objects, so an aggregate exposes one concept instead of two loose fields that can drift apart."

## Context

Spec 057 removed primitives from domain models one property at a time. It made
every property well-typed and left a second question untouched: **several of
those properties are not independent of each other**, and nothing in the model
says so.

A survey of every domain model found four such groups.

| Group | Sites | Today | What the pairing means |
|---|---|---|---|
| Timestamp + actor | 9 | `CreatedAt` + `CreatedBy`, `RegisteredAt` + `RegisteredBy`, `ProvisionedAt` + `ProvisionedBy` | Always written in the same statement; neither is meaningful alone |
| Actor + username | 1 | `AuditEvent.Actor` + `ActorUsername` | One identity, of which the username is the optional human-readable half — **not grouped; see below** |
| Payload + size | 1 | `AuditEvent.Payload` + `PayloadSizeBytes` | **Not a pair — a derivation.** See below |
| Trigger source + kind | 1 | `Rule.TriggerSource` + `TriggerKind` | Together they name one trigger; separately they name half of one — **not grouped; see below** |

The nine timestamp/actor sites are: `Rule`, `Layout`, `Layout.Revision`,
`Overlay`, `Overlay.Revision`, `Variable` (creation); `Camera`,
`RegisteredClient` (registration); `Stream` (provisioning).

**Two of the twelve were surveyed, attempted and then accepted ungrouped**
(2026-09-02, issue #2026, option C). `AuditEvent.Actor` + `ActorUsername` and
`Rule.TriggerSource` + `TriggerKind` each sit inside a composite index
alongside a column outside the pair — `ix_audit_actor_occurred` and
`ix_rules_fab_trigger_state`. Grouping either would move the pair into a
composite, and **EF cannot express an index that spans a composite and its
row**, by owned reference or complex type. The alternatives were to take the
index out of the EF model, which re-creates issue #2022's divergence trap on
purpose, or to change what these tables are indexed on, which is a
query-performance decision this feature had no standing to make.

**So this spec delivers ten of twelve, deliberately.** The two that remain are
the two where the database has an opinion, and the reasoning is recorded here
rather than left for the next reader to re-derive.

**Why this is worth doing at all.** Two fields that must agree, and that
nothing forces to agree, will eventually disagree. The aggregate's constructor
is the only place that currently knows they belong together, and that knowledge
is not expressed anywhere a reader or a compiler can see it. Grouping them puts
the invariant where it can be enforced once instead of remembered at every call
site.

**The payload group is different and must not be built like the others.**
`PayloadSizeBytes` is `Encoding.UTF8.GetByteCount` of `Payload` — a *function*
of the content, not an independent value that happens to accompany it. A
composite that accepted both would preserve exactly the defect worth removing:
today, nothing prevents a size that does not match its content. The composite
must compute the size from the content it is given.

**One asymmetry this feature must record and must not fix.** `PublishedAt` and
`ArchivedAt` carry **no actor anywhere in the codebase**. So a creation
composite will sit on the same aggregate as bare publish and archive
timestamps. That is a finding in its own right — this system records who
created a revision and not who published it — but inventing a publisher actor
as a side effect of a behaviour-preserving refactor would be a scope breach and
a schema change. It is recorded here and left alone.

## User Scenarios & Testing *(mandatory)*

The "users" of this feature are the engineers reading and changing these
aggregates, and the reviewers who have to spot when a rule is broken.

### User Story 1 — An aggregate names the concept, not the two fields (Priority: P1)

An engineer opens `Camera` and sees one `Registration`, not a `RegisteredAt`
beside a `RegisteredBy` that nothing connects. When they write a new aggregate
with the same shape, the type already exists in their context and they use it
rather than declaring the pair again.

**Why this priority**: It is the bulk of the work — nine of the twelve sites —
and the reason the feature exists. It is also the slice that can ship one
context at a time, so it can start delivering before the whole feature lands.

**Independent Test**: Take one context — StreamDistribution is the smallest,
one aggregate and one configuration — replace its pair with `Provisioning`, and
confirm the aggregate exposes one property, the database schema is unchanged,
and the existing tests pass untouched except where they name the property.

**Acceptance Scenarios**:

1. **Given** an aggregate with a timestamp/actor pair, **When** the composite
   replaces it, **Then** the aggregate exposes exactly one property for the two
   and no caller can set one without the other.
2. **Given** the same aggregate, **When** the schema is compared against the
   migration history, **Then** no change is pending — the columns the composite
   maps to are the columns the pair mapped to.
3. **Given** a context that already had covering tests for the pair, **When**
   the composite replaces it, **Then** those tests still pass, adjusted only
   where they name the property.

---

### User Story 2 — A payload cannot disagree with its own size (Priority: P1)

An audit row carries its content and a byte count. Today those are two
independently-set fields and nothing checks that the second describes the
first. After this change the count is derived from the content when the value
is built, and there is no way to supply a size that does not match.

**Why this priority**: It is the only slice that closes a real defect class
rather than improving how the model reads, and it is one of the smallest. It is
independent of Story 1 and can ship before or after it.

**Independent Test**: Construct the composite from a payload containing
multi-byte characters and confirm the size is the UTF-8 byte count, not the
character count; confirm there is no way to construct one with a mismatched
size.

**Acceptance Scenarios**:

1. **Given** payload content, **When** a stored payload is built from it,
   **Then** its size is that content's UTF-8 byte count.
2. **Given** an audit row read back from storage, **When** its stored payload
   is inspected, **Then** content and size agree, as they did before the
   change.
3. **Given** the audit write path, **When** a row is written, **Then** the same
   two columns receive the same two values they received before.

---

### User Story 3 — An actor is one thing, with an optional name (Priority: P2) — NOT BUILT

> **Accepted ungrouped on 2026-09-02** (issue #2026, option C).
> `ix_audit_actor_occurred` spans the composite and the row, and EF cannot
> express that index. The story is kept as written because the reasoning below
> is still the reason someone would want it — what changed is the cost, not the
> value.

An audit row identifies who acted. Some actors are the system itself and have
no username; the rest have one. Today that is an identifier property beside a
nullable username property, and the relationship between them — that the
username belongs to the identifier and never stands alone — is not expressed.

**Why this priority**: Smaller value than Stories 1 and 2, and it touches the
audit write path, which is hand-written SQL rather than the mapping used
everywhere else. Worth doing, worth doing after.

**Independent Test**: Build an actor with and without a username, and confirm
the system actor is recognisable as such in both the composite and the audit
projection.

**Acceptance Scenarios**:

1. **Given** an actor with a username, **When** the composite is built,
   **Then** both parts are carried together and the username cannot be set
   without the identifier.
2. **Given** the system actor, **When** the composite is built, **Then** it has
   no username and is still recognisable as the system.
3. **Given** an existing audit row, **When** it is read after the change,
   **Then** it projects to the same outward shape it projected to before.

---

### User Story 4 — A trigger is one thing (Priority: P3) — NOT BUILT

> **Accepted ungrouped on 2026-09-02** (issue #2026, option C), for the same
> reason as User Story 3: `ix_rules_fab_trigger_state` spans the composite and
> the row.

A rule fires on a trigger. The trigger has a source and a kind, and neither
half describes a trigger on its own.

**Why this priority**: One site, no defect behind it, and the smallest reader
benefit of the four. It is included because leaving one known pair ungrouped
while grouping the others makes the rule look like a matter of taste.

**Independent Test**: Replace the pair on `Rule` and confirm rule evaluation,
persistence and projection are unchanged.

**Acceptance Scenarios**:

1. **Given** a rule, **When** the composite replaces the pair, **Then** the
   rule exposes one trigger and evaluation behaviour is unchanged.

---

### Edge Cases

- **A composite whose parts are optional.** The audit username is optional and
  its identifier is not. The composite must express that asymmetry rather than
  becoming wholly optional itself.
- **An aggregate whose rows are written directly rather than through the path
  every other context shares.** The audit context both writes and archives its
  rows that way. Both read the properties directly and both must change with
  them.
- **A composite inside an owned collection.** Two of the nine sites are
  revisions, which are already owned collections inside their aggregate. A
  composite nests one level deeper than the others.
- **Reading rows written before the change.** Every row in every table predates
  this feature. The composite must materialise from exactly the columns those
  rows already have.
- **A pair that looks like the others and is not.** The payload group is a
  derivation; the publish and archive timestamps have no actor at all. Neither
  should be forced into the shape of the nine.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each of the four groups MUST be exposed by its aggregate as a
  single property, replacing the loose properties it is built from.
- **FR-002**: Composite types MUST be declared in the bounded context that uses
  them. A shared or generic composite MUST NOT be introduced, and no context
  may reference another context's type.
- **FR-003**: Each context MUST keep its own distinct timestamp types. This
  feature MUST NOT collapse them into a common one.
- **FR-004**: The database schema MUST NOT change. Each composite MUST be
  stored in the same columns, with the same names and nullability, as the
  properties it replaces.
- **FR-005**: The stored-payload composite MUST derive its size from its
  content. It MUST NOT accept a size from a caller.
- **FR-006**: ~~The actor composite MUST require an identifier and permit the
  username to be absent, and MUST continue to distinguish the system actor.~~
  **Withdrawn 2026-09-02** (issue #2026, option C). The actor pair stays two
  properties; `ix_audit_actor_occurred` cannot survive the composite. The same
  withdrawal applies to the trigger pair, which FR-001 no longer covers.
- **FR-007**: Every write path that sets these properties MUST be updated with
  them. The audit context has one that writes its columns directly rather than
  through the path the other contexts share, and it is in scope.
- **FR-008**: Outward-facing shapes — HTTP responses, integration events,
  archived projections — MUST be unchanged. This feature is not visible outside
  the domain models.
- **FR-009**: The work MUST be behaviour-preserving. Covering tests MUST exist
  and pass before each change and continue to pass after it.
- **FR-010**: The absence of an actor on publish and archive MUST be recorded
  as a finding. This feature MUST NOT add one.

### Key Entities

- **Creation**: when a thing was created and by whom. Declared separately in
  Automation, LayoutComposition, OverlayDesigner and SystemVariables.
- **Registration**: when a thing was registered and by whom. Declared
  separately in CameraCatalog and Identity.
- **Provisioning**: when a stream was provisioned and by whom. Declared in
  StreamDistribution.
- ~~**Audit actor**: who acted, and their username where there is one.~~ Not
  built — see Context.
- **Stored payload**: an audit row's captured content together with the size
  derived from it.
- ~~**Trigger**: the source and kind a rule fires on.~~ Not built — see Context.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Ten** of the twelve pairs become ten single properties — 20
  loose properties reduced to 10. The two the survey identified and this spec
  does not group are named in Context, with the reason; no *other* property it
  found is left ungrouped.

  *Corrected from "twelve … reduced to 12" on 2026-09-02. The original count
  was written before anyone checked whether the indexes survive a composite —
  Phase 0 proved only that the columns do.*
- **SC-002**: No database schema change is pending in any context after the
  work, verified the same way it is verified today, and no migration is added
  by this feature.
- **SC-003**: Every existing test passes without weakening an assertion. Tests
  change only where they name a property.
- **SC-004**: It is impossible to construct any of the four composites in an
  inconsistent state — one half without the other, or a size that does not
  match its content — and a test demonstrates each refusal.
- **SC-005**: Outward-facing shapes are byte-identical before and after, so no
  consumer of the HTTP API, the integration events or the archive can tell the
  change happened.
- **SC-006**: Each of the nine timestamp/actor sites can be delivered on its
  own, so the feature can stop after any context with the codebase consistent.

## Assumptions

- **ADR-0140's exemption wording is a dependency, not a blocker.** Constitution
  §II exempts "a value object's own backing values" in the plural, with an
  identity-reference carve-out, only once ADR-0140 merges with PR #2021. On
  this branch's base, §II still reads as the older nine-type list. The
  composites here are consistent with both readings; the amendment matters for
  how their components are judged, not for whether they are allowed.
- **The composites are stored, not computed.** Each maps to existing columns
  rather than becoming a derived view over them, because the columns are
  queried and indexed today.
- **Naming follows the existing convention** — no abbreviations, the noun the
  concept is called by (ADR-0091, ADR-0094).
- **The audit context costs more than its two sites suggest**, because its
  write path is hand-authored SQL and it has an archival projection. This is
  assumed to be a larger slice, not a blocked one.
- **No new behaviour is introduced anywhere in this feature**, so the red-first
  obligation in constitution §Testing does not apply; the green-throughout
  obligation does.
