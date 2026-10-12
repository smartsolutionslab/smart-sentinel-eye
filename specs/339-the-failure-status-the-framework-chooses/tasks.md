# Tasks 339: The failure status the framework chooses

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2571 (feature-level;
it is already on Project #13, and no per-task issues will be created).

**Phase 4a colour: RED** (behaviour-changing). T004 is the red test. The ten host cases and the
completeness test arrive green **by design**. The property they guard already holds, and T008
proves they bite (precedent: `DependencyHealthCheckGuardTests`).

**Engineer:** backend-engineer. **Test-writer:** T004, T006, T007. **Production code:** none
committed. T008's edits are local and are reverted.

Legend: `[P]` means the task can run in parallel with the other `[P]` tasks in its phase, because
it owns disjoint files. All files are under `tests/Architecture.Tests/` unless a path says
otherwise.

---

## Phase 1: Foundational (blocks everything)

- [ ] **T001 [US1]** `Persistence/PostgresComposition.cs`: change `ConfigurationFor` from `private`
  to `internal`. Visibility only. Run `dotnet test tests/Architecture.Tests --filter
  FullyQualifiedName~Persistence` before and after the change, and confirm it is green both times
  (characterisation, nothing to change). Commit alone.

## Phase 2: Harness (sequential, same folder)

- [ ] **T002 [US1]** `HealthChecks/HostCaptureCollection.cs` with
  `[CollectionDefinition(Name, DisableParallelization = true)]`, and `HealthChecks/ServedHealthHosts.cs`:
  - the ten hosts (assembly name plus project directory);
  - the union placeholder configuration over `PostgresComposition.ContextNames` (plan §4);
  - `AllHosts()` as `TheoryData<string>`;
  - the content root from `RepositorySource.RepositoryRoot()`.

  Depends on T001.
- [ ] **T003 [US1]** `HealthChecks/ComposedHost.cs`, the capture described in plan §3.2:
  - dedicated thread;
  - thread-filtered `DiagnosticListener`;
  - configuration through args;
  - optional `HostBuilding` hook;
  - sentinel thrown on `HostBuilt`;
  - unwrapping;
  - 60 s timeout naming the assembly;
  - "returned without building a host" failure;
  - doc comment stating the undocumented-hook dependency.

  Also add `HealthChecks/HealthCheckFailureStatusRule.cs` as a **stub** returning `[]`, and the
  ten-host theory in `HealthChecks/HealthCheckFailureStatusGuardTests.cs` under
  `[Collection(HostCaptureCollection.Name)]`. Run the theory: 10/10 green. That result is not
  evidence of anything, so say so in the commit body. Depends on T002.

## Phase 3: Red (phase 4a, test-writer)

- [ ] **T004 [US1] RED.** Add `The_guard_reports_a_check_registered_without_a_failure_status`.
  - It captures ApiGateway with the hook
    `b => b.ConfigureServices(s => s.AddHealthChecks().AddCheck("planted", () => HealthCheckResult.Healthy()))`.
  - It asserts the offender list has exactly one entry and that the entry names `"planted"` and
    `Unhealthy`.
  - **First verify plan §3.2's `HostBuilding` assumption:** the planted registration must appear
    in the captured `HealthCheckServiceOptions`. If it does not, stop and report. Do not improvise
    a `HostBuilt` mutation.
  - Run it against the stub. It must fail **on the Shouldly assertion**; a compile error does not
    count.
  - Return the verbatim output. It goes into the PR body.

  Depends on T003.

## Phase 4: Green (engineer)

- [ ] **T005 [US1]** Implement `HealthCheckFailureStatusRule.Offenders` as specified in plan §5:
  - the exempt `self` shape: name, tags exactly `{live}`, `DelegateHealthCheck` by `FullName`,
    exactly one per host;
  - `Unhealthy` makes any other registration an offender;
  - the offender format.

  T004 must pass **unmodified**, and all ten host cases must stay green. Depends on T004.

## Phase 5: Remaining cases (test-writer, after T005)

- [ ] **T006 [P] [US1]** In `HealthCheckFailureStatusGuardTests.cs`, add the rule cases. All run
  against ApiGateway through the hook:
  - explicit `failureStatus: HealthStatus.Unhealthy` is reported;
  - `self` with its tag changed to `ready` (through `Configure<HealthCheckServiceOptions>`) is
    reported as not matching the exempt shape;
  - a second `AddCheck("self", …)` makes both `self` registrations offenders.

  Depends on T005.
- [ ] **T007 [P] [US1]** Add the capture-failure cases to the same file, in a separate region:
  - Automation.Api with an empty dictionary throws an exception whose message contains
    "Keycloak base URL not found".
  - Capturing `Architecture.Tests`' own assembly fails with "returned without building a host".
    First confirm the assembly has an entry point (Microsoft.NET.Test.Sdk generates one). If it
    has none, use the "entry point is null" guard message instead, and record which in the
    commit.

  Depends on T003.

  *Note:* T006 and T007 both edit `HealthCheckFailureStatusGuardTests.cs`. Either run them in
  sequence, or have T007 put its cases in `HealthChecks/ComposedHostCaptureTests.cs`, which
  carries the same `[Collection]` attribute, so the two tasks own disjoint files. **Prefer the
  separate file.**
- [ ] **T008 [P] [US1]** Add `HealthChecks/ServedHealthHostCompletenessTests.cs` (FR-007): a
  source scan of `src/**/Program.cs` calling `MapDefaultEndpoints(`, mapped to the `.csproj` in
  the same directory and set-compared with `ServedHealthHosts`. Slash-normalise the paths. No
  collection attribute. Depends on T002.

## Phase 6: Bite (engineer; local edits, reverted)

- [ ] **T009 [US1]** Run the counterfactuals and quote each verbatim in
  `specs/339-the-failure-status-the-framework-chooses/T009-counterfactual.md` (plan §7):
  - (a) Set `src/ServiceDefaults/WolverineDefaults.cs:211` to `HealthStatus.Unhealthy`. **Exactly
    nine** context cases go red and ApiGateway stays green. Revert.
  - (b) Change the tag at `src/ServiceDefaults/Extensions.cs:129` to `"ready"`. **All ten** go red,
    naming `self`. Revert.
  - (c) Delete one entry from `ServedHealthHosts`. The completeness test goes red, naming that
    `Program.cs`. Revert.

  After each revert, confirm `git diff --stat src/` is empty. A restored file keeps its old
  timestamp, so force a rebuild before the re-run (memory). Depends on T005–T008.

## Phase 7: Verify and close (orchestrator)

- [ ] **T010 [US1]** Run the full Architecture.Tests suite with a Release build. It must be green,
  with no new warnings in the new files and no suppressions. Run `dotnet format --verify-no-changes`
  (#2175 gate). Record the run time of the new collection; target under 30 s (SC-005).
- [ ] **T011 [US1]** In the PR body, include:
  - the T004 red output;
  - the T009 counterfactual outputs;
  - the statement that the ten host cases and the completeness test arrived green by design;
  - `Phase 5` evidence: the counterfactual is the end-to-end observation for a build guard;
  - the clause-2 follow-up from plan §9, as a suggestion only.

  Use `--base develop`. Re-check that spec number 339 is still free before opening.

---

## Dependency graph

```text
T001 → T002 → T003 → T004 (red) → T005 (green) → { T006 [P], T007 [P]*, T008 [P] } → T009 → T010 → T011
                 └──────────────── T007 and T008 may start after T003 / T002 respectively ┘
* T007 is [P] only in its own file (ComposedHostCaptureTests.cs).
```

Fan-out is narrow because the change is one project and one folder. The only real parallelism is
T006, T007 and T008, once each owns its own file.
