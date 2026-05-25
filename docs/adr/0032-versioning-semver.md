# ADR-0032: Versioning — SemVer for the Whole Product

**Status:** Accepted
**Date:** 2026-05-25

## Context

[ADR-0028](0028-git-workflow-gitflow.md) makes releases explicit.
Each release needs a tag scheme that:

- Communicates compatibility implications to customers.
- Aligns with Conventional Commits so automation can derive bumps.
- Works for a product that ships its nine bounded contexts together as
  one Aspire stack.

## Decision

Adopt **Semantic Versioning** (SemVer 2.0.0) for the **whole product**
as a single tag line on `main`.

- Tags: `vMAJOR.MINOR.PATCH` (e.g. `v0.2.0`, `v1.0.0`).
- Pre-releases: `vMAJOR.MINOR.PATCH-alpha.N`,
  `vMAJOR.MINOR.PATCH-beta.N`, `vMAJOR.MINOR.PATCH-rc.N`.
- All 9 bounded contexts ship at the same version. There is one
  product, even though it is composed of multiple internal services.
- Tags are created on `main` only, after a `release/x.y.z` or
  `hotfix/<short>` branch merges into `main`.

**SemVer rules driven by Conventional Commits:**

- `feat:` → minor bump.
- `fix:` / `perf:` → patch bump.
- `feat!:` or any commit with a `BREAKING CHANGE:` footer → major bump.
- Other types (`docs`, `chore`, `test`, `refactor`, `build`, `ci`) do
  not bump the version unless paired with a `feat`/`fix` change.

Changelog generation tool to be selected (likely `release-please` or
`changesets`) when the first non-alpha release approaches.

## Consequences

- **Positive:** customers can reason about compatibility per release.
- **Positive:** bumping is mechanical — no judgement calls per
  release.
- **Negative:** a change to one bounded context bumps the product
  version even if other contexts are untouched. Acceptable because we
  ship as one stack.
- **Negative:** until v1.0.0, the SemVer rules permit breaking
  changes in minor bumps (0.x.y semantics). We will document this
  clearly in release notes.

## Alternatives Considered

- **CalVer (`2026.05.0`)**: appealing for predictable cadence, but
  weaker compatibility signal. Rejected for v1; reconsider if release
  cadence becomes strictly monthly.
- **Per-context SemVer**: useful if contexts shipped independently.
  They don't. Adds tag noise without benefit.
- **No formal versioning until GA**: was tempting but makes the
  release branch ceremony harder to teach and rehearse.

## Implementation Notes

- Initial tag: `v0.1.0-alpha.1` after the walking skeleton lands.
- Document the release runbook in `docs/operations/release.md` when
  cutting the first release branch.
