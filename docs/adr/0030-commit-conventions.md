# ADR-0030: Commit Conventions — Conventional Commits

**Status:** Accepted
**Date:** 2026-05-25

## Context

We need predictable commit messages so that:

- Reviewers can scan a PR's history at a glance.
- SemVer bumps (ADR-0032) can be derived automatically from commit
  history.
- A changelog can be generated mechanically.
- Spec-Kit's auto-commits (which use a fixed `[Spec Kit] <stage>`
  prefix) coexist with human commits without ambiguity.

## Decision

Human-written commits follow **Conventional Commits** with **scope =
bounded context**.

Format:

```
<type>(<scope>): <subject>

<body explaining WHY, not WHAT>

<optional footer>
```

**Allowed types:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`,
`perf`, `build`, `ci`.

**Allowed scopes:**

- 9 bounded contexts: `camera-catalog`, `stream-distribution`,
  `layout-composition`, `system-variables`, `event-ingestion`,
  `overlays`, `automation`, `identity`, `audit-observability`.
- Cross-cutting: `repo`, `infra`, `web`, `tests`, `deploy`, `adr`,
  `workflows`.

**Breaking changes:** mark with `!` after type/scope and a
`BREAKING CHANGE:` footer. These trigger a major SemVer bump.

**Spec-Kit auto-commits** keep their `[Spec Kit] <stage>` prefix —
they are a separate namespace and are not subject to this convention.

`commitlint` runs via Husky on the `commit-msg` hook and rejects
non-conforming human commits before they land.

## Consequences

- **Positive:** changelog generation tools (`release-please`,
  `changesets`) can derive releases from commit history.
- **Positive:** PR titles and squashed commits are uniform and
  scannable.
- **Negative:** small learning curve for newcomers; offset by
  `commitlint` catching mistakes early.
- **Negative:** scope vocabulary must be kept in sync with the
  bounded-context list. Drift handled by updating both
  `.commitlintrc.json` and this ADR.

## Alternatives Considered

- **Free-form with mandatory body**: simpler, harder to automate.
  Rejected because release automation is wanted from the start.
- **Spec-Kit-style bracketed prefix for everything**: uniform with
  Spec-Kit auto-commits but loses the Conventional Commits tooling
  ecosystem.
- **No convention**: rejected as inconsistent with a 24/7 product
  posture.

## Implementation Notes

- Configure `commitlint` with `@commitlint/config-conventional` and a
  custom `scope-enum` matching the allowed-scopes list above.
- Husky installation happens during the .NET / web scaffold task
  (Task #8 / #9).
