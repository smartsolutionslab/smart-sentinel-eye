# Tasks — Spec 167, a slow hang that fails loudly

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2410](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2410) · **Branch:** `fix/2410-an-integration-hang-that-fails-loudly`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Phase 4a colour:** **BEHAVIOUR-CHANGING (RED first)** — T003 and T004
together carry two counterfactuals, not one. See `spec.md` §5 for why a
single hung-test demonstration (spec 166's recipe) does not cover this
job's distinctive failure shape.
**Latency (§IV): N/A.**
**Delivery:** one PR → `develop`, cut fresh. **Not stacked.**
**New ADR required: no** — `spec.md` §4.
**Board gate:** #2410 carries `agent:ready` today. **`spec.md` §0 flags
that the issue's own body asked for a human to confirm scope first, and
this spec's findings (§2) make that confirmation more warranted, not
less** — resolve §0 before treating this as clear to enter phase 4.
**Contention file note (ADR-0109):** `.github/workflows/ci.yml` is
high-contention. This diff is additive (one `run:` extended, one `path:`
extended, one new step) — coordinate if another in-flight branch also
touches `ci.yml`.

---

## `[P]` markers — ADR-0109

Nothing here is parallel. T001–T003 all land in the same file and the same
step; T004 (the second counterfactual) depends on T001 existing (it tests
the *ordering* T001's 12-minute figure assumes against `StartupTimeout`,
which requires the real flag to be present, even if scaled down for the
local run). T005 depends on all four. Sequential: T001 → T002 → T003 →
T004 → T005 → T006.

---

## Evidence first — what T003/T004 must produce, read before starting

Per `spec.md` §5, §2 found **two** distinct legitimate-vs-hung shapes this
instrument has to distinguish, and they exercise different code:

1. A hung test case, once xUnit has dispatched it (the `backend`/spec 166
   shape).
2. A hang **during fixture bring-up**, before any gated test has been
   dispatched — where `--blame-hang` has nothing to attribute the hang to,
   and where `AspireFixture`'s own `StartupTimeout` is supposed to win the
   race by design (`spec.md` §2.6, §3.1's comment).

Both need a constructed, quoted counterfactual. Neither can be observed
from a real hang on demand (heisenbug class, same as spec 166).

---

## T001 — Add `--blame-hang` to the "Run integration tests" step `[infra-engineer]`

**File:** `.github/workflows/ci.yml`

Per `plan.md` §3.1. Replace the existing "No blame-hang watchdog here…
Tracked as a follow-up: #2410" comment with the new one justifying 12
minutes against `spec.md` §2's measured numbers and the `StartupTimeout`
ordering constraint — don't leave both comments stacked, the old one is
false the moment this lands.

**Verify before considering this done:**
- Confirm via `dotnet test --help` (pinned SDK) that `--blame-hang*` is
  documented compatible with `--logger` and `--filter` together — this
  three-way combination has not been run in this repo (`coverage-check.ps1`
  pairs blame with `--collect:XPlat Code Coverage` instead, not the same
  combination).
- A full CI `integration` job run passes with identical pass/fail counts
  (539 tests, per the four sampled runs in `spec.md` §2.1) to a same-commit
  run without the flags.

## T002 — Extend the diagnostics upload `[infra-engineer]`

**File:** `.github/workflows/ci.yml`

Per `plan.md` §3.2 — extend the existing "Upload integration test results"
step's `path:` to include `**/TestResults/*.dmp` and
`**/TestResults/*Sequence*.xml`, anchored (not `backend`'s wider `**/*.dmp`
— this call site has no `--results-directory` override, verified in
`spec.md` §3.2, so the anchored form is correct here). Leave
`if-no-files-found` unset (default `warn`) — do not copy `ignore` from
`backend`'s step; that step exists solely for files normally absent, this
one already uploads the always-present `.trx`.

**Verify:** on a normal (non-hung) run, the step still succeeds and still
uploads the `.trx` (i.e. the extension doesn't change existing behaviour
for the case that already worked).

## T003 — Add the cancellation-only container-log step `[infra-engineer]`

**File:** `.github/workflows/ci.yml`

Per `plan.md` §3.3 — `if: failure() || cancelled()` (not `always()`,
matching `709d56c8`'s own stated reasoning, reused rather than
reinvented), `docker ps -a` then `docker logs --tail 200` per container,
grouped with `::group::`/`::endgroup::` for readability.

**Verify:** trigger on a genuine test failure (e.g. temporarily break an
assertion, observe the step runs and prints container logs, then revert)
— confirming the condition fires on `failure()` as well as `cancelled()`
is cheap to check directly rather than trusting the YAML reads correctly;
do not attempt to verify the `cancelled()` half by actually cancelling a
30-minute job.

## T004 — Counterfactual 1: a hung test case, once dispatched `[infra-engineer]`

Per `spec.md` §5 item 1. Add one throwaway `[Fact]` in a class carrying
`[Collection(AspireCollection.Name)]` (real call site — the "Run
integration tests" step's normal filter, not the Docker-free path spec 166
used) with `await Task.Delay(Timeout.InfiniteTimeSpan,
CancellationToken.None);` as its body.

- **Before:** run unflagged (temporarily comment out T001's flags, or run
  against a commit before T001). Kill manually after ~30s — do not wait
  out a real 30-minute job. Quote the silence.
- **After:** T001's flags in place. Re-run. Quote: fails inside the
  12-minute budget, names the hung test, writes a `.dmp`.
- **Delete the test.** Confirm `git status` clean before the PR.

## T005 — Counterfactual 2: a boot-phase hang, scaled down `[infra-engineer]`

Per `spec.md` §5 item 2 — this is the evidence specific to this slice's
central finding (§2.6) and has no equivalent in spec 166.

**Local-only, never committed:** temporarily edit `AspireFixture.cs` to
(a) shrink `StartupTimeout` from 8 minutes to ~10 seconds, and (b) point
one `WaitForResourceAsync` call at a resource name that cannot resolve
(e.g. `"keycloak-typo"`). Run a scratch `dotnet test` invocation against
this locally-modified fixture twice, varying only `--blame-hang-timeout`:

1. **`--blame-hang-timeout 20s` (above the shrunk `StartupTimeout`):**
   quote the fixture's own `TimeoutException` firing first — the
   resource-state table from `FormatTimeoutMessage`, confirming
   `StartupTimeout` wins the race when it's smaller, exactly as `ci.yml`'s
   real 12min/8min ordering intends.
2. **`--blame-hang-timeout 5s` (below the shrunk `StartupTimeout`):** quote
   whatever vstest actually reports as "the test running when the crash
   occurred." Record verbatim what it says — this is the open question
   `spec.md` §2.6 raises (does it come back empty, `null`, or something
   else) and must be answered by observation, not assumption.

**Revert every temporary edit** (`StartupTimeout`, the mistyped resource
name) before the PR. `git status` / `git diff origin/develop` must show
`AspireFixture.cs` untouched in the final commit — same discipline as
T004's throwaway test.

## T006 — Verify end to end and report `[infra-engineer]`

- A normal `integration` job run (no injected hang) passes with the same
  539-test result as the four baseline runs in `spec.md` §2.1.
- Report: branch, the one modified file (`ci.yml`, three changes), all
  four counterfactual transcripts (T004 before/after, T005's two orderings)
  quoted verbatim, and explicit confirmation that `AspireFixture.cs` and
  every other file are untouched in the final diff.
- Flag `spec.md` §0 in the PR description explicitly — the issue's own
  "needs a human" line, and that this spec's findings did not remove that
  need.

---

## Definition of done

- [ ] T001 — `--blame-hang*` on the "Run integration tests" step, 12min
      justified at the call site against measured data and the
      `StartupTimeout` ordering constraint.
- [ ] T002 — diagnostics upload extended (anchored glob, `.trx`/`.dmp`/
      `*Sequence*.xml`).
- [ ] T003 — cancellation-only container-log step added.
- [ ] T004 — hung-test-case counterfactual captured, throwaway test
      removed from the final diff.
- [ ] T005 — boot-phase-hang counterfactual captured (both orderings),
      `AspireFixture.cs` reverted to untouched in the final diff.
- [ ] T006 — full verification pass reported; PR opened against `develop`;
      §0 flagged in the PR body.
- [ ] No ADR added. No existing test edited, skipped, renamed, or relaxed.
      No fixture file changed in the shipped diff. `backend`, `frontend`,
      `e2e` jobs untouched.
