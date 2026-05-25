# ADR-0040: Domain Events vs Integration Events — Separate Types

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0010 establishes RabbitMQ as the cross-context messaging fabric.
ADR-0016 puts each of the 9 contexts behind its own boundary. We need
to decide whether internal aggregate-state-change events and external
inter-context events share a type, or are deliberately separated.

## Decision

**Domain events and integration events are separate types.**

- **Domain events** live inside a bounded context, fire in-process when
  an aggregate's state changes, and **never leave the context**. They
  are typed against the aggregate's internal language (e.g.
  `CameraRegistered { CameraId, CameraName }`).
- **Integration events** are defined in `Shared.Contracts`, versioned
  with `V<N>` suffixes (ADR-0073), and published to RabbitMQ. They
  carry only the cross-context vocabulary (e.g. `CameraRegisteredV1`).
- A handler inside the publishing context translates the domain event
  into one or more integration events and emits them via the outbox
  (Wolverine + Postgres outbox, ADR-0042).

## Consequences

- **Positive:** renaming or reshaping an aggregate's domain event has
  zero impact on external consumers.
- **Positive:** integration events evolve on their own versioning
  schedule (ADR-0073).
- **Positive:** clear blast radius for internal refactors.
- **Negative:** small translation layer per published event. Pays back
  on the first refactor.

## Alternatives Considered

- **Single event type for both purposes** — couples external consumers
  to internal aggregate naming. Routinely regretted in DDD literature.
- **Synchronous REST between contexts** — coupling deploys, breaks the
  latency SLO.
