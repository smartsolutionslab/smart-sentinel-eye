# ADR-0034: Code Style Enforcement

**Status:** Accepted (extended by ADR-0064: line length 160, and ADR-0084: SonarAnalyzer metrics)
**Date:** 2026-05-25

## Context

Consistent formatting is a precondition for productive review — every
keystroke spent on whitespace or trailing-newline arguments is a
keystroke not spent on the actual change. The codebase mixes C# and
TypeScript with smaller amounts of YAML and Markdown. Each ecosystem
has its own toolchain.

## Decision

- **`.editorconfig`** at the repository root encodes shared formatting
  rules (line endings, indent style/size, trim trailing whitespace,
  final newline) plus C#-specific style rules.
- **C#:** `dotnet format` against the analyzer rules baked into
  .NET 10 + Roslyn analyzers:
  - `EnableNETAnalyzers=true`
  - `AnalysisLevel=latest`
  - `TreatWarningsAsErrors=true` in `Release` configuration
- **TypeScript / React:** ESLint + Prettier with the configs shipped
  in `apps/web/`.
- **YAML / Markdown:** Prettier defaults.
- **Husky** installs pre-commit hooks that run formatters on
  **staged files only** for a fast local loop:
  - `dotnet format --include <staged-cs-files>`
  - `prettier --write <staged-non-cs-files>`
  - `eslint --fix <staged-ts-files>`
- **CI** verifies via `--verify-no-changes` and ESLint failure as
  PR-blocking checks (ADR-0033). A bypassed hook still fails the PR.

## Consequences

- **Positive:** style debates effectively over before they start.
- **Positive:** PR diffs reflect only meaningful changes.
- **Negative:** small per-commit latency from the pre-commit hook.
  Staged-files-only keeps it bounded.
- **Negative:** `TreatWarningsAsErrors=true` in Release builds forces
  warnings to be resolved or explicitly suppressed before release.
  Worthwhile; documents intent.

## Alternatives Considered

- **CSharpier + Prettier**: more opinionated, zero-config formatter
  for C#. Reasonable choice; rejected because `dotnet format` is the
  Microsoft default and integrates with the analyzer infrastructure
  we want anyway. Reconsider if `dotnet format` proves unreliable.
- **CI-only enforcement (no Husky)**: slower feedback loop;
  developers discover formatting issues only after pushing. Rejected
  in favor of staged-files-only pre-commit hooks.
- **`.editorconfig` only, no formatter**: relies entirely on developer
  hygiene. Drifts over time.

## Implementation Notes

- Repo-root `.editorconfig` is committed alongside this ADR.
- Husky installation, `dotnet format` integration, `commitlint`
  configuration, ESLint config, and Prettier config happen during the
  .NET and web scaffold tasks (Task #8 and #9).
