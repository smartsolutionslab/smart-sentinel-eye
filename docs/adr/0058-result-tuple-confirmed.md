# ADR-0058: Confirms ADR-0047 (Result&lt;T, Error&gt;) Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney uses a simpler `Result` base + `Result<T>` with a single
generic, where failures carry a `(string code, string message)` pair.
ADR-0047 chose the two-generic `Result<T, Error>` with exhaustive
sealed-record error hierarchies.

## Decision

**Keep ADR-0047 as written.** Failure types remain sealed-record
hierarchies per handler. Exhaustive pattern matching at API boundaries
is valued over Yumney alignment.

## Consequences

- More boilerplate per handler (each declares its own error union).
- Diverges from Yumney; engineers switching between projects adapt
  by reading the handler signature.

## Alternatives Considered

See ADR-0047. Yumney's pattern was evaluated and rejected for the
compile-time exhaustiveness benefit.
