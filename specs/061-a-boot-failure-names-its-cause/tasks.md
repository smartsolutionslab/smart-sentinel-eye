# Tasks: A boot failure names its own cause

**Spec**: `specs/061-a-boot-failure-names-its-cause/spec.md`
**Plan**: `specs/061-a-boot-failure-names-its-cause/plan.md`
**Issue**: #2061 — feature-level, on Project #13 (status *In Progress*)
**Branch**: `fix/2061-a-boot-failure-names-its-cause`

**Phase 4a colour: RED** (behaviour-changing — plan Declaration 3), with one
declared behaviour-preserving prelude (T001).

---

## Parallelism: there is almost none, and that is the honest answer

ADR-0109 marks `[P]` where tasks own **disjoint files**. This change owns
**two** files, and five of the seven tasks touch one of them. Marking things
`[P]` here would be decoration.

```
tests/Integration.Tests/Fixtures/AspireFixture.cs                    T001, T003, T005
tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs T001, T002, T004, T006
```

**T001 is the foundational task and blocks everything.** Until the signature
carries the exit code, no test about exit-code-driven selection can compile.
Say so to the orchestrator plainly: **there is nothing to fan out here.** A
single-file diagnostic fix is not a parallelisable feature, and pretending
otherwise would produce two agents editing the same 742-line file.

The one genuinely independent task is **T007** (the grep assertion), which
depends on T005's output but touches no shared file and could be written by a
second agent given T005's report. It is not worth a second agent.

---

## Task list

### T001 — widen the selection signature to carry exit codes

**Agent**: `infra-engineer` · **Colour**: behaviour-preserving (characterisation
green) · **Blocks**: T002–T007

Replace, in `tests/Integration.Tests/Fixtures/AspireFixture.cs`:

```csharp
internal static string[] SelectResourcesToReport(Dictionary<string, string> states)
```

with the two-argument form specified in `plan.md`, taking
`Dictionary<string, int?> exitCodes` as the second parameter. **Accept it and
do not consult it.** `IsHealthy` is unchanged in this task. Update the internal
caller (`CaptureFailedResourceLogsAsync`) to pass `_exitCodes`.

Update the five existing `SelectResourcesToReport` call sites in
`AspireFixtureReportSelectionTests.cs` by adding an argument — an empty
`new(StringComparer.Ordinal)` where the test has no exit codes to express.

**Evidence required (ADR-0144 characterisation path)**: run the six tests
**before** and **after**, capture both outputs verbatim. Both must read
`Passed! - Failed: 0, Passed: 6`. Baseline observed 2026-09-04 on `48f3b00`:

```
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 82 ms
```

**Stop and block if**: any *asserted value* has to change. Threading an
argument is mechanical and allowed; an assertion that must be edited means the
widening changed behaviour, which it must not.

**Why this is a separate commit**: ADR-0087 lands commits individually under
rebase-merge, so each must build alone — and separating it is what makes T002's
red an assertion failure rather than a compile error. `plan.md` discloses this
and the PR must too.

---

### T002 — the red test: a one-shot that died is selected

**Agent**: `test-writer` · **Colour**: RED · **Depends on**: T001

Add `A_one_shot_that_died_is_reported` to
`AspireFixtureReportSelectionTests.cs`, driven from run 33623647778's real
snapshot — nine `FailedToStart` services, `migrations` `Finished` with exit
code 134, `-rebuilder` resources `NotStarted`, databases and infrastructure
`Running`. Do not abbreviate the state map to two entries; the realism is the
point, and it is what makes the failure output legible.

**Assert on which resource is selected**, not on the report's shape and not
that a method was called. `migrations` must be among the selected names.

**Required artifact**: the verbatim failing output. Expected form (`plan.md`
carries the full text):

```
Shouldly.ShouldAssertException :
AspireFixture.SelectResourcesToReport(states, exitCodes) should contain
"migrations" but was ["audit-observability", "automation", …]
```

**`test-writer` may not touch `AspireFixture.cs`.** If the test passes on
first run, that is a phase-4 failure under ADR-0144 and the run retries from
4a — it would mean T001 consulted the exit code, which it must not.

---

### T003 — make the one-shot exemption conditional on the exit code

**Agent**: `infra-engineer` · **Depends on**: T002 (receives its red output as
the brief)

In `IsHealthy`, the one-shot clause becomes: `Finished` **and** one-shot **and**
the recorded exit code is not a non-zero value.

**Absent is not failure.** A missing or `null` exit code stays healthy — FR-002,
assumption A1, and the reason `A_one_shot_job_that_finished_is_not_reported`
must keep passing with its assertion untouched.

**May not edit T002's test.** T002 goes green; the six pre-existing tests stay
green.

---

### T004 — adversarial pass on the selection rule

**Agent**: `test-adversary` · **Depends on**: T003

ADR-0144 asks for the adversary where the issue is about a failure mode. This
one is entirely about a failure mode, and it is the **second consecutive issue
in this file** where a green test sat over a broken diagnostic (#2054). The
brief is not decorative:

1. **Try to write a test that passes against the unfixed code and looks like it
   covers this.** If you succeed, say so loudly — that is the #2054 shape and
   it means T002 is weaker than it reads.
2. Cover the boundaries the happy path misses: exit code `0` (not selected);
   exit code absent from the map entirely (not selected); a `migrations-`
   prefixed one-shot with a non-zero code (selected — `IsOneShot` matches the
   prefix); a non-one-shot service `Finished` with exit code 0 (still selected,
   as today — #1918's original case must not be lost); an exit code present for
   a `Running` resource (not selected).
3. Negative exit codes and an empty state map.

Add what genuinely covers a gap. **Do not pad.** A test per boundary that
already has one is noise, and this file is small and readable today.

---

### T005 — the report says why each resource was selected

**Agent**: `infra-engineer` · **Depends on**: T003

Three edits in `AspireFixture.cs`, satisfying FR-003, FR-004 and FR-005. The
target report shape is specified in `plan.md` — **follow it, do not invent
prose**:

- Section headers carry state and exit code, and distinguish a process that
  **ran and died** from one that **never launched** (for which an empty log is
  the expected outcome, not a collection failure — the reasoning already sits
  in the file at `AspireFixture.cs:250-256` from #2038 and is simply not said
  out loud in the report).
- A leading `Likely cause:` line before `Resource states:` when any resource
  exited non-zero, naming the resource and the code. This is FR-005 and it is
  the one item a reviewer could reasonably cut; it is a separate concern in
  this task so that cutting it is a small edit.
- The word **"exited"** must appear, so a future reader's grep lands.

**Do not** re-order the failed-resource section (plan: *Ordering*). **Do not**
deduplicate the placeholders (spec: *Scope ruling*).

---

### T006 — the formatting test, asserting on the produced string

**Agent**: `test-writer` · **Depends on**: T005 for the target shape, written
**before** it per ADR-0139

Renders the full failed-resource report from run 33623647778's state and
exit-code maps and asserts on the string: `migrations` appears with `Finished`
and `134`; the nine services appear with the never-launched explanation; no
`-rebuilder` appears anywhere; the `Likely cause:` line names `migrations`.

Red first, output captured verbatim.

**Note for whoever writes this**: `CaptureFailedResourceLogsAsync` is an
instance method needing a live `_app`, so the formatting must be factored into
an `internal static` pure function to be testable at this seam — the same shape
`FormatResourceStates` already has. Doing so is part of T005, and this note
exists so T005 does not land an untestable method that T006 then has to
retro-fit.

---

### T007 — the grep assertion (spec Tier 1, step 4)

**Agent**: `infra-engineer` at phase 5 · **Depends on**: T006

Run the exact query a human ran on the real CI log — `grep -icE
"migration.*fail|abort|SIGABRT"`, or its .NET equivalent — against the report
string T006 produces, and confirm it is **non-zero** where it was **0** on run
33623647778.

This is the closest thing to an end-to-end check available: the literal
question that failed on the real log, answered by the new report. It belongs in
the verification note.

**Do not overclaim.** The note says *"the report now names `migrations` and its
exit code, and a grep for a crash finds it"*. It does **not** say *"the report
now shows the migrations logs"* — nothing here establishes that
`ResourceLoggerService.WatchAsync` serves an exited resource, and `plan.md`
names the trigger under which that becomes its own issue.

---

## Dependency graph

```
T001 (signature — foundational, blocks all)
  └─ T002 (RED: selection)
       └─ T003 (fix: exit-code-aware exemption)
            ├─ T004 (adversary: boundaries)
            └─ T005 (report says why)   ← T006 written red before T005 lands
                 └─ T006 (RED: formatting)
                      └─ T007 (phase 5: the grep assertion)
```

## Commits (ADR-0030 Conventional Commits, ADR-0086 no `Co-Authored-By`)

Each must build and pass **on its own** — ADR-0087 rebase-merge lands them
individually, and a commit that only compiles with its successor breaks
`git bisect` permanently.

| Task | Commit |
|---|---|
| T001 | `refactor(tests): the selection can see the exit code it was already printing` |
| T002 | `test(tests): a one-shot that died should be in the failure report` |
| T003 | `fix(tests): finishing is only success when the exit code says so` |
| T004 | `test(tests): the boundaries around a one-shot's exit code` |
| T005 | `fix(tests): the report says why it named each resource` |
| T006 | `test(tests): the report names the resource that exited` |

## Phase 3 gate

`tasks.md` is written; #2061 is on Project #13 (status *In Progress*). Per
CLAUDE.md, phase 3 creates **no per-task issues** — the feature-level issue is
the tracked artifact, and `/speckit-taskstoissues` is deliberately not run.
