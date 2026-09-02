# Verification: 058 — properties that travel together

**Feature**: 058 | **Date**: 2026-09-02

## User Story 3 is DECLINED, not deferred

**`AuditEvent.Actor` + `ActorUsername` cannot become one composite without
dropping an index, and FR-004 forbids that.** This is a hard EF limitation,
found by building it rather than by reasoning about it, and it is recorded here
because a story ticked as "done later" would imply it is merely unfinished.

### What blocks it

`ix_audit_actor_occurred` spans **two entity types once the composite exists**:

```csharp
builder.HasIndex(auditEvent => new { auditEvent.Actor, auditEvent.OccurredAt })
    .HasDatabaseName("ix_audit_actor_occurred");
```

`Actor` becomes the composite and `OccurredAt` stays on the row. EF has no way
to express an index across that boundary, by either mechanism:

**Owned reference** — the index lambda tries to add a scalar `Actor` where a
navigation already exists:

```text
The property or navigation 'Actor' cannot be added to the 'AuditEvent' type
because a property or navigation with the same name already exists on the
'AuditEvent' type.
```

**Complex type** — research R1 rejected complex types generally; they were
tried here anyway, because this is exactly the case where an owned reference
fails and a complex type's members belong to the owning entity. Same error.
Naming the member directly fails differently and just as finally:

```text
The property 'Actor.Identifier' cannot be added to the type 'AuditEvent'
because no property type was specified and there is no corresponding CLR
property or field.
```

**Removing the index makes the model build.** That is how the cause was
confirmed — and it produces a pending migration that drops
`ix_audit_actor_occurred`, which is precisely the schema change FR-004 exists
to prevent. The index backs the audit search's actor filter, on a hypertable.

### What was not done about it

No workaround was adopted, and that is deliberate. The available ones are all
worse than the problem:

- **Drop the index** — breaches FR-004 and slows an indexed search path on the
  largest table in the system.
- **Keep it outside the EF model**, created by raw SQL like the TimescaleDB
  hypertable already is. Then EF's differ wants to DROP it on the next
  migration anyone generates — the same latent-divergence trap as issue #2022,
  deliberately re-created.
- **Split the composite** so only the username moves. That is not the story.

### Consequence for the spec

FR-006 and SC-001's count of twelve are **not achievable as written**. Eleven
pairs become eleven composites; the twelfth stays two properties. `spec.md` and
`data-model.md` describe the actor composite as buildable, and they were wrong
about the one thing that could not be checked in advance — an index is not a
column, and R1 only checked columns.

**If US3 is wanted, it needs a decision that is not this feature's to make**:
either the index moves out of the model with the divergence risk accepted, or
`occurred_at` and the actor stop sharing an index. Both are schema decisions.

## User Story 2 is delivered

`StoredPayload` groups the audit payload with its size and **derives** the
size, so the two can no longer disagree. No index spans it, so nothing above
applies.

- AuditObservability reports no pending model change.
- Domain 89, Application 43, Architecture 105 green; Release build clean.
- The derivation is enforced structurally, not by assertion: `StoredPayload`
  has no public constructor and its only factory takes content alone. A test
  asserts that shape, so re-introducing a size parameter fails the build's
  test run rather than being noticed in review.
