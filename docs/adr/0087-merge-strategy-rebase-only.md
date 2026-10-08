# ADR-0087: Amends ADR-0029 — Merge Strategy: Rebase-Only

**Status:** Accepted (amends ADR-0029)
**Date:** 2026-05-25

## Context

ADR-0029 originally allowed **squash or rebase** merges. Yumney
uses **rebase-only** ("no merge commits, no squash"). Aligning gives
identical git-log shape across both flagship repos.

## Decision

**Rebase-only.** PR commits land individually on `develop` or `main`.

- GitHub repo settings: only `Rebase merge` enabled; `Squash merge`
  and `Merge commit` disabled (already applied via
  `gh repo edit --enable-squash-merge=false --enable-merge-commit=false
  --enable-rebase-merge=true`).
- Branch protection rule `required_linear_history: true` continues to
  block merge commits.

Implications for the developer workflow:

- **Every commit on a feature branch survives** — clean each one
  before opening the PR. Use `git rebase -i` (interactive squash /
  fixup / reword) on the feature branch before requesting review.
- **Conventional Commits per commit** — each commit is independently
  visible on `develop`, so each commit message must conform to ADR-
  0030.
- **`git bisect` works on every commit** — with one documented exception,
  below.

## Consequences

- **Positive:** complete, linear, fine-grained history. Best for
  archaeology and bisect.
- **Positive:** aligns with Yumney.
- **Negative:** more discipline required per commit on the feature
  branch. Hands the squash-cleanup responsibility to the author.
- **Negative:** large PRs produce more commits on the trunk than
  the squash model. Acceptable when each commit is meaningful.
- **Negative, accepted (2026-10-08, #2178):** a phase-4a red/green pair
  (ADR-0139/ADR-0144 — the failing test lands, then its fix lands as the
  next commit) is a commit on `develop` at which the test suite the pair
  belongs to fails, even though that commit **compiles**. `git bisect`
  crossing that range gets a `bad` verdict there for a reason unrelated to
  whatever bug is actually being hunted. Found concretely at `a7a2f00f`
  (spec 085, #2113). **Accepted rather than closed**: the alternative —
  requiring the red test and its fix to land as one commit — would weaken
  ADR-0139's evidence trail to just the PR body, and reordering
  declarations-before-guard only inverts which half of the pair is red.
  A bisect that lands inside such a range should re-run from one commit
  earlier or later, not trust the verdict at the pair itself.

## Alternatives Considered

- **Keep ADR-0029's "squash or rebase" flexibility** — per-PR author
  choice; uneven history.
- **Squash-only** — most uniform history; loses intermediate commits
  permanently.
