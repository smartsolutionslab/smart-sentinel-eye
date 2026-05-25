# ADR-0038: Value Object Strictness — Maximalist

**Status:** Accepted
**Date:** 2026-05-25

## Context

Constitution §II says "primitive types do not cross domain boundaries". That
principle scales from "wrap IDs" to "wrap every primitive". We need to fix
the depth so that every aggregate constructor, every command, and every
event carries the same level of strictness.

## Decision

**Wrap every primitive at a domain boundary.** This includes:

- All identifiers (`CameraId`, `LayoutId`, `OverlayId`, ...).
- All semantic values (`Percentage`, `Url`, `RtspUrl`, `EmailAddress`,
  `IpAddress`, `Port`, `Duration`, `OnvifProfileToken`, `PanAngle`,
  `CronExpression`, ...).
- Free-text fields with domain meaning (`CameraName`, `Description`,
  `OverlayLabel`, `RuleName`).

Framework-typed values (`DateTimeOffset`, `decimal` for primitive money/
quantities without domain meaning) may remain unwrapped.

## Consequences

- **Positive:** every constructor signature is self-documenting. Mixing
  up `CameraId` and `LayoutId` is impossible.
- **Positive:** validation lives in one place per concept.
- **Negative:** many small types — ~150–300 value-object types across the
  9 contexts. Requires investment in the base struct infrastructure
  (ADR-0046).
- **Negative:** more ceremony when constructing test data; offset by
  the test-builder convention (ADR-0054).

## Alternatives Considered

- **Pragmatic:** IDs + a few semantic primitives only. Less code, weaker
  semantic enforcement.
- **Minimal:** only IDs wrapped. Drifts toward anemic model over time.
- **Per-context choice:** inconsistent across the codebase.
