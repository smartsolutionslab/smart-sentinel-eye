# ADR-0056: Confirms ADR-0048 (NRT Disabled + Option&lt;T&gt;) Against Yumney Divergence

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney has `Nullable=enable` and no `Option<T>`/`Maybe<T>` in its
shared kernel. ADR-0048 chose the opposite path.

## Decision

**Keep ADR-0048 as written.** NRT remains disabled at the solution
level; `Option<T>` is the canonical way to express domain absence.

## Consequences

- Diverges from Yumney.
- Developer friction at NuGet boundaries (BCL null annotations
  ignored) is acknowledged and accepted for the consistency benefit
  inside the codebase.

## Alternatives Considered

See ADR-0048. Re-evaluated against Yumney; conclusion unchanged.
