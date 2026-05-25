# ADR-0086: Amends ADR-0030 — No Co-Authored-By Footer in Commits

**Status:** Accepted (amends ADR-0030)
**Date:** 2026-05-25

## Context

Earlier commits in this repository included
`Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`
footers to attribute LLM assistance. Yumney's CLAUDE.md explicitly
forbids the footer; commits are attributed solely to the human
author.

## Decision

**No `Co-Authored-By` footers** in any commit, regardless of how much
LLM assistance contributed.

- Applies going forward. Existing commits with the footer are not
  rewritten — they're part of the immutable history.
- The Claude Code agent prepares commit messages without the footer.
- Conventional Commit format remains the same (ADR-0030).

## Consequences

- **Positive:** consistent attribution model with Yumney; matches
  the broader smartsolutionslab convention.
- **Positive:** simpler audit story — one author per commit.
- **Negative:** future readers cannot tell from the commit metadata
  alone which changes were LLM-authored. PR descriptions remain the
  source of that context.

## Alternatives Considered

- **Keep Co-Authored-By** — diverges from Yumney; conflicts with the
  emerging org-wide convention.
- **Per-commit judgement** — inconsistent.
