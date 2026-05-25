# ADR-0055: Yumney as Cross-Reference Codebase

**Status:** Accepted
**Date:** 2026-05-25

## Context

[Yumney](https://github.com/smartsolutionslab/yumney) is a sister
flagship product under the same `smartsolutionslab` org, built on the
same .NET Aspire + DDD stack. Aligning patterns reduces context-
switch cost for engineers who work in both repositories.

## Decision

- Treat Yumney as the **reference codebase**: evaluate every
  cross-cutting decision against Yumney's pattern; align unless we
  have a deliberate, documented reason to diverge.
- Each divergence is captured as its own ADR (this set, ADRs 0056 –
  0069) and explicitly notes "diverges from Yumney" with reasoning.
- Each adopted Yumney pattern (`Ensure.That`, `IValueObject<T>`,
  `MigrationRunner`, custom `Deconstruct`, plural naming) is also
  captured as an ADR or codified in CONTRIBUTING.md.

## Consequences

- **Positive:** team context-switching across both repos is cheap
  where decisions align.
- **Positive:** divergences are explicit and reviewable, not
  accidental drift.
- **Negative:** Yumney itself evolves; the alignment matrix needs
  periodic re-evaluation.

## Alternatives Considered

- **Independent decisions, no cross-reference** — risks
  accidental divergence on small things that aren't worth
  re-thinking.
- **Strict alignment, no divergence allowed** — overrides legitimate
  per-project reasoning.
