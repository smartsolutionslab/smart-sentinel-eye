# ADR-0029: Branch Protection on `main` and `develop`

**Status:** Accepted (merge-strategy clause amended by ADR-0087: rebase-only)
**Date:** 2026-05-25

## Context

[ADR-0028](0028-git-workflow-gitflow.md) makes `main` and `develop`
both protected long-lived branches. Without explicit protection
rules, the policies in CONTRIBUTING.md are advisory only — anyone
with push access can violate them. The product runs 24/7 in
industrial settings; a careless force-push to `main` is unacceptable.

## Decision

Apply identical branch-protection rules to both `main` and `develop`:

- ✅ **Pull requests required** — no direct pushes, even by admins.
- ✅ **Linear history** — squash-merge or rebase-merge only. Merge
  commits are blocked so each PR appears as a single commit on the
  target branch.
- ✅ **Required passing status checks** (from
  [ADR-0033](0033-ci-gates.md)): build, unit tests, integration
  tests, NetArchTest, format/lint, secret scan, container smoke.
- ✅ **≥ 1 approving review** for PRs touching `src/` or `apps/web/`.
  Docs-only PRs (touching only `docs/`, `specs/`, top-level `*.md`)
  may merge without review but still require passing checks.
- ✅ **Force-push blocked.**
- ✅ **Branch deletion blocked.**
- ⬜ **Signed commits — deferred.** Revisit once GPG/SSH signing is
  rolled out team-wide.

## Consequences

- **Positive:** the policies in CONTRIBUTING.md are now enforced by
  the platform, not by convention.
- **Positive:** every change to production-eligible code traces back
  to a PR with a CI run and a reviewer.
- **Negative:** solo work requires opening PRs against your own
  branches — small overhead during the early phase. Acceptable given
  the safety guarantees.
- **Negative:** broken main protection can be bypassed only via
  manual admin override, which logs in the audit trail.

## Alternatives Considered

- **Stricter (2 reviewers, signed commits, CODEOWNERS-enforced)**:
  closer to enterprise compliance norms. Deferred until the team grows
  beyond one engineer.
- **Looser (PR + CI required, no review)**: viable while solo;
  rejected because review-by-default is a cheap insurance policy and
  forces self-review on each PR.
- **No protection**: ruled out for a public 24/7 product.

## Implementation Notes

Apply via `gh api` `repos/{owner}/{repo}/branches/{branch}/protection`
or via the GitHub UI. Track the applied rule set in
`docs/operations/branch-protection.md` so it can be reproduced or
re-applied if the GitHub UI state drifts.
