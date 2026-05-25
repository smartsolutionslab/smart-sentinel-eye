# ADR-0084: Code Metric Limits via SonarAnalyzer

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney enforces hard code-metric limits via analyzers: 300 LOC/file,
30 LOC/method, 4 params max, cyclomatic complexity ≤ 10, nesting
depth ≤ 3. Smart Sentinel Eye had no equivalent automation.

## Decision

Adopt the same limits via **SonarAnalyzer.CSharp**, configured in
`Directory.Build.props`:

| Limit | Value | Sonar rule |
|---|---|---|
| Max LOC per file | 300 | S104 |
| Max LOC per method (backend) | 30 | S138 |
| Max parameters | 4 | S107 |
| Cyclomatic complexity | 10 | S1541 |
| Nesting depth | 3 | S134 |
| Frontend: max LOC per method | 50 | (ESLint complexity / max-lines-per-function) |

- All rules are warnings by default; `TreatWarningsAsErrors=true` in
  Release config makes them PR blockers via ADR-0033 CI.
- **Test projects exempt** — long, narrative test methods are
  valuable; `S104;S138;S107;S1541;S134` plus xUnit-specific
  conventions are suppressed via `NoWarn` for paths under `tests/`.

## Consequences

- **Positive:** quantitative quality bar; reviewers don't count lines
  by hand.
- **Positive:** aligns with Yumney.
- **Negative:** SonarAnalyzer is one more package to track for
  upgrades.
- **Negative:** some legitimate code (Wolverine config blocks, EF
  configuration methods) brushes against the limits and requires
  per-instance refactoring or a documented `[SuppressMessage]`.

## Alternatives Considered

- **Relaxed limits (500 LOC / 50 LOC / 5 params / 12 / 4)** —
  considered; rejected to stay aligned with Yumney.
- **No automated limits** — drift over time.
- **Stricter limits (200 / 20 / 3 / 8 / 2)** — too aggressive for
  Wolverine handler shapes and Marten projection callbacks.
