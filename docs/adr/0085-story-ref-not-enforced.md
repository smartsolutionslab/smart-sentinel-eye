# ADR-0085: Story Reference in Commits — Not Enforced

**Status:** Accepted
**Date:** 2026-05-25

## Context

Yumney requires every commit and PR title to include a `(US-NNN)`
reference to a GitHub Project user story. Smart Sentinel Eye has a
Project board (#13) and a parallel Spec-Kit feature numbering
(`NNN-feature-name`).

## Decision

**Do not enforce story refs in commits or PR titles.** ADR-0030
(Conventional Commits) is unchanged.

Rationale:

- Spec-Kit feature branches (`NNN-feature-name`) already trace work
  back to the spec.
- `/speckit-taskstoissues` pushes tasks to the Project board,
  preserving the trace through GitHub issues.
- A separate `(US-NNN)` footer convention would duplicate the trace.

## Consequences

- Diverges from Yumney; engineers context-switching between repos
  carry slightly different commit-message expectations.
- Eliminates one source of `commitlint` friction during early
  development.

## Alternatives Considered

- **Adopt Yumney's `(US-NNN)` requirement** — duplicates Spec-Kit
  branch-name trace.
- **Adopt with Spec-Kit feature ID instead** — same trace, just a
  different identifier shape. Considered, deferred until a real need
  emerges.
