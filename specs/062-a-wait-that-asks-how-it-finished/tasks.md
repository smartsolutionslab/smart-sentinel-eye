# Tasks: A wait that asks how it finished

**Feature Branch**: `fix/2064-a-wait-that-asks-how-it-finished`
**Spec**: `specs/062-a-wait-that-asks-how-it-finished/spec.md`
**Plan**: `specs/062-a-wait-that-asks-how-it-finished/plan.md`
**Issue**: #2064
**Story**: US1 — the person waiting on a red CI run (P1). There is only one.

---

## Parallelism: there is none, and the ordering is load-bearing

Every task touches one of two files, and the two files interact:

- `AspireFixture.cs` — one shared file. Nothing that edits it can run
  alongside anything else that edits it.
- `AspireFixtureMigrationGateTests.cs` — new, but it **references members that
  do not exist yet**, so the moment it lands the test project stops compiling.

That second fact makes the ordering a constraint rather than a preference:
**T001 must finish before T002 exists on disk.** T001 runs the real integration
suite to capture the pre-fix red, and a test project that does not compile
cannot run anything. Putting T002 first would silently destroy the load-bearing
red artifact and the failure would look like an unrelated build break.

So: **no `[P]` markers in this feature, and that is the honest answer**, not an
omission. ADR-0109's disjoint-file rule finds nothing to parallelise in a
twenty-line change to one file.

---

## Task list

### T001 [US1] — the pre-fix runtime red (phase 4a, the load-bearing artifact)

**Agent**: `test-writer`. **Touches**: `src/MigrationRunner/Program.cs`
(scratch, reverted) — **no commit**.

Provoke the failure on the **unmodified** fixture and capture what happens, per
`specs/061-a-boot-failure-names-its-cause/verification.md` §"How the failure was
provoked":

1. Confirm the tree is clean (`git status --short` empty).
2. Add the scratch throw to `src/MigrationRunner/Program.cs` immediately after
   `await host.StartAsync();`, marked `SCRATCH … REVERT`.
3. Run, timed, capturing everything:
   ```sh
   dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release \
     --filter "FullyQualifiedName~A_camera_registration_reaches_the_camera_catalog_log_tail" \
     > pre-fix.log 2>&1
   ```
4. **Run it a second time** and use the second figure. The first run after
   machine churn looks exactly like a regression.
5. Revert the scratch line; `git status --short` must be empty again.

**Captured verbatim, and these three things specifically:**

- the **exception type and the wait that raised it** — expected
  `System.TimeoutException : Aspire AppHost did not start within 8 minutes.`,
  from the `camera-catalog` wait;
- the runner's own `Duration:` line;
- the observed exit code, which on Windows will be a large negative number, not
  CI's `134` (#2061 saw `-532462766`).

**Done when**: the pre-fix duration and exception are recorded verbatim, the
scratch line is reverted, and the tree is clean.

**If the provocation no longer works** — the runner exits 0, or the boot
succeeds — **stop and block**. Tier 1 alone does not evidence this change, and
`plan.md` limit 1 says why.

**Depends on**: nothing.

---

### T002 [US1] — the red unit tests, standard cases (phase 4a)

**Agent**: `test-writer`. **Touches**:
`tests/Integration.Tests/Fixtures/AspireFixtureMigrationGateTests.cs` (new).
**May not touch `AspireFixture.cs`.**

Write, in the style of the sibling `AspireFixtureReportSelectionTests` (xUnit +
Shouldly, sentence-style names, no Docker):

1. `A_migration_that_exited_non_zero_is_a_failure` — `ExitedNonZero(134)` true.
2. `A_migration_that_exited_cleanly_is_not_a_failure` — `ExitedNonZero(0)` false.
3. `The_failure_message_names_the_code_before_it_shows_the_log` —
   `FormatMigrationFailureMessage(134, "…")` contains the code, contains the
   log, and the code's index is **less than** the log's.

Run them. **Expect red, and expect that red to be a compile failure**
(`error CS0117: 'AspireFixture' does not contain a definition for …`). Capture
it verbatim.

**State in the report that this red is weak evidence** — it proves the members
are absent, not that the behaviour is absent. T001 is the strong artifact.
`plan.md` §"The red output, concretely" is the wording to follow.

**Depends on**: T001 (which cannot run once this file exists).

---

### T003 [US1] — the adversarial cases (phase 4a)

**Agent**: `test-adversary`. **Touches**: the same new test file.
**May not touch `AspireFixture.cs`.**

ADR-0144 pairs the adversary with the test-writer *"where the issue is about a
failure mode"*, and this issue is nothing else. Add:

4. `A_migration_whose_exit_code_was_never_observed_is_not_a_failure` —
   `ExitedNonZero((int?)null)` false. **Written with an explicit, present
   `(int?)null` argument.** Not an absent dictionary key, not an empty
   dictionary, not a default. This is the assertion the whole change's
   mutation-resistance rests on: the mutant that drops the null guard aborts
   every healthy boot in the repository, and #2061's blocker was this exact
   branch passing for the wrong reason.
5. `A_negative_exit_code_is_a_failure` — `ExitedNonZero(-532462766)` true. The
   value #2061's phase 5 actually observed. Kills a `> 0` mutant that every
   other test here would let through.

Then **look for what these five miss** and report it rather than silently
widening scope. Specifically worth probing: `int.MinValue`; whether any test
here would still pass if `FormatMigrationFailureMessage` returned the log
without the code; and whether the existing 18 tests in
`AspireFixtureReportSelectionTests` would notice if the `int?` overload
introduced in T004 changed the dictionary overload's behaviour.

**Done when**: five tests exist, all red, the red output is captured, and the
report names the gaps it chose not to fill.

**Depends on**: T002.

---

### T004 [US1] — the rule and the message (phase 4b)

**Agent**: `infra-engineer`. **Touches**: `AspireFixture.cs`.
**May not edit the tests from T002/T003.**

Per `plan.md` §"Phase 4a design":

- Factor `ExitedNonZero`'s rule into `internal static bool ExitedNonZero(int? exitCode)`
  and make the existing dictionary overload call it. Move #2061's comment onto
  the rule, where it now lives.
- Add `internal static string FormatMigrationFailureMessage(int? exitCode, string migrationsLog)`,
  cause sentence first, log last, wording aligned with `FormatLikelyCause`.

**Done when**: the five new tests pass **and** the 18 existing
`AspireFixtureReportSelectionTests` pass **unmodified**. If any of the 18 has to
change, the refactor changed behaviour — stop and block (ADR-0144 §4a).

Do **not** wire the call site here. This commit builds and is green on its own.

**Depends on**: T003.

---

### T005 [US1] — the wait asks how it finished (phase 4b)

**Agent**: `infra-engineer`. **Touches**: `AspireFixture.cs:124–126`.

Replace the string-overload wait with the predicate overload for its **return
value**, read the exit code off the returned snapshot, and throw
`InvalidOperationException` when it is non-zero — capturing `migrations`' own
log through the existing `CaptureOneResourceLogAsync` **only on that branch**.
Exact shape in `plan.md`.

The four rulings that are not the engineer's to revisit:

- the predicate filters on **state only** — `ExitCode is null or 0` inside a
  predicate fails open (`spec.md`);
- `InvalidOperationException`, never `TimeoutException` and never an
  `OperationCanceledException` subtype (FR-006 — the catch at `:182` would
  swallow it);
- no log capture on the healthy path (FR-004);
- the message is scoped to `migrations`, not the whole timeout report.

**Done when**: `dotnet build -c Release` is clean at zero warnings under
`TreatWarningsAsErrors`, with **no suppression added**, and the full
`AspireFixtureReportSelectionTests` + `AspireFixtureMigrationGateTests` are
green.

**Depends on**: T004.

---

### T006 [US1] — measure, and write it down (phase 5)

**Agent**: `infra-engineer` or the orchestrator. **Touches**:
`specs/062-a-wait-that-asks-how-it-finished/verification.md` (new).

1. Re-apply the scratch line, run T001's exact command, `> post-fix.log`, twice.
2. Revert the scratch line; `git status --short` empty.
3. Healthy path — the #1918 regression check, not optional:
   ```sh
   dotnet test … --filter "FullyQualifiedName~LogTailDeliversIntegrationTests"
   ```
   Expected `Passed! - Failed: 0, Passed: 3`.
4. Write the note with:
   - **the figure the issue asked for**: pre-fix duration, post-fix duration,
     the difference in seconds, and the machine named;
   - the post-fix exception verbatim, showing it names `migrations` and the
     code;
   - the CI saving stated as a **bound** — *at most* the 8-minute
     `StartupTimeout`, against run 33623647778's measured `8 m 49 s` test
     duration — never as a measurement, because CI's time-to-death is
     unrecoverable from a log full of `(no logs captured)`;
   - `Latency: N/A — test-harness startup diagnostic; touches no §IV leg.`
   - what was **not** covered, in the shape #2061's note used.

**Block if** the post-fix run still takes ~8 minutes: that is R1, the exit code
lagging the state, and `plan.md` §Risks names the contingency. Do not improvise
one.

**Depends on**: T005.

---

## Dependency graph

```
T001 (pre-fix runtime red)         ← must be first; a broken test build erases it
  └── T002 (standard red tests)
        └── T003 (adversarial red tests)
              └── T004 (rule + message)      ← builds green on its own
                    └── T005 (call site + throw)  ← the behaviour change
                          └── T006 (measure + verification note)
```

Strictly linear. No `[P]`.

---

## Commits (ADR-0030 Conventional Commits, ADR-0086 **no `Co-Authored-By`**)

**Every commit must build on its own** — rebase-merge lands them individually on
`develop`, so a commit that only compiles with its successor breaks `git bisect`
forever (CLAUDE.md). That rules out committing the red test alone: it does not
compile until T004. The red is therefore captured **as output, before the
commit**, which is what ADR-0139 and ADR-0144 actually require — the artifact is
the failing output, not a red commit.

| # | Tasks | Message |
|---|---|---|
| A | T002–T004 | `test(integration): a migration's exit code is a verdict, not a detail` |
| B | T005 | `fix(integration): the wait on migrations asks how it finished` |
| C | T006 | `docs(integration): what the fast fail saved, measured` |

Commit A carries the five tests and the two pure members together and is green.
Commit B is the behaviour change and is the one the PR is about. Both build
alone.

---

## Phase 3 gate

**The gate is: the feature's issue is on Project #13** (CLAUDE.md — per-task
issues stopped after spec 028; `/speckit-tasks` adds nothing to the board).

Already satisfied — `gh issue view 2064` reports
`projects: Smart Sentinel Eye (In Progress)`. Nothing to add, and nothing
should be added: filing six task issues here would bury the in-flight items the
board exists to show.

**The follow-up issue from `spec.md` scope ruling 2** — the eleven `Running`
waits — is **recommended, not filed**. Phases 1–3 do not file issues; that is
the orchestrator's call, and it should carry `agent:ready` only after a human
has read it.
