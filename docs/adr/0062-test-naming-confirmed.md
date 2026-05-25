# ADR-0062: Confirms Sentence-Style Test Naming Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney uses `Method_Scenario_Expected` test names. ADR-0053 picked
the sentence-style underscore convention.

## Decision

**Keep sentence-style test naming (ADR-0053).** The test suite reads
as a behaviour specification.

## Consequences

- Diverges from Yumney.
- Test names are longer; offset by the readability gain.

## Alternatives Considered

See ADR-0053. Yumney's pattern was evaluated and rejected for
readability.
