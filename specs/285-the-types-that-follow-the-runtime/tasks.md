# Tasks: Spec 285, the types that follow the runtime

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2645 · **Lane:** supervised
**Phase-4a colour:** **RED (behaviour-changing).** The guard is new behaviour (spec §2,
plan §5): a doubly-red sequence (module absent, then a stub returning `[]`) precedes the
real module, both runs quoted in the PR (ADR-0139).
**Engineers:** `test-writer` (4a, already committed `scripts/node-types-alignment.test.mjs`
at `02335108`), then `infra-engineer` for 4b.

No `[P]` markers: two new files that depend on each other (plan §6), strictly sequential.

**Board.** The feature issue is #2645, already on Project #13 (CLAUDE.md, Phase 3). No
per-task issues.

**Branch.** `chore-2645-types-follow-the-runtime`, cut from `origin/develop` at `edf41b97`
(already pushed as draft PR #2664).

**Contention files (ADR-0109):** none — `package.json`, `pnpm-lock.yaml` and
`.github/workflows/ci.yml` are read, never written (spec FR-001, SC-003).

**Per-commit rule (ADR-0087).** `pnpm test:guards` must pass on the module's own commit.
Conventional Commits, no `Co-Authored-By` trailer (ADR-0086).

---

## Phase 4a: red (`test-writer`, done)

- [x] **T001** `scripts/node-types-alignment.test.mjs` committed alone (`02335108`); red run 1
  (`ERR_MODULE_NOT_FOUND` on every case) and red run 2 (a `[]`-returning stub — every
  case expecting a problem fails for its own reason) both quoted in the PR (plan §5).

## Phase 4b (`infra-engineer`)

- [ ] **T010** `scripts/node-types-alignment.mjs`: `readRepositoryInputs` (the only I/O —
  every `.github/workflows/*.yml`/`*.yaml` file plus root `package.json`) and
  `checkNodeTypesAlignment` (plan §4.1/§4.2 — CI major per setup-node step, engines
  floor, types major, running major; the six problem kinds; checks 4–6 independent per
  their own readable inputs).
  - **Depends on:** T001.
- [ ] **T011** `node --test scripts/node-types-alignment.test.mjs` green, unmodified test
  file — verbatim output quoted in the PR.
  - **Depends on:** T010.
- [ ] **T012** Confirm `scripts/node-types-alignment.mjs` is picked up by the existing
  `test:guards` glob (`scripts/**/*.test.mjs`) with no `package.json` edit.
  - **Depends on:** T011.
- [ ] **T013** Four counterfactuals against the real `.github/workflows/*.yml` and root
  `package.json` (SC-002): `@types/node` → `^26.6.3`; one setup-node step → `'24'`;
  `engines.node` floor → `^24.15.0`; one step → `lts/*`. Each edited, run, reverted with
  `git checkout --`, `git status` confirmed clean before the next — all four outputs
  quoted in the PR.
  - **Depends on:** T011.
- [ ] **T014** Local gates: `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm format:check`
  (memory: *typecheck:e2e fails on a clean develop* — check before blaming the branch).
  - **Depends on:** T012, T013.

## Phase 5–7 (orchestrator / reviewers)

- [ ] **T020** Phase 5: CI's frontend job green on the PR — the guard running under the
  real Node 22 runner is the end-to-end observation; cite the job's `test:guards` output
  line for this file.
  - **Depends on:** T014.
- [ ] **T021** Phase 6: `infra-reviewer` (CI/tooling). No security surface.
  - **Depends on:** T014.
- [ ] **T022** Phase 7: PR body carries `Closes #2645`, spec §1's measurement table, both
  red runs (T001) and the four counterfactuals (T013). Confirm #2645 is closed after
  merge (memory: a PR mention rarely auto-closes the issue).
  - **Depends on:** T020, T021.
