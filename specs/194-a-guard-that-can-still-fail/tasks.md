# Tasks 194 — A guard that can still fail

**Phase:** 3 (Tasks) — ADR-0037
**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2293](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2293) — feature-level, on Project #13, status Todo (verified `--limit 2000`)

Per CLAUDE.md §Workflow, phase 3 creates **no per-task issues** since spec 028.
`tasks.md` is the artefact the work is tracked against.

---

## Parallelism: there is none, and that is by construction

**No task carries `[P]`.** Every task in this spec edits one file:

```
tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs
```

ADR-0109 marks `[P]` only for disjoint file ownership. One file, one worker,
strictly sequential. Splitting the three user stories across branches would
produce guaranteed rebase conflicts for a saving of two lines — rejected in
`spec.md` §Why one slice and not three.

**File contention — verified 2026-09-20, clean:**

- `gh pr list --state open` → **zero open PRs**.
- Nine remote branches swept with `git diff --name-only origin/develop...origin/<b>` →
  **none touches `PersistenceLoop*`**.
- Spec 193 (`2292-outbox-sync-commit-guard`, worktree `D:/Github/sse-2292`)
  touches no file under `EventIngestion`.

**Spec number 194** — highest merged on `origin/develop` is 192; 193 is claimed
by the unmerged `2292-outbox-sync-commit-guard` branch. Re-check before opening
the PR: an unmerged branch can claim a number that `origin/develop` does not
show.

---

## Ordering

```
T001 ──> T002 ──> T003 ──> T004 ──> T005 ──> T006 ──> T007 ──> T008 ──> T009
 (baseline) (mutate) (old=green) (new=red) (revert+green) (US-2 red+green) (clean proof) (US-3) (suite)
```

T001–T007 are one indivisible evidence sequence. Interrupting it between T002
and T007 leaves a mutated production file in the worktree — the single worst
outcome available to this spec.

---

## Phase 4a — test-writer

> ADR-0144: test-writer writes tests only, runs them, and returns the
> **verbatim output**. Here it also applies and reverts a temporary production
> mutation, because that is the only way this spec's red exists (`spec.md`
> §Phase-4a colour). The mutation is evidence, not implementation, and T007 is
> the proof it did not survive.

### [T001] [US-1,US-2] Capture the baseline green

Run and keep the verbatim output:

```
dotnet test tests/EventIngestion.Infrastructure.Tests/SmartSentinelEye.EventIngestion.Infrastructure.Tests.csproj \
  --filter "FullyQualifiedName~PersistenceLoopHostedServiceTests"
```

**Done when:** 8 passed, 0 failed, and the output is saved for the PR body.
This is the characterisation capture for the six tests this spec does not edit
(constitution §Testing, behaviour-preserving half).

**Depends on:** nothing.

### [T002] [US-1] Apply the counterfactual mutation to the loop

In `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs`,
`RetryAsync` (`:204`): after `await CompleteAsync(delivery, outcome, cancellationToken);`,
stop attempting deliveries once one comes back `Outcome.Failed` and carry the
remainder untried. A `for` loop over the index is the simplest shape —
`IReadOnlyList<T>` has no `IndexOf`.

This is head-of-line blocking: the defect the test at `:81` names, and the one
its docstring says spec 018 fixed.

**Done when:** it compiles and the mutation is marked with a comment that makes
it impossible to mistake for real code.

**Depends on:** T001.

### [T003] [US-1] Prove the *old* test cannot see the defect

With the mutation in place and `PersistenceLoopHostedServiceTests.cs` **still
unmodified**, run the filtered suite.

**Done when:** `One_delivery_that_never_stores_does_not_hold_up_the_others`
**passes**, and the verbatim output is saved.

This output is the bug report. Without it, T004's red proves only that the new
test can fail — not that the old one could not. Expect ~5 s (the poison is
abandoned at 500 ms, then the healthy event stores).

**Depends on:** T002.

### [T004] [US-1] Make the change, and observe it red under the mutation

Apply **change 1** from `plan.md` §Design: add `Window = TimeSpan.FromSeconds(30)`
to the harness initialiser at `:85-88`, with the sibling's explanatory comment.
Change nothing else — no assertion, no deadline, no name.

Run the filtered suite with the mutation still in place.

**Done when:** `One_delivery_that_never_stores_does_not_hold_up_the_others`
**fails**, and the verbatim failure (name, message, line) is saved for the PR.
Expect ~11 s — the test now runs out its 10 s deadline.

**Depends on:** T003.

### [T005] [US-1] Revert the mutation and observe green

Restore `PersistenceLoopHostedService.cs` to `HEAD`. Run the filtered suite
again, with change 1 in place.

**Done when:** 8 passed, and the verbatim output is saved. This is the fourth
cell of the 2×2 and the one that says **there is no production bug**.

**Depends on:** T004.

### [T006] [US-2] Red then green on the retry test

Two runs, both saved verbatim:

1. **Red.** Temporarily set `Window = TimeSpan.FromMilliseconds(1)` on
   `Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands` (`:46`)
   and run `--filter "FullyQualifiedName~Retries_a_failed_delivery"`.
   **Expected:** `Shouldly.ShouldAssertException : completion.Stored should be 1
   but was 0` — the exact signature of the 2026-09-11 flake, made
   deterministic.
2. **Green.** Replace the 1 ms with **change 2** from `plan.md` §Design —
   `Window = TimeSpan.FromSeconds(30)` with its explanatory comment — and run
   the full filtered class.

**Done when:** the red message is captured, the green run is 8 passed, and the
diff shows **no assertion changed**. Record explicitly in the PR that
`Attempts.ShouldBe(3)` — the load-bearing assertion — is untouched, so this is
not a threshold raised to reach green (ADR-0144).

**Depends on:** T005.

### [T007] [US-1,US-2] Prove the mutation did not survive — blocking

Three checks, all of which must hold:

1. `git diff HEAD --stat -- src/` reports **nothing**.
2. `touch` the reverted production file, then re-run the filtered suite. A
   restored file can carry an older timestamp than the compiled output; MSBuild
   then skips the rebuild and the run reports binaries nobody has.
3. `git diff --name-only HEAD` lists exactly one path:
   `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`.

**Done when:** all three hold and 8 tests pass on the forced rebuild.

**Blocking.** Do not proceed, commit, or report success while any check fails.

**Depends on:** T006.

### [T008] [US-3] Extend the harness default's docstring

Apply **change 3** from `plan.md` §Design to `:247-252`: keep the existing text,
add the paragraph saying only the abandonment cases should take this default and
that a test which must not be unblocked by abandonment sets `Window`, citing
spec 194 / issue #2293.

Comment only. No code, no behaviour, no new member.

**Done when:** the docstring builds and reads as *why*, not *what* (ADR-0036).

**Depends on:** T007.

### [T009] [all] Full project suite and analyzers

```
dotnet test tests/EventIngestion.Infrastructure.Tests/SmartSentinelEye.EventIngestion.Infrastructure.Tests.csproj
```

plus a Release build of the touched project, since
`dotnet_style_prefer_collection_expression` and `TreatWarningsAsErrors` bite
there and not in Debug.

**Done when:** the whole test project is green, no **new** analyzer warning is
attributable to this diff, and the six untouched tests in the class passed
unmodified in both T001 and here — the characterisation obligation, discharged
at both ends.

Pre-existing advisory warnings (`S107` on the loop's 5-parameter constructor,
`S138` across the context) are out of scope (ADR-0084).

**Depends on:** T008.

---

## Phase 4b — not applicable

No production file is modified in the delivered diff. The engineer has nothing
to receive. PR body, in ADR-0037's skip form:

```
Phase 4b: not applicable — no production file is modified; the delivery is
entirely test-file edits (spec 194).
```

---

## Phase 5 — verify

### [T010] [all] Write `verification.md`

Reproduce the 2×2 and record it as **observed**, not as reasoned. A measurement
reported only in a handoff message is invisible to every later grep.

| | mutated loop | current loop |
|---|---|---|
| old test (500 ms) | T003 result | T001 result |
| new test (30 s) | T004 result | T005 result |

Plus: T006's red signature and green, and T007's three clean-revert checks.

**Latency:** state `N/A — no file under src/ is modified` explicitly. Do not
leave the line out; §IV's record has drifted before by omission rather than by
error.

**Done when:** `specs/194-a-guard-that-can-still-fail/verification.md` exists
and every figure in it was produced by a run quoted in the PR.

**Depends on:** T009.

---

## Phase 6 — backend-reviewer

### [T011] [all] Review

**backend-reviewer**, not infra-reviewer: this is EventIngestion infrastructure
C#, with no Aspire, Docker, CI, Keycloak or Helm surface.

Points to put in front of the reviewer, because each is a place this change
could be wrong in a way a diff does not show:

1. Is the 30 s window a **strengthening**? Confirm no assertion changed and that
   `Attempts == 3` is intact — that this is not a threshold moved to reach green
   (ADR-0144's second forbidden act).
2. Is the red honest? The red comes from a mutation, not from the tree. Check
   that both the mutated-red **and** the mutated-green-on-the-old-test are
   quoted; one without the other is an incomplete claim.
3. Did the mutation survive anywhere? `git diff HEAD -- src/` on the branch.
4. Is `Records_and_releases_a_delivery_that_never_stores` still fast, and still
   the one legitimate consumer of the 500 ms default?
5. Is the governance flag in `spec.md` §Assumptions left **open** rather than
   answered? The lane may not settle it (ADR-0144).

**Done when:** every finding is resolved or accepted in writing.

**Depends on:** T010.

---

## Phase 7 — PR

### [T012] Open the PR against `develop`

`gh pr create --base develop`. Body carries: the four verbatim runs from
T001/T003/T004/T005, T006's red and green, the phase-4b not-applicable line,
the latency `N/A`, and `Closes #2293`.

A mention rarely auto-closes — use the closing keyword and check the issue state
after the merge.

**Done when:** the PR is open, parked for CI, and the four CI buckets are read
manually before merging (`develop` has no required status checks; a skipped or
cancelled bucket is not a green).

**Depends on:** T011.

---

## Gate — phase 3

Tasks are atomic and ordered. Issue #2293 is on Project #13 (verified with
`--limit 2000`; the number filter returns zero, so the check was by
`content.url`). Hand back for review before phase 4.
