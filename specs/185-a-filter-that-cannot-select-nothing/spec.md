# Spec 185 — A filter that cannot select nothing

**Issue:** [#2289](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2289)
— **already on Project #13**, labels `bug`, `ci`, `agent:ready`, no
`agent:blocked`. Verified 2026-09-18 against a `--limit 2000` board dump by
`content.url`, not by the number filter. **No `item-add` needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **184** as the
highest merged. `gh pr list --state open` returns **no open PRs at all**, so no
unmerged sibling claims 185. This spec is **185**. (The number one above
develop's highest is not automatically free — it has collided twice this month —
so it was checked rather than assumed.)

**ADRs referenced:**

- **ADR-0033** (CI gates — maximum strictness, *"required checks (all
  blocking)"*). The ADR this spec serves: a blocking check that passes having
  executed zero tests is not a gate, it is a green square.
- **ADR-0139** (rules that fail the build, not the review — and the red-first
  obligation).
- **ADR-0036** (smallest possible change; no speculative generality).
- **ADR-0037** / **ADR-0144** (the phased workflow; the autonomous lane and its
  phase-4a colours).
- **ADR-0052** / **ADR-0053** (xUnit + Shouldly; sentence-style test naming).
- **ADR-0103** (integration tests are Aspire-only) — why the 30-minute Docker
  job exists at all, and therefore why the Docker-free slice at `ci.yml:72` was
  carved out of it.
- **ADR-0109** (the `[P]` disjoint-file rule).

**No new ADR is needed.** This spec decides nothing architectural: ADR-0033
already says every required check blocks, and this closes a hole in how one of
them reports. It does not amend the constitution.

**Latency budget (§IV): N/A.** The diff lands in `.github/workflows/ci.yml` and
`tests/Architecture.Tests/`. No production assembly changes, so none of the six
legs of `event arrival → overlay rendered` is touched and no leg's measurement
status moves.

---

## 1. The defect, re-measured on this tree

The issue was filed 2026-09-13. Everything below was **re-run on this worktree**
(`origin/develop` @ `d7674d12`) on 2026-09-18, because half of what the issue
cites has already drifted.

### 1.1 A filter that matches nothing exits 0 — confirmed

```
$ dotnet test tests/Shared.Kernel.Tests/SmartSentinelEye.Shared.Kernel.Tests.csproj \
    --no-build --nologo --filter "Category=NoSuchCategoryAtAll"
A total of 1 test files matched the specified pattern.
No test matches the given testcase filter `Category=NoSuchCategoryAtAll` in ...
BASELINE exit=0
```

Exit code read directly, not through a pipe (`tail` would have reported its own
status and masked it — the issue notes the same trap).

### 1.2 The two filtered steps — one citation holds, one has already gone stale

| What the issue says | What this tree says |
|---|---|
| `ci.yml:72` — `Category=FixtureLogic` | **Correct.** `:72` is the `dotnet test` line; the `--filter` is at `:75`. |
| `ci.yml:179` — the exclusion filter | **Stale.** `:179` is now `key: nuget-...` in the integration job's NuGet-cache step. The exclusion filter is at **`:287`**, inside a `dotnet test` starting at **`:284`**. |

This is not a footnote. `IntegrationTestSelectionTests.cs:43-44` holds those two
numbers as `const string` — and one of them **is already wrong**, silently, on
`develop`, today. The issue predicted "a single inserted line invalidates both
silently" as a future risk; it is not future. `git log -- .github/workflows/ci.yml`
shows six changes since the guard was written, `6fa7f2a2` (spec 181) among them.

The two constants are also cited at different granularities — `:72` names the
`dotnet test` line, `:179` was meant to name the `--filter` line — which is the
tell of a number nobody can check.

### 1.3 What the guard proves, and the exact shape of its blind spot

`IntegrationTestSelectionTests` reads every `.cs` under `tests/Integration.Tests`
and requires each class holding a `[Fact]`/`[Theory]` to carry either
`[Collection(AspireCollection.Name)]` or `[Trait("Category", ...)]` whose value
is one of four names. Those four names are a **regex literal** at `:81`:

```csharp
@"^[ \t]*\[\s*Trait\(\s*""Category""\s*,\s*""(FixtureLogic|Measurement|Disruptive|Maintenance)""\s*\)\s*\]"
```

The class is careful and well-tested — it refuses doc-comment mentions,
commented-out attributes, reflection mentions and misspelled values, each with
its own fact. **Nothing in it opens `ci.yml`.** It holds one half of a pair and
asserts the two halves agree by having the same words typed into both.

### 1.4 The silent scenario, named exactly

Rename the trait on the test side — `FixtureLogic` → `DockerFree` — in every
test class *and* in the guard's regex. Every test in the repository is green:
the guard credits `DockerFree`, and the Docker-free classes still carry a
declaration. `ci.yml:75` still says `Category=FixtureLogic`, selects **zero
tests**, and the backend job reports **success**.

That is the composite failure. Neither piece of this spec catches it alone from
both ends, and each catches it at a different moment:

- **US1** makes the *job* red, at the moment it runs, for any cause of
  zero-selection — including causes nobody anticipated.
- **US2** makes the *build* red, before CI is even reached, for this specific
  class of cause, and retires the stale line numbers.

### 1.5 An honest note on which step is actually at risk

The two filters are not equally exposed, and the spec should not pretend they
are.

- `Category=FixtureLogic` is an **inclusion** filter. A typo, a rename, or a
  class set that loses its last `FixtureLogic` trait all select zero. This is
  the live risk.
- `Category!=Measurement&Category!=Disruptive&Category!=Maintenance` is an
  **exclusion** filter over the whole suite. It can only select zero if the
  suite itself is empty. Its realistic failure is the opposite — an exclusion
  that stops excluding, which surfaces as a *red* job, loudly.

US1 is therefore applied to both steps because it is one flag and symmetry is
cheaper than an explanation, but its value is concentrated at `:75`. Saying so
here prevents the next reader from over-reading the exclusion step's protection.

---

## 2. The mechanism, chosen against the issue's two suggestions

The issue offers two: parse the `.trx` `<Counters executed="...">`, or grep
stdout for `No test matches`. There is a third, and it was **measured on this
tree** before it was chosen.

`RunConfiguration.TreatNoTestsAsError=true` is a VSTest run-setting the SDK
already supports (this repo is on VSTest: `Microsoft.NET.Test.Sdk` 18.9.0 +
`xunit.runner.visualstudio`, `global.json` pins SDK `10.0.300`). Passed after
`--`, it turns "no test matched" into a non-zero exit.

Measured 2026-09-18, with the **exact** flag set `ci.yml` uses, both directions:

```
# non-matching filter, with --blame-hang, with the flag
$ dotnet test ... --filter "Category=NoSuchCategoryAtAll" \
    --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min \
    -- RunConfiguration.TreatNoTestsAsError=true
NEGATIVE exit=1

# matching filter, same flags
$ dotnet test ... --filter "FullyQualifiedName~Result" \
    --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min \
    -- RunConfiguration.TreatNoTestsAsError=true
Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11 ...
POSITIVE exit=0
```

Both alternatives are worse here, and for reasons that matter to this particular
issue:

- **Grepping stdout for `No test matches`** makes a green gate depend on an
  English sentence emitted by a tool we pin and upgrade. When the wording moves,
  the check stops checking and stays green — *the same defect this spec exists
  to fix, rebuilt inside its own fix.*
- **Parsing `.trx` `<Counters executed="...">`** is structurally sound but costs
  more than it buys: the cheap step at `:72` writes no `.trx` today (it has no
  `--logger`), so it would need one added; the integration step's `.trx` lands
  in a directory whose layout is already documented at `ci.yml:297-320` as
  having surprised someone once; and the parser would itself need a
  file-missing-is-fatal branch, or it acquires a silent-green path of its own.
  Two new moving parts, one of them a new file on disk, to reach a verdict one
  flag already gives.

**The flag's own failure mode is named, not ignored.** It is a knob that could
be dropped in a future edit of `ci.yml`, and dropping it is silent. That is
precisely what US2's reader closes: the guard asserts the flag's presence on
every filtered `dotnet test` in the workflow, so removing it fails the build.
The two stories cover each other.

---

## 3. Scope

**In:**

1. `.github/workflows/ci.yml` — `-- RunConfiguration.TreatNoTestsAsError=true`
   on the two `dotnet test` steps that carry a `--filter` (`:72` and `:284`),
   each with a one-line *why*.
2. `tests/Architecture.Tests/IntegrationTestSelectionTests.cs` — reads `ci.yml`,
   locates the two filtered steps **by content**, derives the recognised
   category set from the filters it finds, asserts the flag is present on each,
   and reports real line numbers instead of holding frozen ones.

**Out, deliberately:**

- **A repository-wide `.runsettings` turning the flag on for every `dotnet test`.**
  Tempting and broader, but ADR-0036 (smallest possible change): the defect is
  filters, the unfiltered call sites cannot exhibit it, and a global run-settings
  file is a new artefact every future test invocation inherits.
- **The `.trx`/`<Counters>` route**, for the reasons in §2. Recorded as
  considered, not as forgotten.
- **Any change to which tests carry which trait.** The trait assignments are
  correct; only the machinery that holds workflow and tests together is not.
- **`scripts/coverage-check.ps1`.** It runs every non-Integration test project
  unfiltered; no filter, no exposure.
- **The frontend/e2e jobs.** No `--filter` anywhere in them (grepped).

---

## 4. User stories

### US1 (P1) — A filtered CI step that selects nothing fails

> As the person reading a green `backend` job, I want a filtered test step that
> matched zero tests to fail, so that "green" means tests ran and passed rather
> than that nothing was attempted.

Independently shippable: it is two lines of `ci.yml` plus the architecture test
that holds them there. Observable end-to-end by running the real command with a
deliberately wrong filter and reading the exit code.

### US2 (P2) — The guard reads the workflow it claims to guard

> As someone renaming a test category, I want the build to fail if
> `.github/workflows/ci.yml` still names the old one, so the pair is held
> together by a test instead of by two people typing the same word.

Independently shippable on top of US1 (or without it — it touches a different
file). Retires `CheapStep`/`ExcludeStep` as frozen line numbers, one of which is
**already wrong** (§1.2).

---

## 5. Acceptance scenarios (Gherkin)

### AS-1 — the defect as filed (US1, happy→loud)

```gherkin
Given ci.yml's Docker-free step filters on Category=FixtureLogic
  And no test class in tests/Integration.Tests carries that trait
 When the backend job runs that step
 Then dotnet test exits non-zero
  And the backend job reports failure
```

Today: exits 0, job green. Measured in §1.1.

### AS-2 — the positive control (US1, must not regress)

```gherkin
Given ci.yml's Docker-free step filters on Category=FixtureLogic
  And test classes carry that trait
 When the backend job runs that step
 Then dotnet test exits 0
  And the printed total is greater than zero
```

The flag must not turn a working step red. Measured in §2.

### AS-3 — the flag cannot be dropped (US1, guarded by US2's reader)

```gherkin
Given a dotnet test invocation in ci.yml that carries a --filter
 When it does not also carry RunConfiguration.TreatNoTestsAsError=true
 Then Architecture.Tests fails, naming the step's real line number
```

This is the fact that is **red on this tree today**, because neither step
carries the flag.

### AS-4 — the drift scenario (US2)

```gherkin
Given ci.yml's cheap step filters on Category=FixtureLogic
  And every test class has been renamed to Category=DockerFree
 When Architecture.Tests runs
 Then it fails, reporting that ci.yml names a category no test class declares
```

Today: green, because the guard reads only the test side.

### AS-5 — the reverse drift (US2)

```gherkin
Given a test class carries Category=Slow
  And ci.yml's filters name no such category
 When Architecture.Tests runs
 Then that class is reported as undeclared
```

Today this already holds — via a hard-coded regex. After the fix it holds
because the set came from `ci.yml`. **The assertion must not change; the source
of its truth must.**

### AS-6 — the bad-input case: the reader must not pass by matching nothing

```gherkin
Given ci.yml is restructured so no filtered dotnet test step can be located
 When Architecture.Tests runs
 Then it fails saying the reader is broken, not that the workflow is clean
```

Non-negotiable and directly in this spec's own subject matter. A reader that
returns an empty step list, or an empty category set, must fail loudly. An empty
category set would otherwise build a regex that credits **every** trait value, or
none — either way a guard that has stopped guarding while staying green. This is
the existing class's own `"the scan is broken, not the code"` pattern, applied to
the new half.

### AS-7 — the conflict case: the stale citation

```gherkin
Given IntegrationTestSelectionTests cites a location in ci.yml
 When ci.yml has changed such that the citation no longer resolves
 Then the guard fails rather than reporting a number that is merely wrong
```

`:72` resolves today; `:179` does not. **The red is asymmetric**, and that
asymmetry is the proof the reader works (see `tasks.md` §4a).

### AS-8 — auth/scope: N/A

No endpoint, no token, no scope, no fab boundary. Stated rather than omitted.

---

## 6. Independent end-to-end test procedure

Runnable by a reviewer with no knowledge of the diff. The first two need a
built `Integration.Tests`; the third does not.

**1. The zero-match counterfactual against the real project.**

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  -c Release --no-build --filter "Category=FixtureLogick" \
  --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min \
  -- RunConfiguration.TreatNoTestsAsError=true
echo "exit=$?"
```

Expect `exit=1` and `No test matches the given testcase filter`. **Read the exit
code without a pipe.** Drop the `-- RunConfiguration...` suffix and re-run:
expect `exit=0` — the defect, reproduced against the real project.

**2. The positive control.**

Same command with the correct `Category=FixtureLogic`. Expect `exit=0` and a
`Passed!` line with a non-zero total. Record the total; that number is the
answer to "did anything run".

**3. The guard's counterfactual, without touching the working tree.**

```sh
dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
  --filter "FullyQualifiedName~IntegrationTestSelectionTests"
```

Expect green. Then, on a scratch copy of `ci.yml`, misspell the cheap filter to
`Category=FixtureLogick` and re-run: expect red, naming the category `ci.yml`
filters on that nothing declares. Then restore, delete the
`RunConfiguration.TreatNoTestsAsError=true` from one step, re-run: expect red,
naming that step's **current** line number. Both are the counterfactual a guard
needs, and both must be performed — a guard asserted only from its own source
proves the design was written down, not that it holds.

---

## 7. Success criteria

- **SC-001** Running the cheap step's exact command with a misspelled category
  exits non-zero. (US1, AS-1 — measured, exit code quoted.)
- **SC-002** Running it with the correct category exits 0 with a non-zero test
  total. (US1, AS-2 — the total quoted.)
- **SC-003** Deleting the flag from either step in `ci.yml` fails
  `Architecture.Tests`, and the failure names that step's **current** line
  number. (US1+US2, AS-3/AS-7.)
- **SC-004** Misspelling a category in `ci.yml` alone fails
  `Architecture.Tests`. (US2, AS-4.)
- **SC-005** A reader that locates no filtered step, or derives an empty
  category set, fails with a message that says the reader is broken. (AS-6.)
- **SC-006** The **eight** pre-existing facts in `IntegrationTestSelectionTests`
  pass with their assertions **unmodified** (assertion *messages* that cite the
  retired `CheapStep`/`ExcludeStep` constants are updated; that is not an
  assertion change — `plan.md` §3.4). (AS-5 — characterisation.)
- **SC-007** No `const` in `IntegrationTestSelectionTests` holds a `ci.yml` line
  number. (§1.2.)

---

## 8. Assumptions, marked

- **A-1** `RunConfiguration.TreatNoTestsAsError` behaves on the CI runner
  (ubuntu, same pinned SDK) as measured here on Windows. Same SDK version via
  `global.json`, same VSTest; the flag is platform-independent. **Risk if
  wrong:** the flag is inert and the steps stay silently green — which is
  exactly today's behaviour, so the failure is no worse than the status quo, but
  it would be *believed* fixed. **Mitigated by** phase 5 quoting the
  counterfactual, and by the first CI run after merge being read for the
  positive control's non-zero total.
- **A-2** The `--` passthrough survives any future move of these steps into a
  script. It is a `dotnet test` argument, not a shell feature. Low risk.
- **A-3** The reader parses `ci.yml` as **text**, not as YAML. Consistent with
  every existing precedent in this repo (`AgentBriefClaimTests.WorkflowJobs()`,
  `AppHostE2ESwitchTests`, `AppHostStackStatusTests`), and it avoids adding a
  YAML parser dependency to a test project (ADR-0036). The cost: a step written
  in a different YAML style (flow scalars, a folded block) could evade the
  reader. **Mitigated by** AS-6 — a reader that locates nothing must fail.

---

## 9. Locked tech choices (nothing new)

| Concern | Choice | Source |
|---|---|---|
| CI | GitHub Actions, `.github/workflows/ci.yml` | ADR-0033 |
| Test framework | xUnit 2.9.3 + Shouldly | ADR-0052 |
| Test naming | Sentence-style with underscores | ADR-0053 |
| Guard placement | `tests/Architecture.Tests`, reading the tree from disk | existing class's own rationale |
| Workflow reading | Line-oriented text parse, no YAML dependency | `AgentBriefClaimTests`, `AppHostE2ESwitchTests` |
| No-test-selected | `RunConfiguration.TreatNoTestsAsError=true` | measured, §2 |
