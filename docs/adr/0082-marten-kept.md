# ADR-0082: Confirms ADR-0009 + ADR-0071 (Marten) Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

A second-pass deep-read of the Yumney codebase revealed that Yumney
uses **EF Core + explicit projection handlers** for both state-based
AND event-sourced aggregates. It does not use Marten. ADR-0009 and
ADR-0071 commit Smart Sentinel Eye to Marten for event-sourced
contexts (Overlays, Automation).

## Decision

**Keep Marten** as the event store and projection engine for event-
sourced contexts. Diverge from Yumney here.

Rationale:

- Marten provides Postgres-native streams, snapshot support, inline
  projections, and an async daemon — features we'd reproduce by hand
  in an EF-only world.
- Wolverine's Marten integration is tighter than its EF integration
  (transactional outbox, stream-forwarded events).
- State-based contexts still use EF Core as planned (ADR-0009); we
  use the right tool per persistence paradigm.

## Consequences

- **Positive:** purpose-built tooling for event-sourcing; less code to
  maintain.
- **Negative:** two persistence stacks in the same codebase (EF Core
  + Marten). Engineers context-switching between Yumney and Smart
  Sentinel Eye see different patterns for event-sourced contexts.

## Alternatives Considered

- **Align with Yumney (EF Core everywhere)** — reproduces Marten's
  features by hand; rejected.
- **Hybrid (Marten only for highest-complexity event-sourced
  contexts)** — two patterns to teach; harder than picking the right
  tool per paradigm.
