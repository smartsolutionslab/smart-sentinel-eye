# Tasks 188 — An override the AppHost hears

**Spec:** `specs/188-an-override-the-apphost-hears/spec.md`
**Plan:** `specs/188-an-override-the-apphost-hears/plan.md`
**Issue:** [#2254](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2254)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-20 via
`gh project item-list 13 --owner smartsolutionslab --limit 2000`.
**No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

| | |
|---|---|
| **Phase 4a colour — US1** | **RED** (behaviour-changing) |
| **Phase 4a colour — US2** | no new tests; existing suites captured green before, green **unmodified** after |
| **Phase 4a colour — US3** | characterisation by **code hash** (comment-only) |
| **Phase 4a role** | `test-writer` |
| **Phase 4b role** | `infra-engineer` |
| **Phase 6 roles** | `infra-reviewer` (primary) + `security-reviewer` (narrow scope, plan §7) |
| **Not involved** | `backend-engineer`, `frontend-engineer`, `frontend-reviewer` |
| **New ADR needed** | **no** (spec §6 records what a reviewer might want written down, and why it is a separate human-started issue) |
| **Latency budget** | **N/A** — no production assembly on any of the six legs changes |

**US1's first run is not all-red.** Two of its facts assert the fallback still
holds and must be **green** in that same run. An all-red first run means the
fallback broke and is a stop condition, not progress.

---

## Files this spec touches

| File | Status | Story |
|---|---|---|
| `src/AppHost/OverridableParameters.cs` | new | US1 |
| `src/AppHost/AppHost.cs` | modified (ten lines, method name only) | US1 |
| `tests/Integration.Tests/AppHostParameterOverrideTests.cs` | new | US1 |
| `tests/Integration.Tests/AppHostE2ESwitchTests.cs` | comments only | US3 |
| `tests/Integration.Tests/AppHostStackStatusTests.cs` | comments only | US3 |
| `tests/Integration.Tests/AppHostMediaMtxImageTests.cs` | comments only | US3 |
| `tests/Integration.Tests/AppHostMigrationGateTests.cs` | comments only | US3 |
| `tests/Integration.Tests/AppHostReplicaCountTests.cs` | comments only | US3 |
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | **unchanged** | US2 |

`AspireFixture.cs` appearing with no diff is deliberate and is US2's whole
point: its four arguments start working without being edited.

---

## Phase 0 — spike and baselines (blocks everything)

### `[T001]` `[US1]` Prove a model-only test can read a parameter's resolved value

**Blocks all of phase 4a.** Plan §5 R3 records this as the spec's one marked
assumption.

Write a throwaway probe (not committed) that calls
`DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>`
with the fixture's arguments, takes the `ParameterResource` named
`MigrationRunnerClientSecret` out of `builder.Resources`, and awaits
`GetValueAsync(cancellationToken)` under a short deadline.

- **Expected:** it returns `dev-only-migration-runner-secret` promptly, because
  `WaitForValueTcs` is null outside a started application and `GetValueAsync`
  then returns the lazy value.
- **If it hangs or throws:** do not work around it. Report, and fall back to
  reading the value through the resource's own snapshot, or move the new tests
  into the Docker-requiring fixture lane. Either fallback changes T006's cost,
  so it is a decision to surface, not to take quietly.

**Done when:** the probe's output is pasted into the task record, including the
elapsed time. Delete the probe.

---

### `[T002]` `[P]` `[US2]` Capture the existing suites green, before anything changes

Run and **save the verbatim output** of:

1. the five `FixtureLogic` classes (`AppHostE2ESwitchTests`,
   `AppHostStackStatusTests`, `AppHostMediaMtxImageTests`,
   `AppHostMigrationGateTests`, `AppHostReplicaCountTests`) — Docker-free;
2. the Docker-requiring `Integration.Tests` suite via `AspireFixture`.

This is US2's green-stays-green baseline. A pass count alone is not enough —
save the per-test list, because "green afterwards" has to mean *the same tests*,
not the same number (this repo has been caught by a re-run that erased what it
was compared against).

**Machine churn note:** the first run after a machine has been busy looks like a
regression. If anything fails here, run it a second time before treating it as a
baseline failure.

**Done when:** both outputs are saved and referenced by path in the PR body.

---

### `[T003]` `[P]` `[US1]` Record the pre-change manifest

Generate the Aspire manifest on `origin/develop` and save it. This is the
artifact T010 diffs against for plan §5 R2 (no secret gains a `default`).

If manifest generation cannot be run in this environment, say so explicitly and
record R2 as **discharged by construction instead**: mechanism B keeps
`ParameterResource.Default` null, which is the field
`ManifestPublishingContext.WriteParameterAsync` gates the `default` object on.
Write down which of the two forms of evidence was obtained — never leave it
implied.

**Done when:** either the manifest file is saved, or the by-construction note is
written with the decompiled line quoted.

---

## Phase 4a — tests first (`test-writer`)

### `[T004]` `[US1]` Write `AppHostParameterOverrideTests` and observe it RED

New file `tests/Integration.Tests/AppHostParameterOverrideTests.cs`,
`[Trait("Category", "FixtureLogic")]`, model-only (`CreateAsync` starts no
resources, so it costs no container and is safe beside a live stack — the
reasoning and the trait the five sibling classes already use).

Five facts, sentence-style with underscores (ADR-0053), Shouldly (ADR-0052):

| Fact | Asserts | First run |
|---|---|---|
| `A_parameter_argument_overrides_the_declared_default` | `Parameters:MigrationRunnerClientSecret=deliberately-wrong` resolves to `deliberately-wrong` | **RED** (`dev-only-migration-runner-secret`) |
| `Every_declared_parameter_honours_its_own_override` | enumerate every `ParameterResource` in the composition, build a second model overriding each with a value derived from its name, assert each resolves to its own override | **RED** (ten failures) |
| `A_parameter_with_no_argument_keeps_its_default` | with no `Parameters:` argument, every parameter resolves to the literal at its call site | **GREEN** — and must stay green |
| `An_empty_parameter_argument_keeps_its_default` | `Parameters:MigrationRunnerClientSecret=` resolves to the default, matching Aspire's own null-or-empty semantics | **GREEN** — and must stay green |
| `An_override_does_not_change_the_composed_resource_set` | the resource names with and without overrides are the same set | **GREEN** — the guard that the fix changes values and nothing else |

Constraints:

- **The parameter count comes from the composition, never from a list in the
  test.** A name list would pass unchanged when an eleventh parameter is added
  without the helper — the exact drift this spec exists to close.
- **No assertion may check its own input.** The expected value is composed by
  the test; the subject is what the AppHost resolves. Confirm each assertion can
  fail if the AppHost changes without the assertion text changing.
- **Wait for a condition against a deadline, never for a count of yields**
  (ADR-0150) if any await here needs bounding.
- Do not touch `AppHost.cs`. Do not touch any existing test.

**Done when:** the run is captured **verbatim** — two facts red with their
actual-vs-expected values quoted, three green. That output is the brief handed
to phase 4b and is quoted in the PR body (ADR-0139).

**Stop condition:** all five red. That means the fallback path broke and the
test file is wrong, not the AppHost.

---

## Phase 4b — implementation (`infra-engineer`)

The phase-4a output is this phase's brief. **The tests may not be edited to make
them pass.**

### `[T005]` `[US1]` Add the `AddOverridableParameter` helper

New file `src/AppHost/OverridableParameters.cs` — `internal static class`,
single extension method, exactly the shape in plan §2.4.

- `string.IsNullOrEmpty(configured) ? fallback : configured` — **not** `??`.
  An empty argument must fall back, matching Aspire's own `GetParameterValue`.
- Key is `"Parameters:" + name`, derived from the parameter's own name at the
  one place the name is written.
- No `Ensure.That` guard — ADR-0105 exempts the AppHost by name.
- No comment restating what the code says; one comment only, for the *why*:
  that the literal overload never reads configuration, with the issue number.

**Blocks T006.**

**Done when:** it compiles and `dotnet format` / analyzers are clean.

---

### `[T006]` `[US1]` Switch the ten call sites

`src/AppHost/AppHost.cs:28–64`. `AddParameter` → `AddOverridableParameter` at
all ten, and the positional `value` argument becomes the `fallback`.

- **All ten, not the four that are passed today.** The parameter spec 128
  actually needed — `MigrationRunnerClientSecret` — is not among the four
  (spec §2).
- **Every existing comment block above these lines stays**, unedited. They
  record which realm client each secret mirrors, which is why an override is a
  working failure injector.
- **Nothing else in the file changes** — no resource, no `WithReference`, no
  replica count, no image tag. If the diff shows anything else, it is a
  drive-by; remove it.

**Done when:** T004's two red facts are green, its three green facts are still
green, and the run output is captured.

---

### `[T007]` `[US2]` Re-run the five `FixtureLogic` classes, unmodified

Against T002's saved baseline, test for test.

**Done when:** the same tests pass. **If any assertion needs editing, block** —
that is evidence behaviour moved somewhere it was not meant to, not a test that
needs adjusting.

---

### `[T008]` `[US2]` Re-run the Docker-requiring integration suite, unmodified

One machine, one Aspire stack: confirm no other stack is running first, or the
`FailedToStart` that follows reads exactly like a code defect. Stop any live
`aspire run` before building, or MSB3027 will look like a broken build.

**Done when:** green against T002's baseline, test for test, with the output
saved. A second run is warranted before calling any failure real.

---

### `[T009]` `[US1]` Release build, format and analyzers clean

`dotnet format` and a Release build (where `TreatWarningsAsErrors` bites and the
collection-expression rule is at `warning`).

Note: a Release analyzer error on a file outside the diff has been flaky here
before; re-run the same SHA once before investigating.

---

### `[T010]` `[US1]` Prove no secret gained a manifest `default`

Diff the generated manifest against T003's, or discharge by construction with
the quoted `WriteParameterAsync` gate, whichever form T003 established.

**Done when:** the result is written down as observed — stating which of the two
evidence forms was used. A result reported only in conversation is invisible to
every later grep and reviewer.

---

## Phase 4b — US3 (comment-only, parallel-safe)

### `[T011]` `[P]` `[US3]` Correct the five arrays' doc comments

Touch **only** XML docs and comments in:

- `AppHostE2ESwitchTests.cs`
- `AppHostStackStatusTests.cs`
- `AppHostMediaMtxImageTests.cs`
- `AppHostMigrationGateTests.cs`
- `AppHostReplicaCountTests.cs`

Each comment claiming the array is "the shape the end-to-end job boots" or "the
shape a developer's `aspire run` … see" must stop claiming CI passes
`Parameters:` arguments. `ci.yml:467` passes `ScenarioSimulator=` and
`StackStatusFile=` and nothing else; `aspire run` passes nothing. Record the
line reference so the next reader can check rather than trust.

**The arrays themselves do not change** — spec §5 gives the reasoning.

**Disjoint from every US1 file**, so a different agent in a different worktree
can do this concurrently (ADR-0109).

---

### `[T012]` `[US3]` Prove nothing but comments moved

For each of the five files, strip comments and XML docs from the `origin/develop`
version and the changed version, hash both, require them equal.

**Never assert that the prose contains a string** — that checks the change's own
input and cannot fail for the reason it claims to.

**Done when:** five pairs of equal hashes are recorded in the PR body.

---

## Phase 5 — verification (`/verify`)

### `[T013]` `[US1]` Observe the injection working, and its counterfactual

Spec §7, all five steps, in order. The short form:

1. Boot with `Parameters:MigrationRunnerClientSecret=deliberately-wrong` →
   `migrations` **fails**, with the Keycloak token failure quoted.
2. Boot without it → `migrations` **succeeds**.
3. Same command as (1) on `origin/develop` → `migrations` **succeeds**. This is
   the step that separates "the override works" from "something else broke the
   stack", and it is the one most often skipped.
4. With the fixture's stack up, read the **observed** Postgres credential and
   confirm it is `testpassword`, not `dev-only-postgres-password`. A green suite
   is consistent with the argument still being ignored, so the suite is not this
   evidence.

A persistent AppHost can outlive the code it runs — check the process start time
against the commit before trusting any manual observation.

**Done when:** `verification.md` records each observation as observed, with the
quoted output. Every figure written down, not merely reported.

---

## Phase 6 — review

### `[T014]` `[US1]` `infra-reviewer`

Composition correctness; that the helper is the mechanism plan §2 chose and not
a variant; that `AppHost.cs` carries no drive-by change; that the green runs
prove what they are cited for (especially T008 versus plan §5 R6).

### `[T015]` `[P]` `[US1]` `security-reviewer`, narrowly scoped

Three questions only (plan §7):

1. Does any `secret: true` parameter gain a manifest `default`?
2. Can an overridden value reach the stack-status report, a log line, or the
   dashboard in a way it could not before?
3. Does the empty-string handling permit an empty secret to be installed?

Disjoint from T014 in what it reads; both are read-only, so they run together.

---

## Dependency graph

```
T001 ──> T004 ──> T005 ──> T006 ──> T007 ──> T008 ──> T009 ──> T010 ──> T013 ──> T014
 │                                                                              └──> T015  [P]
T002 [P] ──────────────────────> (baseline for T007, T008)
T003 [P] ──────────────────────────────────────────────> (baseline for T010)
T011 [P] ──> T012                       (US3, disjoint from every US1 file)
```

**Blocking:** T001 gates all of phase 4a. T005 gates T006.
**Parallel:** T002 and T003 with each other and with T001; T011/T012 with the
whole US1 chain; T015 with T014.
