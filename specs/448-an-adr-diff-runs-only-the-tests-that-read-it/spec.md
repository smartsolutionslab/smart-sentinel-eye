# Spec 448: An ADR diff runs only the tests that read it

**Issue:** [#3122](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3122),
*CI: an ADR/docs-only diff should run the backend job (Architecture.Tests guards) but skip
the Docker integration and e2e shards*. **Lane:** autonomous (ADR-0144), `agent:ready`.
Phase 4 owner: `infra-engineer`; phase 6 reviewer: `infra-reviewer`. No per-task issues.

**Branch.** `ci/3122-docs-only-bucket`, cut from `origin/develop`.

**Spec number.** 448. `origin/develop` tops out at 445; open PR #3132 holds 440; the
`sse-3127` and `sse-3078` worktrees may claim 446 and 447, so this spec skips them.
**Re-check before opening the PR** (memory: *spec number: origin/develop isn't enough*).

**ADRs referenced:** ADR-0033 (CI gates; its *Exemption* already grants docs-only PRs a
skip of integration: this spec implements that grant, narrowed to what is safe), ADR-0144
(the lane's manual merge read, which this output feeds), ADR-0037 (phases), ADR-0139 and
constitution §Testing (new behaviour observed red first), ADR-0030 / ADR-0086 (commits),
ADR-0087 (each commit stands alone).

**ADR needed? No.** The classification rule is recorded in `ci.yml` comments and commit
`3ebd6f4a` (the specs/-only bucket), not in an ADR, and ADR-0033 already decided that a
docs-only PR skips integration. This spec applies one existing rule (*a path's bucket is the
narrowest job set containing every test that reads it*) to more paths. It is not a new
decision. ADR-0033's own text has drifted from the workflow (it says `paths-ignore`, names
`docs/` and top-level `*.md` wholesale, and lists gitleaks and container-smoke jobs that do
not exist). That drift predates this spec, and the lane may not amend an ADR. It is listed
for the owner in plan.md §6.

**Not applicable:** DDD, bounded contexts, value objects, persistence, messaging,
Shared.Contracts. **Latency budget (§IV): N/A.** This is CI only and touches no leg.

---

## 1. Obligation 1: who reads these paths (verified 2026-10-24, before design)

Grepped `tests/**` (all projects), `e2e/**`, `apps/**`, `scripts/**` and every job in
`.github/workflows/ci.yml` for reads of `docs/`, `CLAUDE.md`, `.specify/`, `.claude/`.

| Path | Read by (test file) | CI job that runs it |
|---|---|---|
| `docs/adr/**` | `AgentBriefClaimTests`, `FoundingDecisionRecordTests`, `KioskRecoveryRecordTests`, `OverlayFrameClaimTests` | `backend` (Architecture.Tests) |
| `CLAUDE.md` | `OverlayFrameClaimTests` | `backend` |
| `.specify/memory/**` | `FoundingDecisionRecordTests`, `KioskRecoveryRecordTests`, `LatencyLegRecordTests`, `OverlayFrameClaimTests` | `backend` |
| `.claude/**` | `AgentBriefClaimTests` (`agents/`, `commands/`) | `backend` |
| `docs/runbooks/**`, `docs/design/**` | nothing | none |
| `.specify/scripts/**` | `scripts/create-new-feature.test.mjs` | **`frontend`** (`pnpm test` → `test:guards`) |
| `docs/deployment-frontend-env.md` | nothing (named in `apps/*` error strings only) | none |

Not readers: `tests/Integration.Tests` reads `.github/workflows/ci.yml`, `src/**`, and the
realm JSON. It reads no doc path. `e2e/**` reads none. The frontend `format:check` is scoped
to `{apps,e2e}/**`. `pre-commit.test.mjs` names `docs/…` only as synthetic paths in a temp
repo. The other hits are comments (`CLAUDE.md` cited in test prose), not reads.
`ci.yml`'s current comment lists four guards. `KioskRecoveryRecordTests` is a fifth, and
the comment is corrected.

**Finding that decides the design:** every path the issue proposes is read only by
`backend`, **except `.specify/` as a whole**. So the backend bucket takes
`.specify/memory/` only, and `.specify/scripts/` and `.specify/templates/` stay full.
`docs/runbooks/` and `docs/design/` are read by nothing. They join the **backend** bucket,
not the skip bucket: a future Architecture.Tests guard over a runbook would otherwise be
bypassed without anyone noticing, which is exactly how `3ebd6f4a` reasoned about docs. Every
other `docs/` path stays full. Nothing in `docs/` outside these three directories is
proposed.

## 2. User story

### US1 (P1): An ADR-only PR finishes in backend time, not full-pipeline time

As the owner merging an ADR or brief edit, I want CI to run the Architecture.Tests guards
that read it and skip the Docker integration and Playwright shards that cannot, so the PR
needs about 25 minutes less CI time and its guards still run.

**Buckets** (one output, `bucket`, from the `changes` job):

| bucket | when (every changed path, `--no-renames`) | jobs that run |
|---|---|---|
| `none` | under `specs/` | none (unchanged behaviour) |
| `backend` | under `specs/`, `docs/adr/`, `docs/runbooks/`, `docs/design/`, `.specify/memory/`, `.claude/`, or is exactly `CLAUDE.md`, **and at least one is not under `specs/`** | `backend` only |
| `full` | anything else, or any unreadable or ambiguous diff | everything (unchanged) |

**Frontend decision (obligation 5): skip on `backend`.** The frontend job (format:check on
`{apps,e2e}`, lint, typecheck, vitest, `test:guards`) reads none of the backend-bucket paths
(§1), so it cannot produce a signal on such a diff. ADR-0033's "format/lint still run on
docs PRs" is met where a linter covers the changed files: `dotnet format` runs in `backend`,
and no frontend linter's scope includes these paths. `e2e-shards` is gated explicitly as
well as through its `frontend` need, so a later change to frontend's gate cannot quietly
start the e2e shards.

### Acceptance scenarios

```gherkin
Scenario: ADR-only diff runs backend only (happy)
  Given a PR whose only change is docs/adr/0160-x.md
  When CI runs
  Then `changes` outputs bucket=backend and its step summary names the backend bucket
  And `backend` runs
  And frontend, integration-shards, integration-shard-coverage, integration,
      e2e-shards, e2e-shard-coverage and e2e are skipped

Scenario: each backend-bucket root classifies alone
  Given a diff touching only one of CLAUDE.md, .specify/memory/constitution.md,
        .claude/agents/x.md, .claude/commands/x.md, docs/runbooks/x.md, docs/design/x.md
  Then bucket=backend

Scenario: specs plus an ADR is backend, not none
  Given a diff touching specs/999-x/spec.md and docs/adr/0160-x.md
  Then bucket=backend

Scenario: specs-only is unchanged
  Given a diff touching only specs/
  Then bucket=none and the summary names the specs/-only skip

Scenario: one code file alongside an ADR runs everything (conflict)
  Given a diff touching docs/adr/0160-x.md and src/Cameras/Domain/Camera.cs
  Then bucket=full

Scenario: near-miss paths are not the bucket (conflict)
  Given a diff touching only one of .specify/scripts/powershell/x.ps1,
        .specify/templates/x.md, docs/deployment-frontend-env.md, docs/adrx.md,
        apps/CLAUDE.md, src/docs/adr/x.md, CLAUDE.md.bak
  Then bucket=full

Scenario: a rename out of code into docs runs everything (conflict)
  Given `git mv src/Foo.cs docs/adr/Foo.md` (and, separately, into specs/)
  Then bucket=full, because --no-renames reports the deleted src path

Scenario: an unreadable diff runs everything (bad request)
  Given an empty diff, an all-zero base, an empty base (workflow_dispatch),
        or a base commit that is not reachable
  Then bucket=full and the summary names the reason

Scenario: classification can never fail open (bad request)
  Given the classifier exits at any point
  Then bucket=full was written before any decision, and every exit writes one summary line

Scenario: the merge read stays honest (auth: who may treat a skip as green)
  Given a run where integration/e2e are skipped
  Then the reader treats that as green only if `changes` succeeded and its summary names
       a bucket (none or backend) consistent with the skipped jobs, and on `backend`,
       the `backend` job itself concluded success
```

### Independent end-to-end test procedure

1. `node --test scripts/classify-ci-diff.test.mjs` runs every scenario above against real
   throwaway git repositories (no Docker).
2. After the feature PR is pushed (its own diff is `full`, and every job runs), open a
   **throwaway draft PR stacked on the feature branch** whose only change is a blank line in
   `docs/adr/README.md`. Observe that the `changes` summary reads backend, `backend` is green,
   and the seven jobs above are skipped. Record the run URL in the verification note, then
   close the throwaway PR unmerged and delete its branch.
3. Counterfactuals (memory: *prove a guard by counterfactual*): drop the `docs/adr/` arm from
   the script and the backend cases go red. Revert one job's `if:` and the wiring assertion
   goes red.

## 3. Out of scope

- Splitting the cross-side guards so that a frontend-only or backend-only code diff can skip
  the other side (rejected in `3ebd6f4a`, still rejected).
- `docs/` root files and any `docs/` directory beyond the three named.
- Making checks required on `develop` (repo setting, owner's call).
- Amending ADR-0033 or CLAUDE.md (lane-forbidden, listed for the owner in plan.md §6).
