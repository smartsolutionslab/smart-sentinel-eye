# ADR-0109 — Parallel multi-agent development in git worktrees

**Status:** Accepted

**Relates to:** ADR-0028 (GitFlow), ADR-0087 (rebase-only, linear history), ADR-0037 (7-phase workflow + gates), ADR-0030 (Conventional Commits), ADR-0108 (Playwright e2e gate)

## Context

Development ran **serially**: one branch/PR in flight at a time, the lead
blocked waiting on each PR's CI before starting the next slice. Yet the repo is
already structured for parallel work that we didn't exploit — spec-kit
`tasks.md` with explicit `[P]` markers, per-user-story decomposition, Project
#13 issues, and the `.agents/` skill set. CI is **not** the bottleneck: it's
already parallel and ~10 min end-to-end (backend + frontend concurrent;
integration + e2e concurrent after backend). The bottleneck was the serial
*work*.

## Decision

For a batch of **independent slices**, develop them **concurrently**, each in
its own **git worktree** on its own branch off `develop`, each its own PR.

- An **orchestrator** (the lead session/dev) splits the batch, dispatches **one
  agent per slice** (Claude Code Agent tool with `isolation: "worktree"`,
  `run_in_background: true`). Each agent implements its slice (Phase 4), runs
  the local gates it can (build / typecheck / lint / `playwright test --list`),
  commits (ADR-0030, no `Co-Authored-By`), pushes a `type/short-desc` branch,
  and opens a PR `--base develop`.
- The orchestrator then **integrates**: watches each PR's CI (the e2e gate,
  ADR-0108, verifies behaviour) and **rebase-merges in dependency order**.

**Conflict-avoidance rule (load-bearing).** Rebase-only + linear history
(ADR-0087) means parallel branches touching the **same file collide**. A batch
is parallelizable **only if its slices own disjoint file sets.** These
**contention files** must be single-owner per batch (or edited serially *before*
the fan-out):

- `src/Shared.Kernel/*`, `src/Shared.Contracts/*`
- `src/AppHost/AppHost.cs`
- `apps/shared/*` (the gateway client, the Dialog primitive, the `*.api.ts` clients)
- `e2e/support/*` and any spec touched by more than one slice
- `.github/workflows/ci.yml`, `Directory.Packages.props`, `global.json`

Clean-parallel splits in practice: **one bounded context per agent**, or **one
independent frontend feature + its own e2e spec per agent**.

**Gates still bind.** Parallelism applies to the *implement* phase of
independent slices only. The orchestrator still observes the Verify/QA/PR gates
(ADR-0037) with the human — agents do not self-merge or skip gates.

## Consequences

**Positive:** multiple slices in flight at once; wall-clock is no longer
serialized on a single PR's CI; each branch's CI is independent; worktree
isolation means parallel agents can't corrupt each other's working tree.

**Negative / cost:** N concurrent **heavy e2e runs** (each boots the full
stack) — bounded by batch size and Actions concurrency; the orchestrator must
enforce the disjoint-file rule and the merge order, or parallel branches hit
rebase conflicts on the contention files above.

## Alternatives considered

- **Workflow-orchestrated fan-out (single session, one PR) — complementary, not
  the default.** Good for repetitive bursts (e.g. N similar specs) synthesized
  back into one PR, but it's one PR and one CI run — less real parallelism than
  branch-per-slice. Reach for it for fan-out-then-merge tasks.
- **Stacked PRs — rejected as the model.** A serial dependency chain; doesn't
  help genuinely independent slices and complicates rebase-only merges.
- **Lightweight methodology, no worktrees — rejected.** Relies on manual
  discipline to avoid clobbering working trees; `isolation: "worktree"` makes
  parallel agents safe by construction.
