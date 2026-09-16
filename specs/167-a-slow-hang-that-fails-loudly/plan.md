# Plan — Spec 167, a slow hang that fails loudly

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2410](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2410) · **Branch:** `fix/2410-an-integration-hang-that-fails-loudly`
**Spec:** `spec.md`
**Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Phase 4a colour: BEHAVIOUR-CHANGING (RED first), two counterfactuals** —
`spec.md` §5.
**Latency (§IV): N/A** — no production runtime file changes.
**New ADR required: no** — `spec.md` §4.

---

## 1. Which engineer, and why

**`infra-engineer`.** Every change stays inside `.github/workflows/ci.yml`.
`spec.md` §2.6/§6 considered and explicitly rejected touching
`AspireFixture.cs` — its `StartupTimeout` and `WaitForResourceAsync` chain
are used as a **measured constraint** on the CI-side number, not modified.
The one place a fixture file is touched at all is a **temporary, reverted,
never-committed** edit for counterfactual 2 (§5 below) — that stays with
`infra-engineer` too, since it is throwaway harness verification, not a
change to what the fixture does. If a future slice decides the ~132s boot
or the ~97s retry test should themselves change, that is a different
engineer's work (backend, for the retry policy; whoever owns the fixture's
shape, for the boot) with its own spec.

## 2. The change, in one sentence

Add `--blame-hang` (12-minute budget, justified against measured data) to
the `integration` job's one `dotnet test` call, extend its existing
diagnostics upload to catch a hang dump, and add a cancellation-only
container-log step — three additive changes to one file, no other file
touched.

## 3. Exact call site and diffs

### 3.1 `.github/workflows/ci.yml` — "Run integration tests" step

Current (as of this branch's base):

```yaml
      - name: Run integration tests
        # No blame-hang watchdog here (unlike `backend`, #2409): …
        # …
        # Tracked as a follow-up: #2410.
        #
        # Three categories are excluded. …
        run: |
          dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
            -c Release \
            --no-build \
            --filter "Category!=Measurement&Category!=Disruptive&Category!=Maintenance" \
            --logger "trx;LogFileName=integration.trx"
```

Target:

```yaml
      - name: Run integration tests
        # #2410: blame-hang watchdog, sized against this job's own measured
        # timing (specs/167-a-slow-hang-that-fails-loudly/spec.md §2-3), not
        # copied from `backend`'s (#2409) 3-minute figure — that population
        # is fakes-only with near-zero inter-test gaps; this one boots a
        # real Aspire/Docker stack inside a collection fixture, and four
        # independent green runs show a stable, structurally-explained
        # 120-134s host-wide inactivity ceiling (AspireFixture's own
        # ~132s bring-up, invisible to any single test's duration since
        # xUnit dispatches no gated test until the fixture's
        # InitializeAsync returns; separately, EventIngestion.
        # PoisonDeliveryEscapeIntegrationTests legitimately runs ~97s on a
        # retry/backoff path). 12min is chosen to clear that ceiling by
        # >5x AND to sit above AspireFixture's own internal
        # `StartupTimeout` (8min, AspireFixture.cs) with margin -- below
        # StartupTimeout, --blame-hang would pre-empt that mechanism's own
        # richer, resource-named TimeoutException with a generic hang dump
        # that (during fixture boot) has no dispatched test to attribute
        # the hang to.
        #
        # The margin, as a figure: 12min - 8min = 240s, which has to absorb
        # StartupTimeout's cancellation not propagating instantly through
        # some awaited call -- measured directly (StartupTimeout shrunk to
        # 10s, temporarily/locally/reverted), two regions, two very
        # different answers: a WaitForResourceAsync pointed at an
        # unresolvable name fired ~30ms after the nominal 10s (essentially
        # instant); DCP/container bring-up inside StartAsync did not --
        # a nominally-correct 20s-over-10s ordering still lost, surfacing
        # ~74s wall against the 10s cutoff (~64s propagation delay). 240s
        # is ~3-4x that ~64s worst case actually measured, not a hedge
        # against a purely hypothetical one -- see spec.md §3.1 for the
        # full reasoning.
        #
        # This budget is coupled to three things that can drift it without
        # anyone touching this file: AspireFixture gaining another gated
        # resource, EventIngestion's retry/backoff policy changing
        # PoisonDeliveryEscapeIntegrationTests's ~97s, or StartupTimeout
        # itself being retuned upward (it inverts the 12min/8min ordering,
        # not just consumes margin -- tracked: #2412). Re-measure (don't
        # just copy this number forward) if any of the three moves.
        #
        # Three categories are excluded. …
        run: |
          dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
            -c Release \
            --no-build \
            --filter "Category!=Measurement&Category!=Disruptive&Category!=Maintenance" \
            --logger "trx;LogFileName=integration.trx" \
            --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 12min
```

Keep the existing "Three categories are excluded…" comment block verbatim;
insert the new comment above the `run:` block, below the existing "No
blame-hang watchdog here…/Tracked as a follow-up: #2410" comment — replace
that comment rather than stack both (it becomes stale the moment this
lands; leaving it would contradict the line immediately below it).

**Verify before considering this done:**
- `dotnet test --help` (pinned SDK, matches `global.json`) confirms
  `--blame-hang*` is compatible with `--logger "trx;…"` and with `--filter`
  — this exact three-way combination (filter + trx logger + blame) has not
  been run in this repo before; `coverage-check.ps1`'s call combines blame
  with `--collect:XPlat Code Coverage` instead, a different combination.
- A full `integration` job run (real CI, Docker) completes with identical
  pass/fail counts to a same-commit run without the flags — the flags must
  not themselves perturb outcome or timing beyond noise.

### 3.2 Same file — "Upload integration test results" step

Current:

```yaml
      - name: Upload integration test results
        if: always()
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: integration-test-results
          path: '**/TestResults/*.trx'
          retention-days: 14
```

Target — extend `path:` only, same step, same `if: always()`, same action
pin already used elsewhere in this file:

```yaml
      - name: Upload integration test results
        if: always()
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: integration-test-results
          # #2410: --blame-hang writes a mini-dump (and a sequence trace)
          # into this same dotnet test call's results directory. Unlike
          # coverage-check.ps1's per-project --results-directory override
          # (which backend's equivalent step has to work around with a
          # repo-root-relative **/*.dmp glob), this call site takes no
          # --results-directory override, so everything -- trx, dmp,
          # sequence file -- lands under the vstest default TestResults/ --
          # but verified directly against a real hang dump (counterfactual
          # 1, PR body), the .trx lands flat in TestResults/ while the .dmp
          # and Sequence*.xml do not: vstest writes them one level deeper,
          # under a per-run GUID subfolder (TestResults/<guid>/...), and a
          # second copy lands three levels deep
          # (TestResults/<host>_<timestamp>/In/<host>/...). A single-segment
          # `**/TestResults/*.dmp` (matching only a direct child of
          # TestResults/) misses both -- an earlier version of this plan
          # assumed the .trx's flat placement generalised to the blame
          # outputs, and it does not. `**/TestResults/**/*.dmp` (a second
          # `**` between TestResults/ and the filename) is what actually
          # matches, confirmed against both nesting depths above.
          #
          # *Sequence*, not a prefix/suffix anchor: this pinned SDK's
          # collector writes `Sequence_<guid>.xml` but `dotnet test --help`
          # documents the opposite, `<guid>_Sequence.xml` -- the two
          # disagree (verified against the collector assembly directly),
          # so the loose glob matches either rather than risking a future
          # "fix" into one that matches neither.
          path: |
            **/TestResults/*.trx
            **/TestResults/**/*.dmp
            **/TestResults/**/*Sequence*.xml
          retention-days: 14
```

`if-no-files-found` is intentionally left at its default (`warn`) here,
unchanged from the existing step — this differs from `backend`'s new step,
which sets `ignore` because *that* step exists solely for files that are
normally absent. This step already uploads the always-present `.trx`, so
`warn`-on-absence was never the right default to introduce for the two new
patterns either; leaving the step's existing behaviour alone is the
smaller diff and does not mask an unexpectedly-missing `.trx`.

### 3.3 Same file — new step, after 3.2

```yaml
      # #2410: the outermost of three layers. --blame-hang (§3.1) and
      # AspireFixture's own StartupTimeout (AspireFixture.cs) both depend on
      # something inside the test host noticing the hang and unwinding
      # cleanly enough to throw or dump. If neither does, the job runs to
      # its 30-minute timeout-minutes wall and everything with it is lost --
      # the same gap 709d56c8 (#2137) closed for e2e's apphost.log. That
      # job's AppHost is a separate `dotnet run` process with a redirected
      # log file; this job's AppHost runs in-process via
      # DistributedApplicationTestingBuilder, so there is no equivalent
      # single log file -- but the Docker containers it starts are still
      # visible to the runner's own Docker daemon regardless of what the
      # test host is doing.
      #
      # Not always(): 709d56c8's own reasoning, reused verbatim -- this
      # would print a large amount of output on every green run for no
      # reason. failure() || cancelled(), matching that precedent exactly.
      - name: Container state and logs (on failure or cancellation)
        if: failure() || cancelled()
        run: |
          docker ps -a
          for c in $(docker ps -aq); do
            echo "::group::$(docker inspect -f '{{.Name}}' "$c")"
            docker logs --tail 200 "$c" || true
            echo "::endgroup::"
          done
```

Placed after "Upload integration test results" (order among `if: always()`
/ conditional diagnostic steps does not matter functionally, but keeps the
"what happened" step before the "why" step, matching `e2e`'s existing
ordering of its own diagnostic steps).

## 4. What is explicitly not touched

- `AspireFixture.cs`, `AspireFixture.Db.cs`, `AspireFixture.Auth.cs`, any
  `WaitForResourceAsync` call, `StartupTimeout` — used as a measured
  constraint (§ spec.md §2.6), never edited in the shipped diff. Phase 4a's
  counterfactual 2 (`spec.md` §5) touches this file **temporarily, locally,
  reverted before the PR** — `git status` / `git diff origin/develop` must
  show it clean in the final commit, same discipline as the throwaway test.
- `backend`, `frontend`, `e2e` jobs.
- `coverage-check.ps1` — that script only runs in the `backend` job.
- Any test file, any coverage threshold.
- `scripts/`, `Directory.Packages.props`, `global.json` — no new package;
  `--blame-hang` ships in the SDK `global.json` already pins.

## 5. Risk / cost review

| Risk | Mitigation |
|---|---|
| 12min is still too tight for an occasional slower-but-healthy boot (Docker pull contention, Keycloak realm import) | §2.5's asymmetry framing — chosen closer to `StartupTimeout`+50% than to the measured ceiling×12, deliberately erring loose; phase 4/5 re-verify against a fresh real run before merge |
| `--blame-hang` pre-empts `StartupTimeout`'s better diagnostics if ever re-sized below 8min | Call-site comment states the ordering constraint explicitly (§3.1); counterfactual 2 proves it empirically, not just by reasoning |
| The glob change silently stops matching if a future PR adds a `--results-directory` override to this call site | Comment explains *why* the anchored form is correct *for this call site specifically*, not as a general rule |
| Container-log step becomes noisy or leaks secrets | `--tail 200`, and env vars are not dumped (`docker logs`, not `docker inspect --format '{{json .Config.Env}}'`); reviewer should confirm no container prints credentials to stdout at info level — checked in phase 4/5, not assumed here |
| §0's agent:ready/"needs a human" tension is never actually resolved | Named explicitly in `spec.md` §0 and §7's acceptance criteria; not this artifact's authority to resolve |

## 6. Bounded context / DDD sections — none

CI tooling only, matching spec 166 §6's convention for a non-domain slice.

## 7. Rollback

Delete the three additions from `ci.yml` — no schema, no data, no other
file depends on their presence.
