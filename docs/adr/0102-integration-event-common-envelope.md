# ADR-0102: Common metadata envelope on integration events

**Status:** Accepted
**Date:** 2026-05-29
**Supersedes:** —
**Superseded by:** —

## Context

Spec 009 (AuditObservability) requires a single subscriber that records
**every** `*V1` integration event as one audit row, capturing — per
event — who acted (`Actor`), in which fab (`Fab`), when it happened
(`OccurredAt`), and a stable de-duplication key (`EventIdentifier`).

The `*V1` corpus (17 events across 9 contexts) is **heterogeneous**:
timestamps are named `RegisteredAt` / `PublishedAt` / `OccurredAt` /
`RotatedAt` / …; the actor is `RegisteredBy` / `PublishedBy` / absent;
the fab is `Fab` / `FabId` / absent; only a few carry an explicit event
id. There is no common shape to read from.

A generic audit subscriber therefore has no uniform way to populate the
envelope. The options were: (a) reflection-by-convention over the
heterogeneous fields (fragile to naming drift), (b) one explicit handler
per event (17 handlers, verbose), or (c) give every integration event a
**common metadata envelope** read uniformly. We chose (c): it is the
most explicit, keeps the audit subscriber trivial, and makes "who/when/
where" first-class on every cross-context event rather than an
audit-only concern.

## Decision

`IIntegrationEvent` carries an `EventMetadata` value:

```csharp
public sealed record EventMetadata(
    Guid EventIdentifier,    // Guid v7, stable per logical event
    DateTimeOffset OccurredAt,
    string? Fab,             // owning fab, when the event is fab-scoped
    Guid? Actor);            // acting principal, when known

public interface IIntegrationEvent
{
    EventMetadata Metadata { get; }
}
```

Every `*V1` record gains a trailing positional `EventMetadata Metadata`
parameter. Every publisher (the `IDomainEventHandler` / command handler
that builds the `*V1` from a domain event) populates it:
`EventIdentifier = Guid.CreateVersion7()`, `OccurredAt` = the domain
event's timestamp, `Fab` / `Actor` from the aggregate / command where
available, else `null`.

The AuditObservability context runs **one** generic Wolverine subscriber
over the `*V1` set that reads `evt.Metadata`, serialises the message to
the `payload jsonb`, and writes the audit row — no per-event code.

**Additive, not migrating.** Existing per-event fields that overlap the
envelope (e.g. `CameraRegisteredV1.RegisteredAt` / `RegisteredBy`) are
**kept** so current consumers (e.g. StreamDistribution's
`CameraRegisteredIntegrationEventHandler` reading `message.RegisteredBy`)
are untouched. The short-term duplication is a deliberate transitional
cost; a later cleanup can fold the per-event fields into the envelope
once consumers read `Metadata`.

## Consequences

**Positive:**

- The audit subscriber is one generic handler, not 17.
- "Who / when / which fab" is uniform and self-describing on every
  integration event — useful beyond audit (tracing, replay).
- `EventIdentifier` gives a stable idempotency key independent of the
  Wolverine transport envelope.

**Negative:**

- One-time change to every `*V1` record (17) and every publisher (≈13
  call sites) plus their contract tests. Compile-enforced, so the change
  is mechanical and complete.
- Transitional duplication between `Metadata.OccurredAt` / `Actor` and
  the legacy per-event `*At` / `*By` fields until a follow-up migrates
  consumers.

## Alternatives Considered

- **Reflection-by-convention (no contract change) — REJECTED.** Fragile
  to field-naming drift; a renamed field silently drops audit fidelity.
- **One handler per event — REJECTED.** 17 near-identical handlers; new
  events silently skip auditing unless someone remembers to add one (an
  architecture test helps but the boilerplate is real).
- **Wolverine-envelope metadata only — REJECTED.** The transport
  envelope has a message id + sent time but no domain notion of `Fab` /
  `Actor`; those have to live in the contract.
