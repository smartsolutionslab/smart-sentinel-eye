# Tasks — Spec 166, a hang that fails loudly

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2409](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2409) · **Branch:** `fix/2409-a-hang-that-fails-loudly`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Phase 4a colour:** **BEHAVIOUR-CHANGING (RED first)** — T003 carries the
counterfactual. See `spec.md` §5 for why the standard "observe the real bug
red" recipe does not apply here (heisenbug) and what replaces it.
**Latency (§IV): N/A** — no production runtime file changes, no leg moves.
**Delivery:** one PR → `develop`, cut fresh from `origin/develop`. **Not
stacked.**
**New ADR required: no** — `spec.md` §4.
**Board gate:** #2409 already carries `agent:ready` on Project #13 (picked up
by the autonomous lane per its own comment). Nothing to add; feature-level
tracking since spec 028 — do not run `/speckit-taskstoissues`.
**Contention file note (ADR-0109):** `.github/workflows/ci.yml` is a
high-contention file. This slice's diff to it is additive and small (one
`run:` block extended, one new step) — coordinate if another in-flight branch
also touches `ci.yml` before merging.

---

## `[P]` markers — ADR-0109

**Nothing in this task list is parallel.** T001 (coverage-check.ps1) and T002
(ci.yml) touch different files and could in principle run concurrently, but
T003 (the counterfactual evidence) depends on **both** being in place first —
it is the single piece of evidence for the whole PR, run once against the
finished mechanism, not once per call site. Sequential: T001 → T002 → T003 →
T004 → T005.

---

## Evidence first — what T003 must produce, read before starting

Per `spec.md` §5: this is new CI behaviour and the real hang cannot be
summoned on demand. The red/green pair is constructed, not observed
incidentally:

1. **Before** the flags exist (or with them temporarily commented out /
   stashed): add one deliberately-hanging `[Fact]` — e.g. in a scratch file
   under `tests/StreamDistribution.Infrastructure.Tests/` — with
   `await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);` as
   its body. Run the assembly's current `dotnet test` (no blame flags).
   Confirm and note down (do not wait out a real hang — kill the process
   manually after ~30s and record that it had not returned) that it produces
   no completion output. This is the "red": today's actual behaviour,
   demonstrated rather than assumed.
2. **After** T001/T002 land: re-run the **same** deliberately-hanging test
   with the new flags in place. Confirm and capture verbatim: the run fails
   inside the 3-minute budget, the console output names the hung test, and a
   `.dmp` file is written under `TestResults/`.
3. **Delete the deliberately-hanging test.** It must not appear in the
   final diff — `git status` clean of it, `git diff` for the PR contains
   only T001/T002/T004's changes.
4. Both captures (raw console output, trimmed to the relevant lines) go
   into the PR body verbatim, per CLAUDE.md's phase-4 quoting rule.

---

## T001 — Add the hang watchdog to `scripts/coverage-check.ps1`'s test loop `[infra-engineer]`

**File:** `scripts/coverage-check.ps1`

Add `--blame-hang`, `--blame-hang-dump-type mini`, `--blame-hang-timeout
3min` to the `$testArgs` array built inside the `foreach ($proj in
$testProjects)` loop (`plan.md` §3.1). Comment: why (#2409 — silent hang read
as `cancelled`, not `failure`), and why 3 minutes (measured per-project
timing headroom, `spec.md` §3.3 — re-measure against a fresh local or CI run
before picking the final number; do not just copy the spec's number without
checking it still holds).

**Verify before considering this done:**
- `pwsh scripts/coverage-check.ps1 -Configuration Release` (full build, no
  `-NoBuild`) completes normally end to end with the new flags present — no
  regression to the coverage gate, no interaction failure with
  `--collect:XPlat Code Coverage` (`plan.md` §3.1's flagged open question).
- The existing `CoverletDataCollectorException` handling
  (`coverage-check.ps1`'s `$testOutput | Select-String -Pattern
  'CoverletDataCollectorException'`) still triggers correctly if forced —
  no need to force it, just confirm by reading that nothing about the new
  flags changes what `$testOutput` captures (it's still piped through
  `Tee-Object -Variable projectOutput`).

## T002 — Add the same watchdog to `ci.yml`'s "Docker-free fixture logic tests" step `[infra-engineer]`

**File:** `.github/workflows/ci.yml`

Extend the `dotnet test` command in the "Docker-free fixture logic tests"
step (`plan.md` §3.2) with the same three flags. One-line comment referencing
#2409 and the fuller explanation in `coverage-check.ps1` is enough — do not
duplicate the whole rationale in two places.

**Verify:** the `--filter "Category=FixtureLogic"` selection is unaffected
(blame flags are orthogonal to test filtering) — read `dotnet test --help`
or the vstest docs to confirm flag compatibility before assuming it, since
this exact combination has not been run in this repo.

## T003 — Construct and capture the before/after counterfactual `[infra-engineer]`

Per "Evidence first" above. This is the phase-4a red-first evidence for this
slice — not a permanent test, a **constructed demonstration**, captured
verbatim for the PR body. Do this **after** T001 and T002 are both in place
(the "after" run needs the finished mechanism; the "before" run can happen
first or be reconstructed by temporarily reverting the flags — either is
acceptable as long as both outputs are genuinely observed, not written from
memory of how blame-hang is "supposed to" behave).

**Explicit reminder:** the throwaway hanging test must not be part of the
final commit. Confirm with `git status` / `git diff` against `develop`
before opening the PR.

## T004 — Add the hang/crash diagnostics upload step `[infra-engineer]`

**File:** `.github/workflows/ci.yml`

Add the new `if: always()` upload-artifact step to the `backend` job,
per `plan.md` §3.3 — `**/TestResults/*.dmp` and `**/TestResults/*_Sequence.xml`,
`if-no-files-found: ignore`, `retention-days: 14`, pinned to the same
`actions/upload-artifact` SHA already used elsewhere in this file (do not
introduce a second pin for the same action).

**Verify:** on a normal (non-hung) `backend` job run, this step reports
"no files found" (via `if-no-files-found: ignore`) and does **not** fail the
job. This is checkable by running the job in CI once (T005) — do not assume
`if-no-files-found: ignore` is correct without seeing the step's own log
line confirm it.

## T005 — Verify end to end and report `[infra-engineer]`

Per CLAUDE.md's "implement, verify, report":

- A normal `coverage-check.ps1` run (no injected hang) still passes with the
  same coverage gate results as before this change (no threshold moved, no
  test count changed).
- Push the branch and let a real CI run confirm the `backend` job still goes
  green on ordinary (non-hung) code — this is the closest thing to an
  end-to-end smoke this slice gets, since the actual hang cannot be
  summoned.
- Report: branch, the two modified files + the one new step, the T003
  before/after transcript quoted verbatim, and explicit confirmation that no
  existing test file, threshold, or ADR was touched.

---

## Definition of done

- [ ] T001 — `coverage-check.ps1` loop carries the three blame flags, value
      justified at the call site, verified against a real run.
- [ ] T002 — `ci.yml`'s Docker-free fixture step carries the same flags.
- [ ] T003 — before/after counterfactual captured, quoted in the PR, the
      throwaway hanging test removed from the final diff.
- [ ] T004 — hang/crash diagnostics upload step added, `if-no-files-found:
      ignore` confirmed not to fail a normal run.
- [ ] T005 — full verification pass reported; PR opened against `develop`.
- [ ] No ADR added. No existing test edited, skipped, renamed, or relaxed.
      No coverage threshold changed. `integration` and `e2e` jobs untouched.
