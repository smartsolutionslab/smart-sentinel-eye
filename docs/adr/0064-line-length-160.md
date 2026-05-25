# ADR-0064: Amends ADR-0034 — Line Length 160

**Status:** Accepted (amends ADR-0034)
**Date:** 2026-05-25

## Context

ADR-0034 originally set `max_line_length = 120`. Yumney uses 140.
Generic-heavy signatures like
`Task<Result<CameraId, RegisterCameraError>>` and Wolverine handler
shapes routinely exceed 120 chars without natural break points.

## Decision

**Set `max_line_length = 160`** in `.editorconfig`. Slightly looser
than Yumney's 140.

## Consequences

- **Positive:** generic-heavy signatures stay on one line.
- **Negative:** code-review diff readability slightly degraded on
  narrow displays. Mitigated by GitHub's diff width.
- **Negative:** diverges from Yumney by 20 chars.

## Alternatives Considered

- **120** (our original) — too tight for the generic signatures.
- **140 (Yumney's choice)** — still tight for some signatures; closer
  to Yumney but still occasionally cramped.
- **No limit** — encourages walls of dense code.
