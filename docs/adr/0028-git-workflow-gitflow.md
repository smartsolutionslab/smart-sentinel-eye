# ADR-0028: Git Workflow — GitFlow

**Status:** Accepted
**Date:** 2026-05-25

## Context

Smart Sentinel Eye targets 24/7 industrial production deployments. It
needs an explicit release lifecycle: a stable production line, a
distinct integration line, and a way to harden and ship release
candidates without freezing day-to-day development. Spec-Kit already
introduces per-feature branches (`NNN-feature-name`) but does not
opinionate on what they merge into.

## Decision

Adopt **GitFlow** branching:

- **`main`** — production-released code only, tagged with SemVer.
  Protected; direct pushes forbidden.
- **`develop`** — integration line. **Default branch on GitHub.** All
  feature work merges here.
- **`NNN-feature-name`** — Spec-Kit feature branches, cut from
  `develop`, merge back to `develop` via squash- or rebase-merge PR.
- **`release/x.y.z`** — cut from `develop` when freezing for release.
  Only stabilization commits (fix, docs, chore) land here. Merges to
  **both** `main` and `develop`.
- **`hotfix/<short>`** — cut from `main` for production fixes. Merges
  to **both** `main` and `develop`.
- **Non-spec work** (ADR-only edits, infra config, dependency bumps)
  uses a short-lived branch off `develop` prefixed by the Conventional
  Commit type (`docs/…`, `chore/…`, `ci/…`).

Tags are created on `main` only, after a release-branch or
hotfix-branch merge.

## Consequences

- **Positive:** clear separation between production-released and
  in-development code. Multiple release lines can coexist (`release/1.0.x`
  and `release/1.1.x`). Hotfixes are first-class.
- **Positive:** the Spec-Kit feature workflow plugs in without
  modification — features just happen to target `develop`.
- **Negative:** more branches and merge ceremony than GitHub Flow.
  Each release requires a dual-merge (back to both `main` and
  `develop`).
- **Negative:** developers must remember which base branch to cut
  from. CI must enforce target branch on PRs (no feature PRs against
  `main`).

## Alternatives Considered

- **GitHub Flow** (feature → PR → `main`; tags on `main` from any
  commit): simpler, fewer long-lived branches. Rejected because the
  product targets 24/7 deployments where a stable production line
  separated from active development is valuable, and because customer
  fab releases may need to stabilize for weeks before shipping while
  development continues.
- **Trunk-based development**: small short-lived branches behind
  feature flags. Excellent for high-trust small teams with fast CI.
  Rejected for now: industrial code review and compliance expectations
  benefit from explicit PR cycles per feature.
- **Per-customer release branches**: a long-lived `release/customer-foo`
  per fab. Reconsider once we have multiple deployed customers needing
  divergent code; out of scope for v1.

## Implementation Notes

- GitHub default branch must be **`develop`** so new PRs target it by
  default. Set during repository configuration.
- Both `main` and `develop` receive the protection rules from
  [ADR-0029](0029-branch-protection.md).
