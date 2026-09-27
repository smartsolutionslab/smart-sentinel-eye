# Spec 275: The skip that hid a cancel

**Issue:** [#2521](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2521),
*A cancelled backend job leaves e2e/integration reporting a bare 'skipped',
indistinguishable from a normal skip*. **Lane:** supervised (ADR-0037). A human
orchestrator drives phases 1 to 3, then hands phase 4 to `infra-engineer`. No per-task
issues. The board is not touched (orchestrator instruction; the issue is not in the
autonomous lane).

**Branch.** `fix-2521-ci-cancelled-vs-skipped`, cut from `origin/develop`.

**Spec number.** 275. The orchestrator was told 274 was free. It is not: on 2026-09-27
four other unmerged worktrees already hold a `specs/274-*` directory (`sse-2200`,
`sse-2358`, `sse-2490`, `sse-2523`). 273 is held by `sse-2632`. `origin/*` holds nothing
above 272. **Re-check before opening the PR** (memory: *spec number: origin/develop isn't
enough*). Those four 274s also collide with each other. That is outside this spec.

**ADRs and constitution sections referenced:** ADR-0037 (phases and gates), ADR-0139
and constitution §Testing (new behaviour is observed red first), ADR-0030 / ADR-0086
(commit format, no Co-Authored-By), ADR-0087 (rebase-merge, each commit stands alone).
Context only: ADR-0144 (the autonomous lane's merge read, which this output feeds).
**No ADR gap.** The issue's product owner chose the shape (option 2). This spec records
where option 2 is applied and why (§3). It makes no new architectural decision.

**Not applicable:** DDD, bounded contexts, value objects, persistence, messaging,
Shared.Contracts. This change touches only `.github/workflows/ci.yml`.

**Latency budget (§IV): N/A.** CI workflow only. No leg of the event-to-overlay path is
touched.

---

## 1. The problem, as it stands today

The issue was filed at 2026-09-22T02:37Z against the **pre-sharding** workflow. Spec 218
(commits `51319f4f`, `4de9d327`) landed on `develop` about 18 hours later and changed what
the issue describes. Both states are recorded here because the fix targets the second
one.

### 1.1 Before spec 218 (what the issue describes, observed)

Run **35067663094** (push to `develop`, 2026-09-16) is the #2409 hang:

```
run=cancelled push
  frontend - lint + typecheck + test            | success   | 07:16:09 -> 07:17:43
  backend - build + unit tests + coverage gate  | cancelled | 07:16:09 -> 07:36:24   (20m15s = timeout-minutes: 20)
  integration tests (Docker)                    | skipped
  e2e (Playwright, full stack)                  | skipped
```

Run 34504807442 (2026-09-10) has the same shape. Two of the four buckets say `skipped`.
Neither says why.

### 1.2 After spec 218 (today's `ci.yml`, traced, not yet observed on a timeout)

The two names the issue quotes still exist, but they now belong to **synthesis jobs**
(`integration`, ci.yml:548-565; `e2e`, ci.yml:857-871). Both carry `if: always()`. The
jobs that actually need `backend` are now `integration-shards` (matrix, :215),
`integration-shard-coverage` (:456) and `e2e-shards` (matrix, :583). `e2e-shard-coverage`
(:775) has no `needs:`. It runs regardless.

Traced against the YAML, a #2409 timeout now produces:

| Check | Conclusion | What it says |
|---|---|---|
| `backend - …` | cancelled | (the timeout) |
| `integration tests (Docker) (${{ matrix.shard }}/4)` | skipped | nothing |
| `integration shard coverage guard` | skipped | nothing |
| `e2e (Playwright, full stack) (${{ matrix.shard }}/4)` | skipped | nothing |
| `e2e shard coverage guard` | success | its own check |
| **`integration tests (Docker)`** | **failure** | `::error::integration-shards=skipped integration-shard-coverage=skipped (expected success/success).` |
| **`e2e (Playwright, full stack)`** | **failure** | `::error::e2e-shards=skipped e2e-shard-coverage=success (expected success/success).` |

So the bucket checks are no longer a bare `skipped`. They are now a **red that reads
exactly like a regression**. The annotation names only the direct needs, and says
`skipped`. It never names `backend`, and it never says `cancelled`. It cannot: the
synthesis jobs do not list `backend` in `needs:`, so `needs.backend.result` is not in
their context.

This is the same defect in a new form. The issue's constraint from #2247 applies: "a
cancellation that's impossible to mistake for 'did not apply here'". Today it is easy to
mistake for "this PR broke integration tests".

Observed evidence of the post-218 shape exists only for **workflow-level** cancellation
(a superseded run). Run 36285454885 (2026-09-27) shows `backend` cancelled, all three
dependents `cancelled`, and both synthesis jobs ran and ended `failure`. That confirms
`if: always()` jobs run through a cancellation. No `backend` **timeout** has happened
since spec 218 merged (searched: every `cancelled` and `failure` CI run created on or
after 2026-09-22). §1.2's table is therefore a trace, and the spec says so.

## 2. User stories

### US1 (P1): a cancelled upstream is named in the bucket check

As a human or a merge-gate script reading a PR's four CI buckets, when `backend` (or,
for e2e, `frontend`) was cancelled, I want `integration tests (Docker)` and
`e2e (Playwright, full stack)` to say, in an annotation and in the job summary, that the
upstream job was cancelled and that the bucket holds **no verdict** on this PR. Then I
re-run instead of hunting for a regression, and I never read the bucket as passed.

This is one story, one file, and independently shippable.

**Acceptance scenarios**

```gherkin
Scenario: happy path. backend succeeds (behaviour unchanged)
  Given backend and frontend conclude success
  And every integration and e2e shard and both coverage guards conclude success
  When the synthesis jobs run
  Then "integration tests (Docker)" and "e2e (Playwright, full stack)" conclude success
  And neither emits an annotation

Scenario: backend cancelled (the #2409 timeout)
  Given backend concludes cancelled
  And integration-shards, integration-shard-coverage and e2e-shards are therefore skipped
  When the synthesis jobs run
  Then "integration tests (Docker)" concludes failure
  And it emits an error annotation titled "Upstream cancelled (backend)"
  And the message says the shards never ran and the bucket holds no verdict on this PR
  And the job summary carries one line saying the same
  And "e2e (Playwright, full stack)" does the same, naming backend

Scenario: frontend cancelled, backend succeeded (e2e only)
  Given frontend concludes cancelled and backend concludes success
  When the e2e synthesis job runs
  Then it concludes failure with an annotation naming "frontend" as the cancelled upstream
  And the integration synthesis job is unaffected (it does not need frontend)

Scenario: both upstreams cancelled
  Given backend and frontend both conclude cancelled
  Then the e2e synthesis annotation title is "Upstream cancelled (backend frontend)"

Scenario: genuine regression is still a plain failure (conflict case, must not be relabelled)
  Given backend concludes success
  And integration-shards concludes failure
  When the integration synthesis job runs
  Then it concludes failure with the existing "expected success/success" annotation
  And it does NOT carry the "Upstream cancelled" title
  And that annotation now also shows backend=success

Scenario: backend genuinely failed (not cancelled)
  Given backend concludes failure
  Then the synthesis jobs conclude failure with the existing annotation
  And that annotation shows backend=failure, so the upstream is named
  And the "Upstream cancelled" title is NOT used (backend's own red is the explanation)

Scenario: a cancellation is never reported as success (bad-input guard, from #2247)
  Given any upstream concludes cancelled
  Then no synthesis job concludes success or skipped
```

Auth scenario: **N/A.** The workflow's permissions and secrets are unchanged. The new
step reads only `needs.*.result`, which is a GitHub-controlled enum (`success`,
`failure`, `cancelled`, `skipped`), so it creates no injection surface.

**Independent end-to-end test procedure** (plan §5, tasks T001/T005)

1. Locally, run each synthesis step's shell body under `bash -e` against the full
   upstream × needs truth table. Record stdout, annotations and exit code per row. Run it
   against today's body first (red for the cancelled rows), then the new body (green).
2. `actionlint` via Docker over `ci.yml`. It rejects a `needs.<job>` reference to a job
   not listed in `needs:`, which is exactly the mistake §3 has to avoid.
3. Live counterfactual on a throwaway branch: a scratch workflow whose stand-in `backend`
   sleeps past `timeout-minutes: 1`. Two synthesis jobs downstream run the old and new
   step bodies side by side. Read the annotations back with `gh api`. The branch and
   workflow are deleted afterwards. Nothing merges.
4. This PR's own CI is green. That proves only the success path (scenario 1).

## 3. Decision: where option 2 is applied

The product owner chose option 2: distinguish `needs.backend.result == 'cancelled'` from
the normal gate, and post an annotation or summary line naming the cancelled upstream,
either in "the dependent jobs' `if:`" or in "a small always-run gate job". Options 1 and
3 were rejected. This spec does not revisit that.

**Applied in the existing always-run gate jobs:** `integration` and `e2e`. Each gains
its upstream(s) in `needs:` and a cancelled-upstream branch in its check step. The shard
and coverage-guard jobs are **not changed**. Their `skipped` is now an accurate
statement ("did not run"), and the bucket check beside them explains it. The reasoning,
including why changing the shard jobs' own `if:` was rejected, is in plan §2.

**Premise correction for the Phase 1 gate.** The orchestrator's brief assumed #2521 is
about the shard jobs. The issue names `integration tests (Docker)` and
`e2e (Playwright, full stack)`, and those names now belong to the synthesis jobs (§1.2).
The reviewer should confirm this reading at the gate.

## 4. Non-goals

- Fixing the #2409 hang, or changing any `timeout-minutes`.
- Changing any job's conclusion to something other than `failure` on a cancelled
  upstream. A green or skipped bucket after a cancellation is exactly what #2247 forbids.
- Relabelling a shard's **own** cancellation (for example `integration-shards` hitting its
  30-minute timeout while `backend` succeeded). The existing annotation already prints
  `integration-shards=cancelled`. Candidate follow-up, not filed.
- Required status checks (#2288) and `gh pr checks --watch` tooling (option 3).

## 5. Success criteria

- SC-1: For every truth-table row where an upstream is `cancelled`, the synthesis step
  exits 1 and prints `::error title=Upstream cancelled (<names>)::`. It is also verified
  against the current body, which does **not** do this (red).
- SC-2: For every row where all upstreams and needs are `success`, the step exits 0 and
  prints nothing, identical to today.
- SC-3: For non-cancelled failures, the existing `expected success/success` annotation is
  unchanged, apart from the added `backend=` (and for e2e, `frontend=`) fields.
- SC-4: `actionlint` reports zero findings on `ci.yml`.
- SC-5: The live scratch run shows, for its timed-out stand-in, `needs.<job>.result ==
  'cancelled'`, the synthesis job running, and the new annotation retrievable via
  `gh api …/check-runs/<id>/annotations`.
