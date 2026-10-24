# Plan 448: An ADR diff runs only the tests that read it

Spec: [spec.md](./spec.md). Issue #3122. Owner: `infra-engineer`. Reviewer: `infra-reviewer`.

**Phase 4a colour: behaviour-changing → red.** The classifier gains an output value
(`backend`), and four jobs change their gate.

## 1. Shape

No bounded context, domain or messaging is involved. The plan has four parts:

1. **`scripts/classify-ci-diff.sh`** (new). The `Classify the diff` step body moves out of
   `ci.yml` and into this script, so a Docker-free test can run it. This mirrors the existing
   `scripts/wait-for-e2e-stack.sh` + `.test.mjs` pair (bash run for real, driven by
   `node --test`, picked up by `test:guards`).
2. **`ci.yml`**: `changes` calls the script. The `docs_only` output is replaced by one output
   `bucket` ∈ {`none`,`backend`,`full`}, and each job's `if:` is retargeted (§3).
3. **`scripts/classify-ci-diff.test.mjs`** (new): behaviour tests plus one wiring assertion.
4. **Briefs**: `.claude/commands/next-issue.md` (merge read) and
   `.claude/agents/infra-engineer.md` / `infra-reviewer.md` (CI job enumerations).
   `AgentBriefClaimTests` checks those enumerations' `needs` claims against `ci.yml`, so they
   **must** change in the same commit as `ci.yml` or the backend job goes red.

## 2. Script contract

```
scripts/classify-ci-diff.sh <event_name> <base_sha> <head_sha>
  env: GITHUB_OUTPUT, GITHUB_STEP_SUMMARY (files; appended to)
  cwd: the repository
```

Invariants:
- `bucket=full` is the **first** write to `$GITHUB_OUTPUT`, before any git call. A later
  write overrides it, and GitHub keeps the last value. The script fails safe if it exits early.
- Every exit path calls `conclude "<reason>"` exactly once, which echoes to the log and
  appends `**changes:** <reason>` to the summary. The reason text **names the bucket**
  (`bucket=none|backend|full`) so the merge read can look for the bucket by name.
- The order of checks is unchanged: no usable base → unreachable base → `git diff
  --no-renames --name-only` failure → empty diff → per-path classification.
- Classification uses one `case` per path. `specs/*` is specs. `docs/adr/*|docs/runbooks/*|
  docs/design/*|.specify/memory/*|.claude/*|CLAUDE.md` is backend-readable (exact `CLAUDE.md`,
  root only). Anything else sets `other`. Result: `other` gives `full`, any backend-readable
  path gives `backend`, otherwise `none`.
- The path list exists **only** in the script. The `ci.yml` comment points to it, and the
  reader inventory (spec §1, five guards including `KioskRecoveryRecordTests`) is restated
  in the script header.
- `set -uo pipefail`. Not `-e`, because every failure needs its explicit full-bucket exit.
  The step must still exit 0 on every fail-safe path, as it does today.

## 3. Job gates (`ci.yml`)

| job | needs | if |
|---|---|---|
| `changes` | – | – |
| `backend` | `[changes]` | `needs.changes.outputs.bucket != 'none'` |
| `frontend` | `[changes]` | `needs.changes.outputs.bucket == 'full'` |
| `integration-shards` | `[backend, changes]` ← +changes | `== 'full'` |
| `integration-shard-coverage` | `[backend, changes]` ← +changes | `== 'full'` |
| `integration` | unchanged | `always() && … == 'full'` |
| `e2e-shards` | `[backend, frontend, changes]` ← +changes | `== 'full'` |
| `e2e-shard-coverage` | `[changes]` | `== 'full'` |
| `e2e` | unchanged | `always() && … == 'full'` |
| `render-leg-gate` | unchanged | unchanged (`!cancelled()`). It already reports a `skipped` upstream (`render-leg-gate-report.mjs:145`), which is the same path the specs/-only bucket takes today. |

Why `integration-shards` and `integration-shard-coverage` need an explicit gate: they need
only `backend`, which **runs** on the backend bucket, so without the gate they would start.
This is the crux of the change, and the wiring assertion covers it (§4).

Obligation 3 (the coverage guards still behave): both coverage guards keep their steps
unchanged. On `full` they run exactly as before. On `backend` they are skipped together with
the shards they guard, because a diff confined to these paths cannot change a shard
partition (`*.filter` lives under `tests/`, and Playwright specs live under `e2e/`, both
`full`).

## 4. Tests (`scripts/classify-ci-diff.test.mjs`, `node:test`)

- Helper: `mkdtemp` → `git init` → base commit (seed files) → change set → head commit. The
  helper runs the script with temp `GITHUB_OUTPUT` / `GITHUB_STEP_SUMMARY` and returns
  `{ lastBucket, summaryLines, status }`. The test skips only when `bash` is unavailable,
  which is the same rule `wait-for-e2e-stack.test.mjs` uses (never on ubuntu CI).
- Cases: every Gherkin scenario in spec §2, one `test()` each, including all seven near-miss
  paths and both rename directions.
- Every case asserts the exit status is 0, the last `bucket=` value, **exactly one** summary
  line, and that the summary line contains the bucket name.
- **Wiring assertion** (one test). Parse `ci.yml` per job block. `backend`'s `if` contains
  `bucket != 'none'`. Each of the seven jobs in §3 contains `bucket == 'full'`. The
  `integration-shards`, `integration-shard-coverage` and `e2e-shards` `needs` include
  `changes`. `changes` runs `scripts/classify-ci-diff.sh`. No `docs_only` remains. This reads
  the artefact (memory: *guards that read the design artefact*), so it is backed by the
  live throwaway-PR observation in phase 5.

Shard filters do not apply: this is a `node --test` file under `scripts/`, not an xUnit
class, and `test:guards` picks it up by glob.

## 5. Commit sequence (each builds; ADR-0087)

1. `docs(spec-448): …`: these three files (phase 3).
2. `test(3122): …`: red. Every case fails because the script does not exist, and the wiring
   test fails on `docs_only`. Quote the output.
3. `ci(3122): …`: the script, the `ci.yml` wiring and comment correction, and the three
   briefs together (`AgentBriefClaimTests` couples them).

## 6. Edits for the owner (lane may not make them)

- **CLAUDE.md**: no edit is required, because it does not describe the bucket read or the
  specs/-only skip. *Optional:* one line under "The autonomous lane" pointing at
  `next-issue.md`'s merge read as the place where skip exceptions are named.
- **ADR-0033 §Exemption** (an amendment note, owner's call): replace "`paths-ignore`;
  `docs/`, `specs/`, top-level `*.md`" with the three-bucket rule and its reader-inventory
  basis. Also record that the frontend job is skipped on the backend bucket even though the
  ADR says "format/lint still run on every PR including docs", because no frontend linter's
  scope includes these paths. Separately, the ADR names gitleaks and container-smoke jobs
  that `ci.yml` does not have (pre-existing drift, not this spec's).
- **Owner memory** `develop-has-no-required-status-checks.md` says "never merge on a run
  with a `skipping` bucket". It needs the same named exceptions as `next-issue.md`.

## 7. Risks

- **A future guard reads a backend-bucket path from a frontend, integration or e2e test.**
  The skip would bypass it silently. Mitigation: the script header carries the reader
  inventory and the rule. A cheap tripwire is listed as a follow-up and not built here (no
  speculative generality): a `test:guards` check that greps `apps/`, `e2e/`,
  `tests/Integration.Tests` for these roots.
- **A bash `case` glob `*` matches `/`.** That is intended for `docs/adr/*`, and it is why
  `src/docs/adr/x.md` is still `full`: `case` anchors the pattern at the start of the path.
  The near-miss tests pin this.
