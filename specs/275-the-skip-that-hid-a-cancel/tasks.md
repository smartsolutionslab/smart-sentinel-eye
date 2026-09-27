# Tasks: Spec 275, the skip that hid a cancel

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2521 · **Lane:** supervised (ADR-0037)
**Engineer:** `infra-engineer` (phase 4). **Reviewer:** `infra-reviewer` (phase 6).
**Board:** not touched, and no per-task issues (orchestrator instruction).

**Phase-4a colour: RED (behaviour-changing).** The workflow's observable output changes.
On a cancelled upstream, the bucket check's annotation goes from
`integration-shards=skipped …` to a titled `Upstream cancelled (backend)`, with a
no-verdict message and a step-summary line. That is new behaviour, not a reshaping.
"Red" here means the truth-table harness (plan §5.1) fails on the cancelled rows when run
against `origin/develop`'s step bodies, with the output quoted verbatim, **before**
`ci.yml` is edited. After that, the harness may not be edited to reach green. The
historical run 35067663094 (spec §1.1) is quoted as the pre-spec-218 baseline. It is
context, not the red: it predates today's synthesis jobs.

**Parallelism.** There is none worth having. Every implementation task edits the one
contention file `.github/workflows/ci.yml`, so T003 and T004 are sequential. T001 and
T002 read only, and may run in parallel (`[P]`). There is no foundational blocker beyond
T001's red capture, which gates T003.

**Commits (ADR-0030, ADR-0086, ADR-0087).**
1. `docs(spec-275): specify naming a cancelled upstream in the CI bucket checks`, the
   three artifacts.
2. `fix(ci): name a cancelled upstream in the integration and e2e bucket checks`, the
   `ci.yml` edit (T003 and T004 together, so the two buckets never disagree at any
   commit).

No Co-Authored-By trailer. Each commit stands alone.

---

## Phase 4a: red (`infra-engineer`)

- [ ] **T001 [P] [US1]** Write the truth-table harness in the scratchpad (not committed),
  as described in plan §5.1. Extract each synthesis step's `run: |` body from
  `.github/workflows/ci.yml` by step name, parameterised by file path, so the identical
  harness runs against `git show origin/develop:.github/workflows/ci.yml` and the edited
  working copy. Rows:
  - integration: backend ∈ {success, failure, cancelled}, shards/guard ∈ {success,
    failure, skipped, cancelled}, restricted to combinations GitHub can produce.
  - e2e: the backend × frontend product.

  Assertions: SC-1, SC-2 and SC-3 (spec §5). **Run it against `origin/develop`. The
  cancelled rows must fail SC-1.** Quote the output verbatim. That output is the phase-4
  red evidence for the PR body. Then show the counterfactual: a scratch copy with the new
  body minus its `exit 1` turns the harness red (plan §5.1).
  *Done when:* red output is captured and the counterfactual is shown.
- [ ] **T002 [P] [US1]** Run the `actionlint` baseline on the unchanged file (plan §5.2
  command; set `MSYS_NO_PATHCONV=1`). Record findings verbatim, including "none". This is
  the comparison point for T005.

## Phase 4b: implement (`infra-engineer`). One file, sequential.

- [ ] **T003 [US1]** Edit `.github/workflows/ci.yml` `integration` job (:530-565 at
  `1a51bd18`) exactly as in plan §3.1: add `backend` to `needs:`, rename and extend the
  check step, add the cancelled branch with the titled annotation and step-summary line,
  add `backend=` to the generic annotation, and add the one-sentence #2521 note to the
  header comment. Depends on T001.
- [ ] **T004 [US1]** Edit the `e2e` job (:851-871) exactly as in plan §3.2: add `backend`
  and `frontend` to `needs:`, and add the multi-upstream `cancelled` accumulator. Confirm
  the header comment still reads true. Depends on T003 (same file).

## Phase 5: verify (`infra-engineer`, then orchestrator)

- [ ] **T005 [US1]** Re-run T001's harness **unmodified** against the edited file. All
  rows must be green. Re-run `actionlint`: no findings beyond T002's baseline. Quote both
  verbatim.
- [ ] **T006 [US1]** Live counterfactual (plan §5.3) on the throwaway branch
  `scratch/2521-cancel-probe`. **The orchestrator's go-ahead is needed before any push.**
  Confirm, by observation:
  - `backend` is `cancelled` via its timeout.
  - `shards` is `skipped`.
  - Both synthesis jobs ran.
  - The before annotation reads `shards=skipped …`.
  - The after annotation title is `Upstream cancelled (backend)`, fetched via
    `gh api …/check-runs/<id>/annotations`.
  - The summary line is present.

  Delete the branch afterwards (`git push origin --delete scratch/2521-cancel-probe`) and
  confirm its runs are finished. If the go-ahead is withheld, record SC-5 as **not
  observed**. Do not record it as passed.
- [ ] **T007 [US1]** Write the PR body verification note:
  - T001 red.
  - T005 green.
  - T002/T005 actionlint.
  - T006 observation, or its absence.
  - The statement that this PR's own green CI exercises only the all-success path and
    proves nothing about the cancelled path.
  - The residual (plan §2: per-shard checks still `skipped`).
  - The premise correction (spec §3).

  Latency: N/A (CI only). Recheck the spec number (spec header) before opening the PR,
  and rename the directory if 275 has since been taken.

## Phase 6 and 7 (orchestrator)

- [ ] **T008** Run `infra-reviewer` on the diff. Ask specifically whether the plan §2
  residual is acceptable, and whether the title escaping holds on the runner.
- [ ] **T009** Open the PR with `gh pr create --base develop`, with `Closes #2521` in the
  body. After the merge, check that #2521 actually closed (memory: *a PR mention rarely
  auto-closes the issue*). Leave a closing comment naming what to look for on the next
  real #2409 timeout: the `Upstream cancelled (backend)` title on both bucket checks. No
  new tracking issue (plan §5.5).

## Dependencies

```
T001 ─┬─> T003 ─> T004 ─> T005 ─> T006 ─> T007 ─> T008 ─> T009
T002 ─┘                    ^
                           └── T002 baseline is compared here
```
