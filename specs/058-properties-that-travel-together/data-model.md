# Phase 1 Data Model: Properties that travel together

**Feature**: 058 | **Date**: 2026-09-02 | **Plan**: [plan.md](./plan.md)

Ten new value objects replace 24 loose properties with 12. **No table, column,
index or nullability changes** — every composite occupies the columns its parts
occupy today (research [R1](./research.md)).

---

## The seven timestamp/actor composites

Three names, declared once per context that needs them. They are separate types
by decision (FR-002, FR-003), not by omission: each wraps its own context's
timestamp value object, and a shared type would either need a cross-context
reference or collapse `CreatedAt`/`RegisteredAt`/`ProvisionedAt` back into a
bare `DateTimeOffset` — undoing spec 057.

| Context | Type | Components | Aggregates | Columns (unchanged) |
|---|---|---|---|---|
| Automation | `Creation` | `CreatedAt At`, `OperatorIdentifier By` | `Rule` | `created_at`, `created_by` |
| LayoutComposition | `Creation` | `CreatedAt At`, `OperatorIdentifier By` | `Layout`, `Layout.Revision` | `created_at`, `created_by` |
| OverlayDesigner | `Creation` | `CreatedAt At`, `OperatorIdentifier By` | `Overlay`, `Overlay.Revision` | `created_at`, `created_by` |
| SystemVariables | `Creation` | `CreatedAt At`, `OperatorIdentifier By` | `Variable` | `created_at`, `created_by` |
| CameraCatalog | `Registration` | `RegisteredAt At`, `OperatorIdentifier By` | `Camera` | `registered_at`, `registered_by` |
| Identity | `Registration` | `RegisteredAt At`, `OperatorIdentifier By` | `RegisteredClient` | `registered_at`, `registered_by` |
| StreamDistribution | `Provisioning` | `ProvisionedAt At`, `OperatorIdentifier By` | `Stream` | `provisioned_at`, `provisioned_by` |

Nine sites, seven types — LayoutComposition and OverlayDesigner each use theirs
twice, on the aggregate and on its revision.

**Shape.** A `sealed record` with a guarded `From`, both components required.
A record because value equality is the point, and sealed because nothing
specialises it.

**What replaces what**, using `Camera` as the worked example:

```csharp
// before
public RegisteredAt RegisteredAt { get; private set; } = null!;
public OperatorIdentifier RegisteredBy { get; private set; }

// after
public Registration Registration { get; private set; } = null!;
```

**Naming.** `Registration`, not `RegistrationStamp` or `RegisteredAtBy` — the
noun the concept is called by (ADR-0091, ADR-0094). The components are `At` and
`By`, which read at the call site as `camera.Registration.At`.

---

## The three audit and automation composites

| Context | Type | Components | Replaces | Columns (unchanged) |
|---|---|---|---|---|
| AuditObservability | `Actor` | `ActorIdentifier Identifier`, `ActorUsername?` | `AuditEvent.Actor`, `ActorUsername` | `actor_identifier`, `actor_username` |
| AuditObservability | `StoredPayload` | `AuditPayload Content`, `PayloadSizeBytes Size` | `AuditEvent.Payload`, `PayloadSizeBytes` | `payload`, `payload_size_bytes` |
| Automation | `Trigger` | `TriggerSource Source`, `TriggerKind Kind` | `Rule.TriggerSource`, `TriggerKind` | `trigger_source`, `trigger_kind` |

### `Actor` — one identity, an optional name

The username is optional and the identifier is not, so the composite is
required and only one of its components is nullable. `IsSystem` moves onto the
composite, where callers already look for it.

There is a name collision to resolve deliberately: `ActorIdentifier` already
exists and stays. The composite is `Actor`, and `AuditEvent.Actor` changes type
from `ActorIdentifier` to `Actor` — the property name does not move, which
keeps the diff honest about what changed.

### `StoredPayload` — a derivation, not a pair

**The only composite that computes one of its components.** Its factory takes
content and derives the size; there is no factory that accepts both (FR-005).

```csharp
StoredPayload.From(string content)   // size := UTF-8 byte count of content
```

A materialisation path still has to reconstruct one from two stored columns,
because rows written before this feature must load. That path is internal to
persistence and must not be a public factory — otherwise the invariant is one
call away from being bypassed, which is the state this feature is removing.

**A consequence worth stating**: if any stored row's size disagrees with its
content today, reconstruction will preserve the disagreement rather than
repair it. Repairing stored data is a migration, and FR-004 forbids one.
Whether such rows exist is unknown and untested for; the composite prevents
new ones.

### `Trigger` — the smallest of the four

Both components required, no derivation, no optionality. It is the model for
what the other composites would look like without their complications.

---

## Persistence

Every composite maps as an EF **owned reference** with explicit column names,
plus `Navigation(...).IsRequired()` on the owner — the line that keeps the
columns `NOT NULL` and without which this feature creates nine instances of
issue #2022 (research [R1](./research.md)).

```csharp
builder.OwnsOne(camera => camera.Registration, registration =>
{
    registration.Property(value => value.At)
        .HasColumnName("registered_at")
        .HasConversion(at => at.Value, value => RegisteredAt.From(value))
        .IsRequired();
    registration.Property(value => value.By)
        .HasColumnName("registered_by")
        .HasConversion(by => by.Value, value => OperatorIdentifier.From(value))
        .IsRequired();
});
builder.Navigation(camera => camera.Registration).IsRequired();
```

Two sites nest one level deeper, inside the existing `OwnsMany` for revisions;
research R1 confirmed the columns land in the revisions table unchanged.

**AuditObservability does not go through this at all for writes.** Its rows are
written by a hand-authored `INSERT` and projected by an archiver, both of which
read the properties directly. They change with the properties (FR-007). Its
*read* path is unaffected beyond naming — filters and ordering on a composite
component translate to the same SQL against the same indexes (research R2).

---

## What is deliberately not modelled

- **A publisher or archiver actor.** `PublishedAt` and `ArchivedAt` have no
  actor anywhere, and adding one is a schema change and a behaviour change
  (FR-010). The asymmetry stays visible: `Revision` will expose one `Creation`
  beside two bare timestamps, and that reads as the open question it is.
- **A shared `Stamp` in `Shared.Kernel`.** FR-002. `Shared.Kernel` holds
  language-level types; "who created this and when" is domain vocabulary, and
  the seven copies keep each context's own timestamp type.
- **Grouping the remaining lifecycle timestamps** (`RevokedAt`/`RotatedAt` on
  `WebhookIntegration`, `DisabledAt` on `RegisteredClient`, `PublishedAt`/
  `ArchivedAt` everywhere). They are single properties, not pairs. Nothing to
  group.
