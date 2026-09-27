# Plan: Spec 275, the skip that hid a cancel

**Spec:** `spec.md` · **Issue:** #2521 · **Lane:** supervised (ADR-0037)
**File touched:** `.github/workflows/ci.yml` only. Two jobs: `integration` (:530-565) and
`e2e` (:851-871). Line numbers are from `origin/develop` at `1a51bd18`.

## 1. Context that shapes the design

- **Triggers and concurrency (ci.yml:3-17).** `pull_request` (unfiltered), `push` to
  `develop`/`main`, and `workflow_dispatch`. `concurrency: ci-${{ github.workflow }}-${{
  github.ref }}` with `cancel-in-progress: true`. So `backend` is cancelled in two
  distinct ways:
  1. **Workflow-level:** a newer push supersedes the run. This is the common case (about
     14 of them since 2026-09-22). Every unstarted dependent reports `cancelled`, and
     `if: always()` jobs still run (observed: run 36285454885).
  2. **Job-level:** `backend` exceeds `timeout-minutes: 20` (the #2409/#2247 hang). The
     job reports `cancelled` (observed: run 35067663094, 20m15s). Its dependents with the
     implicit `success()` gate report `skipped`. This is the case the issue is about.

  This plan does not try to tell the two apart. Both mean "this run holds no verdict",
  and the annotation text covers both (§3).
- **A job's `needs` context contains only its direct needs.** The synthesis jobs today
  list only the shard and guard jobs, so `needs.backend.result` is unreadable there.
  Referencing it without adding `backend` to `needs:` evaluates to empty, and
  `actionlint` flags it. §3 adds it.
- **Adding `backend` (and `frontend`) to a synthesis job's `needs:` changes neither when
  it starts nor whether it runs.** It already waits for them transitively, and
  `if: always()` stays.
- **Workflow command property escaping.** In `::error title=…::msg`, a property value
  must not contain a raw `:` or `,` (the runner expects `%3A`/`%2C`). The title is
  therefore `Upstream cancelled (backend)`, with parentheses and spaces only. Colons are
  fine in the message part.
- **No path filters.** The workflow has no `paths:`/`paths-ignore:` at any level, so
  "skipped because the diff didn't touch it" is not something this workflow does. A
  `skipped` dependent always means an upstream did not succeed.

## 2. Design decision

Three shapes fall within option 2. The chosen one is **C**.

### (A) Rejected: explicit `if:` plus a first-step gate in each shard and guard job

Give `integration-shards`, `integration-shard-coverage` and `e2e-shards` an explicit
`if: always() && (needs.backend.result == 'success' || needs.backend.result ==
'cancelled')`, and make the first step `exit 1` with the annotation when the result is
`cancelled`.

- **The `always()`/`cancelled()` steps would still fire after the gate fails.** That is 2
  in `integration-shards` (:363 upload results, :438 container logs) and 6 in
  `e2e-shards` (:688 retry summary, :700 render-leg summary, :713 AppHost log, :720 stop,
  :742 scrub, :746 upload). They would run Node scripts against an empty checkout, print
  "no report" noise, and one is the #2287 credential-scrub gate, a security control.
  Every one would need re-guarding with `&& steps.<gate>.outcome == 'success'`. That
  means 8 edited conditions to deliver one annotation, and a regression risk in the scrub
  gate.
- **Runner cost on every superseded run.** `always()` jobs run through a workflow
  cancellation, so every concurrency-cancelled run would start 9 runners (4 + 1 + 4) to
  print one line each. `!cancelled()` would avoid that, but whether job-level
  `cancelled()` is also true after a need's *timeout* is not established in this repo.
  Building the fix on it would add an unverified premise.
- **Wrong target.** The issue names `integration tests (Docker)` and `e2e (Playwright,
  full stack)`. Since spec 218, those names belong to the synthesis jobs (spec §1.2).

### (B) Rejected: a new gate job per chain

A new `integration-gate` and `e2e-gate` with `needs: [backend, …]` and `if: always()`,
plus the shard jobs gaining `needs: [<gate>]`. This adds two check names and duplicates
what the synthesis jobs already are: an always-run job at the end of each chain whose only
purpose is to state the chain's verdict. It also leaves the shard jobs `skipped`, the same
as C does.

### (C) Chosen: the existing always-run synthesis jobs name the cancelled upstream

`integration` and `e2e` are already "a small always-run gate job" (option 2's own words):
`if: always()`, a `needs.*.result` check, `::error::`, `exit 1`. The change:

1. Add the upstream(s) to `needs:`: `backend` for `integration`; `backend` and
   `frontend` for `e2e`.
2. Before the existing check, if any upstream is `cancelled`: emit
   `::error title=Upstream cancelled (<names>)::…no verdict…`, append one line to
   `$GITHUB_STEP_SUMMARY`, and `exit 1`.
3. Add `backend=` (and `frontend=`) to the existing generic annotation, so a genuine
   upstream **failure** names its upstream too.

Conclusions do not change. On a cancelled upstream the bucket is `failure`, as today,
because a non-red bucket would violate #2247's constraint. What changes is that the
annotation title, the first thing on the run page and the field a script reads from
`GET /repos/{o}/{r}/check-runs/{id}/annotations`, now separates "no verdict" from
"regression".

**Residual, accepted.** The per-shard checks
(`integration tests (Docker) (${{ matrix.shard }}/4)`, `integration shard coverage
guard`, `e2e (Playwright, full stack) (${{ matrix.shard }}/4)`) still show `skipped` after
a timeout. With no path filters (§1), `skipped` there is literally true: they did not run.
The bucket check beside them now says why. Record this in the PR body so the reviewer can
weigh it.

## 3. Literal before and after

### 3.1 `integration` (ci.yml:548-565)

Before:

```yaml
  integration:
    name: integration tests (Docker)
    runs-on: ubuntu-latest
    needs: [integration-shards, integration-shard-coverage]
    if: always()
    steps:
      # Phase 6 review S5: names the actual needs.*.result values so a reader
      # can tell "genuinely red" from "superseded" (e.g. cancelled by a
      # force-push landing in the same concurrency group) rather than both
      # reading as an identical, unqualified "did not succeed".
      - name: Check shard and coverage-guard results
        run: |
          shards="${{ needs.integration-shards.result }}"
          guard="${{ needs.integration-shard-coverage.result }}"
          if [ "$shards" != "success" ] || [ "$guard" != "success" ]; then
            echo "::error::integration-shards=$shards integration-shard-coverage=$guard (expected success/success)."
            exit 1
          fi
```

After:

```yaml
  integration:
    name: integration tests (Docker)
    runs-on: ubuntu-latest
    needs: [backend, integration-shards, integration-shard-coverage]
    if: always()
    steps:
      # Phase 6 review S5: names the actual needs.*.result values so a reader
      # can tell "genuinely red" from "superseded" rather than both reading as
      # an identical, unqualified "did not succeed".
      #
      # #2521: `backend` is a direct need only so its result is readable here
      # (the needs context holds direct needs alone). When it was cancelled --
      # a #2409 timeout or a superseded run -- the shards were skipped, never
      # ran, and this check holds no verdict on the PR. It stays red (a
      # cancellation must never read as a pass, #2247), but says so in the
      # title, which is what separates it from a regression.
      - name: Check upstream, shard and coverage-guard results
        run: |
          backend="${{ needs.backend.result }}"
          shards="${{ needs.integration-shards.result }}"
          guard="${{ needs.integration-shard-coverage.result }}"
          if [ "$backend" = "cancelled" ]; then
            echo "::error title=Upstream cancelled (backend)::backend was cancelled, so integration-shards and integration-shard-coverage never ran. This check holds no verdict on this PR: re-run the workflow (or read the newer run, if this one was superseded). Do not read it as a regression."
            echo "**integration tests (Docker): no verdict.** Upstream \`backend\` was cancelled; the shards never ran. Re-run the workflow." >> "$GITHUB_STEP_SUMMARY"
            exit 1
          fi
          if [ "$shards" != "success" ] || [ "$guard" != "success" ]; then
            echo "::error::backend=$backend integration-shards=$shards integration-shard-coverage=$guard (expected success/success)."
            exit 1
          fi
```

Also update the job's header comment (:538-547, "Phase 6 review S5 …"). Add one sentence
saying the cancelled-upstream case now carries its own title (#2521), so a reader of the
comment is not told the log line is the only discriminator.

### 3.2 `e2e` (ci.yml:857-871)

After (before is symmetric to 3.1: `needs: [e2e-shards, e2e-shard-coverage]`, two
variables, one generic `::error::`):

```yaml
  e2e:
    name: e2e (Playwright, full stack)
    runs-on: ubuntu-latest
    needs: [backend, frontend, e2e-shards, e2e-shard-coverage]
    if: always()
    steps:
      # Phase 6 review S5 and #2521: same reasoning as `integration`'s identical
      # step. e2e-shards needs frontend as well as backend, so either (or both)
      # can be the cancelled upstream.
      - name: Check upstream, shard and coverage-guard results
        run: |
          backend="${{ needs.backend.result }}"
          frontend="${{ needs.frontend.result }}"
          shards="${{ needs.e2e-shards.result }}"
          guard="${{ needs.e2e-shard-coverage.result }}"
          cancelled=""
          if [ "$backend" = "cancelled" ]; then cancelled="backend"; fi
          if [ "$frontend" = "cancelled" ]; then cancelled="${cancelled:+$cancelled }frontend"; fi
          if [ -n "$cancelled" ]; then
            echo "::error title=Upstream cancelled ($cancelled)::Upstream job(s) cancelled: $cancelled. e2e-shards never ran. This check holds no verdict on this PR: re-run the workflow (or read the newer run, if this one was superseded). Do not read it as a regression."
            echo "**e2e (Playwright, full stack): no verdict.** Upstream \`$cancelled\` cancelled; the shards never ran. Re-run the workflow." >> "$GITHUB_STEP_SUMMARY"
            exit 1
          fi
          if [ "$shards" != "success" ] || [ "$guard" != "success" ]; then
            echo "::error::backend=$backend frontend=$frontend e2e-shards=$shards e2e-shard-coverage=$guard (expected success/success)."
            exit 1
          fi
```

The e2e header comment (:851-856) points at `integration`'s. No edit is needed beyond
confirming it still reads true.

**Precedence note.** `e2e-shard-coverage` has no needs, so it can genuinely fail in the
same run in which `backend` is cancelled. The cancelled branch wins in the synthesis
annotation. The guard's own failure stays visible on its own check,
`e2e shard coverage guard`, as today. This is accepted: that check is red and named
beside the bucket.

**Shell safety.** Every conditional uses `if … fi`, never `[ … ] && …` as a bare line,
so the runner's default `bash -e` cannot abort on a false test. The interpolated values
are the four-value `result` enum only, so there is no injection surface and no need to
route through `env:` (the existing step interpolates the same way).

## 4. Constitution and ADR alignment

| Item | Status |
|---|---|
| ADR-0037 phased workflow | Phases 1-3 here. Phase 4 goes to `infra-engineer`. Phase 5 is the §5 evidence. Phase 6 goes to `infra-reviewer`. |
| Constitution §Testing / ADR-0139 | Behaviour-changing, so **red first**. The workflow's observable output changes. §5 gives a red that can fail. |
| ADR-0030 / ADR-0086 | `fix(ci): …`, no Co-Authored-By trailer. |
| ADR-0087 | Two commits (spec, then ci.yml). Each stands alone: the spec commit changes no behaviour, and the ci.yml commit parses on its own. |
| #2247 constraint | No cancellation becomes green or skipped. The bucket stays `failure`. |
| #2288 (no required checks) | Untouched. No check names are added or renamed, so any future required-check wiring sees the same names. |
| Architecture.Tests that read ci.yml as text | `IntegrationTestSelectionTests` parses `dotnet test` invocations. `e2e-shard-coverage` reads the `Run the Playwright e2e suite` step. Neither job is touched. The check step is renamed (`Check shard and coverage-guard results` becomes `Check upstream, shard and coverage-guard results`). A grep for the old name across the repo, excluding `specs/`, found it only in `ci.yml` itself (2026-09-27). Re-grep at phase 4. |
| §IV latency | N/A. |

## 5. Verification approach (phase 4a and 5)

"Red first" is applied to the **step logic**, which is executable shell, and then to the
**workflow semantics**, which only GitHub can evaluate.

1. **Truth-table harness (red, then green).** A scratch Bash script (scratchpad, not
   committed) uses `awk` to extract a synthesis step's `run: |` body from a given
   `ci.yml` revision. It substitutes each `${{ needs.X.result }}` with a row value, points
   `GITHUB_STEP_SUMMARY` at a temp file, runs the body under `bash -e`, and records exit
   code, stdout and summary. Rows: upstreams ∈ {success, failure, cancelled} (for e2e,
   the backend × frontend product) × the needs values each implies. Assertions are SC-1 to
   SC-3.
   - **Red:** run against `origin/develop`'s body. The cancelled rows must fail the SC-1
     assertion (no `title=Upstream cancelled`, no `backend` in the output). Quote the
     failing output verbatim.
   - **Green:** the same harness, unmodified, against the edited body.
   - **Counterfactual on the harness itself** (memory: *prove a guard by
     counterfactual*): delete the `exit 1` from the cancelled branch in a scratch copy,
     and show that the harness goes red. That proves it checks the exit code and not only
     the text.
2. **`actionlint`** via Docker, `MSYS_NO_PATHCONV=1 docker run --rm -v
   "D:/Github/sse-2521:/repo" -w /repo rhysd/actionlint:latest -color
   .github/workflows/ci.yml`. First run it on the unchanged file to record any
   pre-existing findings, then after the change: **no new findings**. Its expression
   checker flags `needs.backend` when `backend` is missing from `needs:`, and its bundled
   shellcheck covers the step bodies. No local Python YAML parser works on this machine
   (the WindowsApps stub), and `act` and `actionlint` are not installed. Docker is.
3. **Live counterfactual (the only observation of real GitHub semantics).** On a
   throwaway branch `scratch/2521-cancel-probe` (never a PR, deleted afterwards), push
   one file, `.github/workflows/scratch-2521.yml`, triggered by `on: push:
   branches: [scratch/2521-cancel-probe]`. `ci.yml`'s push trigger is limited to
   `develop`/`main`, so the real CI does not run. It contains:
   - `backend`: `timeout-minutes: 1`, `run: sleep 150`, which reproduces the #2409 timeout
     shape.
   - `shards`: `needs: [backend]`, implicit gate, `run: echo never`.
   - `synth-before`: `needs: [shards]`, `if: always()`, today's step body verbatim.
   - `synth-after`: `needs: [backend, shards]`, `if: always()`, the new body verbatim.

   Observe with `gh run view --json jobs` and `gh api
   repos/{o}/{r}/check-runs/<id>/annotations`: backend `cancelled`, shards `skipped`, both
   synths ran, the before annotation reads `shards=skipped …`, the after annotation title
   reads `Upstream cancelled (backend)`. This settles, by observation, the two premises
   that until now were only traced: a timeout surfaces as `needs.backend.result ==
   'cancelled'`, and `always()` jobs run after a job-level timeout. **Pushing a branch
   needs the orchestrator's go-ahead.** If it is withheld, SC-5 is recorded as not
   observed, not as passed.
4. **This PR's own CI** exercises only scenario 1 (all success). It proves the edit did
   not break the normal path, and it proves nothing about the cancelled path.
5. **The next real #2409 timeout on `develop` or any PR** is the first production
   observation. Record in #2521's closing comment what to look for (the title). A new
   issue is not warranted: the live probe in step 3 already observes GitHub's semantics,
   so the watch is confirmation, not discovery.
