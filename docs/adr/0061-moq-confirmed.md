# ADR-0061: Confirms Moq (ADR-0052) Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney uses **NSubstitute**. ADR-0052 picked Moq, despite the 2024
SponsorLink controversy (which was reverted).

## Decision

**Keep Moq.** Acknowledged friction when context-switching between
repositories.

## Consequences

- Diverges from Yumney.
- Engineers familiar with NSubstitute's API have a brief learning
  curve on Moq syntax.

## Alternatives Considered

- **NSubstitute (Yumney's pick)** — equally fine technically.
- **No mocking library (fakes only)** — more test infrastructure code.
