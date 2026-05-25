# ADR-0060: Confirms Shouldly (ADR-0052) Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney uses **FluentAssertions** (commercial since 2024). ADR-0052
picked Shouldly.

## Decision

**Keep Shouldly.** MIT-licensed, no future licensing risk, comparable
API ergonomics to FluentAssertions.

## Consequences

- Diverges from Yumney on the assertion library.
- One-time per-developer migration cost when context-switching
  between the two projects.

## Alternatives Considered

- **FluentAssertions** — paid; rejected.
- **AwesomeAssertions** (community fork) — newer; long-term
  maintenance unknown.
- **Vanilla xUnit asserts** — less ergonomic.
