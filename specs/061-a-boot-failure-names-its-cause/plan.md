# Implementation Plan: A boot failure names its own cause

**Spec**: `specs/061-a-boot-failure-names-its-cause/spec.md`
**Branch**: `fix/2061-a-boot-failure-names-its-cause` (cut from `origin/develop` at `48f3b00`)
**Issue**: #2061
**Lane**: ADR-0144 autonomous

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`infra-engineer`** for phase 4b and phase 5. **`infra-reviewer`** for phase 6.

The Aspire test fixture is the layer every integration test stands on, which is
precisely how `infra-reviewer`'s brief describes its own remit, and it is the
reason that role exists at all. Nothing here is backend work: no bounded
context, no aggregate, no Application handler, no EF mapping, no HTTP surface.
`backend-engineer` would be reviewing this file for DDD rules that do not bind
it (see the spec's note on §II) and would miss the one thing that matters —
whether the fixture still boots and whether a green run proves anything.

**`security-reviewer` is not spawned.** ADR-0144 calls for it "where the change
touches a trust boundary". This change touches a test-harness diagnostic string.
There is no credential, no scope, no idempotency key, no tenant. Spawning it
would produce a report with nothing in it, and a habit of spawning reviewers
that find nothing is how a reviewer's silence stops meaning anything.
`/code-review` still runs, as ADR-0144 requires of every phase 6.

**Phase 4a is `test-writer` *and* `test-adversary`.** ADR-0144 asks for the
adversary "where the issue is about a failure mode". This issue is *entirely*
about a failure mode, and it is the second consecutive issue in this area where
a green test sat over a broken diagnostic (#2054). The adversary's specific
brief is in `tasks.md` T004 and it is not decorative: its job is to try to write
a test that passes against the *unfixed* code, and report if it succeeds.

### Declaration 2 — is the honest answer a new ADR?

**No. The lane proceeds.**

ADR-0144 forbids the lane from writing an ADR and requires blocking when an
issue's honest answer is a new architectural decision. It is worth being
explicit about why this is not one, because "it is only a test file" is not by
itself an argument — spec 059 had to reason about exactly this boundary for
exactly this file.

The decision already exists. **ADR-0103** chose the Aspire fixture as the
integration-test harness; what that harness reports when it cannot boot is an
implementation detail *inside* that choice, not a competing choice. Spec 059
reached the same conclusion about the same file in the same week and recorded
it: *"a testing-infrastructure detail inside ADR-0103's choice of harness"*.
**ADR-0139** and constitution §Testing supply the rest — a rule that matters
fails the build, and the new selection test is that rule's instantiation.

Concretely, this change introduces no component, no boundary, no dependency, no
message, no persisted shape, no cross-context reference, and no rule binding
anyone outside this one file. It changes a boolean from ignoring a fact to
consulting a fact that was already being collected six lines away.

**What *would* have needed an ADR, and is deliberately not proposed**: a
general diagnostic-report abstraction; a change to what the fixture's failure
contract is (it still throws `TimeoutException` from `InitializeAsync`); a rule
about how one-shot resources are declared across the AppHost; or the
`TailedResources` policy change ruled out in the spec. None of these are in
`tasks.md`, and an engineer who finds themselves reaching for one should stop
and block rather than build it.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing. Phase 4a is red.**

The report will select a resource it does not select today and will say things
it does not say today. That is a change in observable behaviour, and ADR-0144's
tie-break ("ambiguity resolves to behaviour-changing") would land here even if
it were arguable, which it is not.

**One caveat, declared here rather than discovered at 4b.** The first task
(T001) is a *behaviour-preserving* signature widening, and it exists solely so
that the red assertion in T002 is expressible. This is explained in full below
under *The red test*, because it is the one place this plan could be read as
smuggling implementation past the red gate, and it should be checkable rather
than trusted.

---

## The red test — design, and what it cannot do

### Why the test the issue describes cannot be written today

The issue says: *"A test that hands the selection logic a state map containing
`migrations: Finished (exit code 134)` plus nine `FailedToStart` services fails
today."* There is no such state map. The signature is

```csharp
internal static string[] SelectResourcesToReport(Dictionary<string, string> states)
```

and the values are bare state text — `"Finished"`, `"FailedToStart"`. The exit
code lives in a separate `_exitCodes` dictionary that this function has never
been given. **The absence of the exit code from the signature is the defect,
expressed as a type.**

So a test asserting `migrations` is selected cannot compile against today's
code. **A compile error is not a red test.** ADR-0139 wants an observed
assertion failure that names the behaviour, and ADR-0144 makes that failing
output a transported artifact quoted in the PR. `error CS1501: No overload for
method 'SelectResourcesToReport' takes 2 arguments` transports nothing: it says
the method has one parameter, not that the report misses the cause.

I considered three ways out and am ruling on the third.

- **Accept the compile error as the red.** Rejected. ADR-0144 admits a
  compile-time guarantee only for a *refactor* whose point is that a wrong call
  stops compiling. This is a behaviour fix, and using that clause here would be
  borrowing an exemption written for the other colour.
- **Have `test-writer` add the overload itself.** Rejected. ADR-0144 forbids
  `test-writer` from touching implementation code, and the prohibition is not
  negotiable for convenience.
- **Split the signature widening into its own task, done first, by the
  engineer, as a behaviour-preserving change with its own green evidence.**
  **Adopted.**

### The shape T001 lands, specified here so no one invents it at 4b

The architect specifies the target signature; the engineer does not choose it
after reading the code. Replace the signature rather than adding an overload —
an overload would leave a dead one-argument path behind, and ADR-0036 asks for
the smallest change, not the most additive one.

```csharp
internal static string[] SelectResourcesToReport(
    Dictionary<string, string> states,
    Dictionary<string, int?> exitCodes)
```

`FormatResourceStates` already takes exactly this pair, so the two `internal`
entry points into the report become symmetric rather than divergent — which is
itself a small argument that this is the shape the code wanted.

**T001 changes no behaviour**: the new parameter is accepted and not yet
consulted. Its covering tests are the five existing `SelectResourcesToReport`
tests, which are updated **only** by threading an extra argument at the call
site. Under ADR-0144's characterisation rules this is the explicitly permitted
class of edit — *"updating a type name at a construction site is mechanical and
allowed; changing an asserted value is not."* **No asserted value changes in
T001. If one needs to, stop and block**: that would mean the widening changed
behaviour, which it must not.

The green output of those five tests, captured before and after T001, is the
characterisation evidence for that task and goes in the PR alongside the red
output for T002.

### The red output — concretely, as asked

T002 adds `A_one_shot_that_died_is_reported` to
`AspireFixtureReportSelectionTests`, driven from run 33623647778's actual
state map: nine `FailedToStart` services, `migrations` `Finished` with exit
code 134, the `-rebuilder` resources `NotStarted`, the databases and
infrastructure `Running`.

Against T001's pass-through implementation it will fail on the assertion. With
Shouldly, `ShouldContain` on a `string[]`:

```
Shouldly.ShouldAssertException :
AspireFixture.SelectResourcesToReport(states, exitCodes)
    should contain
"migrations"
    but was
["audit-observability", "automation", "camera-catalog", "event-ingestion",
 "identity", "layout-composition", "overlay-designer", "stream-distribution",
 "system-variables"]
```

That is the artifact. It names the missing resource, prints the nine
irrelevant ones that *were* selected, and is legible to someone who has never
read this spec — which is the test ADR-0139 asks for and the one #2054 did not
produce.

A second red arrives from T005's formatting test, which asserts on the produced
report string rather than the selection array:

```
Shouldly.ShouldAssertException :
report
    should contain
"migrations (Finished, exit code 134"
    but was not found. Actual:
"---- audit-observability (FailedToStart …
```

### Correction (2026-09-04, during 4b): that second red had nowhere to happen

**The paragraph above names a red the plan gave no tree to observe it in, and
its own worked example gives the mistake away.** The "Actual" it predicts —
`---- audit-observability (FailedToStart …` — is a header only the T005 *fix*
produces. Before that fix the header is `---- audit-observability ----`. A
predicted failure whose actual value already contains the fix is a failure
that cannot occur.

The cause is that **T005 bundled a new seam with the new behaviour through
it**. `FormatFailedResourceReport` and `FormatLikelyCause` did not exist
before T005 and were correct after it, so at no commit could a test call them
and fail. Against T003's tree the test does not fail, it does not compile:
`CS0117: 'AspireFixture' does not contain a definition for
'FormatFailedResourceReport'` — the exact outcome *The red output* rejects
three paragraphs earlier for T002.

**The remedy was already in this document and was applied to one of the two
tasks that needed it.** T001 exists because a widened signature is a seam
change that must precede the behaviour that uses it. That is the same
argument, and it was not carried to T005 — the reasoning was pinned to the
narrow case (*"the absence of the exit code from the signature is the defect,
expressed as a type"*) rather than to the general one.

**Generalised, for the next plan**: a behaviour-preserving prelude is required
wherever the fix introduces **a seam the test must reach through**, not merely
where a signature widens. Adding a method counts. Extracting one counts.
If the test's target symbol does not exist on the tree before the fix, the
red is a compile error and the gate is unmet.

T005 is therefore split into **T005a** (extraction only, output byte-identical,
seven pre-existing tests green) and **T005b** (the `Likely cause:` line and the
explanatory headers). `tasks.md` carries the task-level record. The red T006
actually produces at T005a is three assertion failures, quoted in the PR body;
the corrected shape of the one predicted above is:

```
Shouldly.ShouldAssertException : report
    should contain (case insensitive comparison)
"migrations (Finished, exit code 134"
    but was actually
"---- audit-observability ----
(no logs captured)
---- automation ----
(no logs captured)
---- ca..."
```

**Found by running T006 at the commit before its fix** — which is the check
that should be routine, and is the only reason this was caught rather than
asserted. Nothing was pushed; the branch was re-sliced in place.

### The honest limits — three, stated plainly

1. **The unit seam proves selection and formatting. It does not prove log
   retrieval.** Whether `ResourceLoggerService.WatchAsync` returns anything for
   an already-exited resource is untested and untestable here. The spec's
   *Tier 2* says why the change is worth landing either way and names the
   trigger that would open a follow-up issue. **Do not let phase 5 claim
   otherwise**: "the report now shows migrations' logs" is a claim nobody can
   make from this evidence. "The report now names migrations and its exit code"
   is provable and is what the verification note must say.

2. **The red for T002 is red against T001's code, not against `develop`'s.**
   Against `develop` it does not compile. This is disclosed here, must be
   disclosed in the PR body, and is the reason T001 exists as a separate,
   separately-evidenced commit rather than being folded into the fix. A reader
   who wants to check it can: T001's diff contains no conditional.

3. **No test here can fail if the real boot failure changes shape.** The state
   map in T002 is a transcription of one CI run. If Aspire renames a state or
   stops reporting `ExitCode` on the snapshot, these tests stay green while the
   report silently degrades — the same class of defect as #2054, one level up.
   There is no cheap guard for it, `LogTailDeliversIntegrationTests` is the only
   thing in the repo that touches the real thing, and it needs Docker.
   **Recorded, not solved.** It is not worth an issue on its own.

---

## Architecture

### Bounded context and layers

**None.** This is not bounded-context work. The change lives entirely in
`tests/Integration.Tests/Fixtures/` — **not one file under `src/` is touched**,
and that is worth stating positively because it is what keeps the change small
and what makes the latency ruling trivially true.

| Concern | Location |
|---|---|
| Selection decision | `tests/Integration.Tests/Fixtures/AspireFixture.cs` — `SelectResourcesToReport`, `IsHealthy` |
| Report formatting | same file — `CaptureFailedResourceLogsAsync`, `CaptureOneResourceLogAsync`, `FormatResourceStates`, and the `throw` in `InitializeAsync` |
| Tests | `tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs` |

Boundary rules (ADR-0027, NetArchTest) are untouched: nothing crosses a context,
nothing new references `Shared.Contracts`, no project reference changes.

### Entities, value objects, invariants

**No domain model, so none.** Constitution §II binds domain models and
`PrimitiveBoundaryTests` scopes to `src/*/Domain`; a `Dictionary<string,
string>` inside a test fixture is out of that scope, and wrapping resource
names in a value object here would be the speculative generality ADR-0036
forbids. Written down so phase 6 dismisses it in a line.

The one *invariant* worth naming is a classification rule, and it is the whole
fix:

> A resource is healthy when it is `Running`; or it is a `-rebuilder` that
> never started; or it is a one-shot that `Finished` **and did not exit
> non-zero**.

Read the last clause carefully: **"did not exit non-zero"**, not "exited
zero". Absent is not failure (spec FR-002, assumption A1). The existing test
`A_one_shot_job_that_finished_is_not_reported` supplies no exit code at all and
must keep passing with its assertion untouched — it is the guard on this exact
distinction, and it becomes load-bearing where it used to be incidental.

### Messaging (domain event → integration event)

**None.** No event is raised, published, or consumed. No Wolverine handler, no
`Shared.Contracts` change, no queue. The change produces a string.

### Boundary rules

- No cross-context project reference is added or needed.
- `Architecture.Tests` is **not** modified. `LogTailCoverageTests` reads
  `TailedResources` from source; that list is deliberately unchanged (spec
  ruling), so the guard's verdict is unchanged.
- **No new source-scanning architecture test is to be written.** The repo has
  recorded the reason: a guard that reads the design artefact proves the design
  was written down, not that it holds. The behavioural test in
  `AspireFixtureReportSelectionTests` is the guard here, and it is a better one
  because it exercises the decision rather than describing it.

### Report shape after the change

The target, stated so T005 has something to assert against and the engineer is
not inventing prose at 4b:

```
Aspire AppHost did not start within 8 minutes.
Likely cause: migrations exited with code 134.
Resource states:
  ...
  migrations: Finished (exit code 134)
  ...
Failed-resource logs:
---- migrations (Finished, exit code 134 — the process ran and died) ----
<its logs, or "(no logs captured)">
---- audit-observability (FailedToStart — never launched, so an empty log is expected) ----
(no logs captured)
---- automation (FailedToStart — never launched, so an empty log is expected) ----
(no logs captured)
  ... seven more ...

Last camera-catalog logs:
...
```

Three deliberate properties:

- The word **"exited"** appears, so `grep -iE "exit|abort|fail"` on a future CI
  log finds the cause. The spec's Tier 1 step 4 asserts exactly this.
- The nine placeholders are **explained rather than removed**. The spec's scope
  ruling argues at length that this is worth more than deduplication; the
  header text is where that argument is cashed out.
- `migrations` sorts first only by accident of `Ordinal` ordering among these
  names. **Do not rely on ordering for prominence** — the `Likely cause:` line
  is what carries it, which is why FR-005 exists rather than "sort failures
  first". Sorting is presentation; the cause line is a statement.

### Ordering within the failed-resource section

Leave `OrderBy(name, Ordinal)` alone. Re-ordering to put non-zero-exit
resources first is a fourth change with its own risk of breaking the existing
`ShouldBe([...])` sequence assertions, and FR-005 already delivers prominence
by a route that cannot be missed. If phase 6 wants it, it is a separate issue.

---

## Constitution and ADR alignment

| Rule | Status |
|---|---|
| §II value objects | Not engaged — no domain model. |
| §III bounded-context isolation | Not engaged — no context. |
| §IV latency budget | **N/A — no leg.** Test-harness only; no production code, no event-to-overlay path. |
| §VII observability | Not engaged — no leg is implemented or re-timed. |
| §Testing (new behaviour red) | Satisfied by T002/T005; see *The red test* for T001's carve-out and its disclosure. |
| §Testing (refactors green) | T001 is behaviour-preserving with the five existing tests as its net. |
| ADR-0036 smallest change | The fix is one predicate clause plus header strings. Deduplication and `TailedResources` both declined, in writing. |
| ADR-0052/0053 | xUnit + Shouldly, sentence-style underscore names. |
| ADR-0084 metrics | `AspireFixture.cs` is **742 lines against a 300-line limit** — see below. |
| ADR-0086 | No `Co-Authored-By`. |
| ADR-0087 | Rebase-only; each commit must build on its own — T001 and T002 must each compile and pass independently, since rebase-merge lands them separately on `develop`. |
| ADR-0103 | The anchor. The fixture is the harness; this is inside that decision. |
| ADR-0105 | `Ensure.That` if a guard is genuinely needed. It should not be: these are `internal static` helpers with one caller inside the type. **Do not add drive-by guards.** |
| ADR-0139 | Red observed and quoted. |
| ADR-0144 | Declarations above; two-agent 4a; review after merge. |

**On ADR-0084 and the 742-line file.** `AspireFixture.cs` already exceeds the
300-line limit by a factor of two and a half, and this change adds to it. That
is a pre-existing condition, not one this work creates, and **splitting the
fixture is not in scope** — it would be a large behaviour-preserving refactor
travelling with a bug fix, which ADR-0144 says explicitly is two issues.
Whether the analyzer actually fires on this file is unknown to me; if the
Release build fails on it at 4b, that is a **blocking discovery**, not
something to suppress. ADR-0144: the lane may not add a suppression to get
green. Raise it and block.

---

## Risks

| Risk | Mitigation |
|---|---|
| The one-shot exemption is loosened too far and #1918 regresses | FR-002 plus two tests: exit code 0 not selected, absent exit code not selected. The second already exists and its assertion must not be edited. |
| `ExitCode` is `null` on a successful run and the null branch is wrong | Both null and 0 are treated as healthy, so the behaviour is identical either way. Assumption A1. |
| The engineer folds T001 into T003 and the red is a compile error | T001 and T003 are separate tasks with separate commits and separate evidence. ADR-0087 requires each to build alone anyway. |
| ~~The same for T005~~ **— this one happened.** The row above was written for the signature seam and not asked of the extraction seam, so T005 shipped both in one commit and T006 had no tree to fail on. | Split into T005a/T005b during 4b, before any push. See *Correction* above. The general form of the mitigation is: **for every task whose test names a symbol the previous commit lacks, there must be a prelude commit.** |
| A test is written that would pass unfixed | T004 is `test-adversary`'s explicit job: try it, and report if it succeeds. |
| Phase 5 overclaims log retrieval | Stated as an honest limit in three places. The verification note must say "names migrations and its exit code", not "shows migrations' logs". |
| `AspireFixture.cs` trips SonarAnalyzer at Release | Block, do not suppress. |

---

## What is explicitly not being built

- Deduplication of placeholder lines (spec scope ruling — declined with
  arithmetic, not overlooked).
- `migrations` added to `TailedResources` (spec ruling — wrong half, unpriced
  cost, and the guard's own doc warns against exactly this entry).
- Any change to why the MigrationRunner exits 134 (#2062).
- Any change to #2060, which stays blocked.
- Splitting `AspireFixture.cs` for ADR-0084.
- Re-ordering the failed-resource section.
- A source-scanning architecture guard.
