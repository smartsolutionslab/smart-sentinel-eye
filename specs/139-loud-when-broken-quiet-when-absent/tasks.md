# Tasks 139 — Loud when broken, quiet when absent

**Spec:** [`spec.md`](spec.md) · **Plan:** [`plan.md`](plan.md) · **Issue:** #2188

Declared at phase 3: **behaviour-changing** → phase 4a is **red, observed failing**.
Engineer: **backend-engineer**. **No ADR is needed** — the argument, and the conditions
under which the gate should overrule it, are in spec §*Does this need an ADR?*

Everything below is **US-1**. US-2 (the preserved absent case) is delivered only as
characterisation coverage inside this slice — T005 and T006 — and files nothing.

**Parallelism (ADR-0109) is nearly nil, and that is correct here.** Three files, two of
which are in one compile unit: the test file cannot compile until
`TryLoadApplicationAssembly` is `internal` and the IVT attribute exists. The only `[P]`
markers are on the two evidence-gathering tasks that read the tree and touch nothing.

---

## Foundational — blocks everything

- [x] **T001** [P] [US-1] **Re-verify the defect is still at the cited lines before
      writing anything.** Branches merge; the spec's line numbers are from
      2026-09-13.
      ```sh
      sed -n '184,211p' src/ServiceDefaults/WolverineDefaults.cs
      sed -n '130,137p' src/ServiceDefaults/WolverineDefaults.cs
      git log --oneline -1 -- src/ServiceDefaults/WolverineDefaults.cs
      ```
      **Done when:** three bare `return null` catches are confirmed present, the `:133`
      consumer is confirmed unchanged, and the newest commit touching the file is the
      one the spec recorded. If any differs, **stop and re-plan** — do not adapt
      silently.

- [x] **T002** [P] [US-1] **Confirm no other branch has taken this file** since phase 3.
      ```sh
      git fetch origin
      git log --all --name-only --pretty=format:'%h %s' -- src/ServiceDefaults/WolverineDefaults.cs | head -20
      ```
      **Done when:** no unmerged remote branch modifies `WolverineDefaults.cs`. If one
      does, this becomes a stacked PR and the child must be retargeted to `develop`
      before the parent merges (CLAUDE.md §Stacked PRs).

- [x] **T003** [US-1] **Open the method for test.** In
      `src/ServiceDefaults/WolverineDefaults.cs`, change `private static Assembly?
      TryLoadApplicationAssembly` to `internal static`. In
      `src/ServiceDefaults/SmartSentinelEye.ServiceDefaults.csproj`, add the
      `InternalsVisibleTo` `AssemblyAttribute` item group naming
      `SmartSentinelEye.ServiceDefaults.Tests`, mirroring
      `src/MigrationRunner/SmartSentinelEye.MigrationRunner.csproj:14–20` **including a
      short comment saying why** — that file's comment is the precedent for both the
      mechanism and the justification.
      **Blocks:** T004. **Done when:** `dotnet build src/ServiceDefaults` is clean and
      the class is still `public static` (only the method widened).
      **Do not change any behaviour in this task.** It is an access-modifier change and
      nothing else; the tests written next must still be red.

---

## Phase 4a — tests first, and two of them must be seen failing

> **Agent: `test-writer`.** Writes tests only. Runs them. Returns the **verbatim**
> output. Does not touch `WolverineDefaults.cs` beyond what T003 already did.

- [x] **T004** [US-1] **Write the probe harness** in a new file
      `tests/ServiceDefaults.Tests/WolverineApplicationAssemblyTests.cs`: a
      `sealed class ResolvingProbe : IDisposable` that subscribes a handler to
      `AssemblyLoadContext.Default.Resolving` on construction and unsubscribes on
      `Dispose`.
      **Three non-negotiable properties**, each of which is a flake if missed:
      1. The probe's infrastructure assembly name is **unique per test** —
         `AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Probe{Guid.NewGuid():N}.Infrastructure"), AssemblyBuilderAccess.Run)`.
      2. The handler matches **only** that name's `.Application` sibling and returns
         `null` for every other request. `Resolving` is process-global and xUnit runs
         test classes in parallel; a handler that answers broadly corrupts other tests.
      3. Unsubscription is in a `finally`/`Dispose`, not at the end of the happy path.
      **Done when:** the file compiles. **Depends on:** T003.

- [x] **T005** [US-1] **Characterisation — the absent case (expected GREEN).** Test
      `An_absent_application_assembly_is_not_an_error`: a dynamic `Probe….Infrastructure`
      with **no** resolving handler registered; assert
      `TryLoadApplicationAssembly(probe)` returns `null` and throws nothing.
      **This test is green before and after.** It is not a phase-4a failure — it is the
      guard that FR-001 survives the fix. Say so in the PR.
      **Depends on:** T004.

- [x] **T006** [US-1] **Characterisation — outside the convention (expected GREEN).**
      Test `An_assembly_outside_the_naming_convention_is_not_probed`: pass an assembly
      whose simple name does not end in `.Infrastructure` (a dynamic
      `Probe….Something`); assert `null`, and assert the resolving handler was **never
      invoked** (a counter in the probe). FR-002.
      **Depends on:** T004.

- [x] **T007** [US-1] **RED — the corrupt image.** Test
      `A_corrupt_application_assembly_fails_the_host`. The probe's handler returns
      `Assembly.Load(new byte[] { 0x01, 0x02, 0x03, 0x04 })`, which the CLR rejects with
      a real `BadImageFormatException` that propagates out of `Assembly.Load(string)` —
      **verified on this machine at phase 3**, see plan §*How the red case is
      constructed*, probes P1 and P4.
      Assert, with Shouldly: `Should.Throw<InvalidOperationException>(...)`, its
      `Message` contains the derived `….Application` name, and its `InnerException` is a
      `BadImageFormatException`.
      **Must be observed failing.** Expected failure: *expected
      `System.InvalidOperationException` but no exception was thrown*.
      **Depends on:** T004.

- [x] **T008** [US-1] **RED — the identity mismatch.** Test
      `An_application_assembly_that_is_present_but_unloadable_fails_the_host`. The
      probe's handler returns an assembly whose identity does **not** match the request
      (`typeof(object).Assembly`); the **runtime itself** then raises
      `FileLoadException` (`0x80131509`) — verified at phase 3, probe P2. This is the
      "found but will not load" case, produced by the loader rather than thrown by the
      test.
      Assert `InvalidOperationException`, message contains the `….Application` name,
      `InnerException` is a `FileLoadException`.
      **Must be observed failing**, same expected text as T007.
      **Depends on:** T004.

- [x] **T009** [US-1] **Run the suite and capture the red verbatim.**
      ```sh
      dotnet test tests/ServiceDefaults.Tests/SmartSentinelEye.ServiceDefaults.Tests.csproj \
        --filter FullyQualifiedName~WolverineApplicationAssemblyTests
      ```
      **Done when:** T007 and T008 are `Failed` with the "no exception was thrown" text,
      T005 and T006 are `Passed`, and the **verbatim output is written into the PR-body
      draft**. This output is the engineer's brief (ADR-0144 phase 4a).
      **If T007 or T008 passes at this point, stop** — it means the method already
      throws, the phase-1 verification was wrong, and T001 needs redoing.
      **If T007 or T008 errors rather than fails** (e.g. the `Resolving` handler's
      exception did not propagate on this runtime), that contradicts the phase-3 probe:
      report the exact output and stop rather than weakening the assertion.
      **Depends on:** T005, T006, T007, T008.

---

## Phase 4b — implement

> **Agent: `backend-engineer`.** Receives T009's verbatim output as its brief. **May not
> edit the tests to pass.**

- [x] **T010** [US-1] **Split the catches.** In
      `src/ServiceDefaults/WolverineDefaults.cs`, replace the `FileLoadException` and
      `BadImageFormatException` catch blocks (currently `:203` and `:207`) with one
      filtered catch that wraps and throws:
      ```csharp
      catch (Exception exception) when (exception is FileLoadException or BadImageFormatException)
      {
          throw new InvalidOperationException(
              $"Assembly '{applicationName}' was found but could not be loaded, so every " +
              "message handler in it would go unregistered while the service reported healthy.",
              exception);
      }
      ```
      Keep `catch (FileNotFoundException) { return null; }` and add a one-line comment
      saying it is the legitimate absence.
      **One filtered catch, not two blocks** — ADR-0084 caps the method at 30 LOC and it
      is at 27 today (NFR-002).
      **`throw new`, not `throw;`** — the consequence is the part the operator cannot
      infer; `InnerException` carries the loader's own diagnosis (FR-004).
      **Done when:** FR-003 and FR-004 hold. **Depends on:** T009.

- [x] **T011** [US-1] **Correct the XML doc comment** on `TryLoadApplicationAssembly`
      (lines 177–183). *"Returns `null` if no matching assembly is loadable"* is false
      after T010. The replacement must distinguish the two outcomes: `null` for absent,
      throw for present-but-broken.
      This is **FR-005 and not cosmetic** — a comment describing the superseded contract
      is the failure mode this very file diagnoses 130 lines earlier. Do not defer it.
      **Depends on:** T010.

- [x] **T012** [US-1] **Turn the suite green without touching the tests.**
      ```sh
      dotnet test tests/ServiceDefaults.Tests/SmartSentinelEye.ServiceDefaults.Tests.csproj
      ```
      **Done when:** all four new tests pass and **no pre-existing test in that project
      regressed** — run the whole project, not the filter. T005 and T006 must pass
      **unmodified** from T005/T006; an assertion that had to be edited is evidence the
      fix over-reached into the absent case (CLAUDE.md §Refactors stay green).
      **Depends on:** T010, T011.

- [x] **T013** [US-1] **Prove nothing else broke.** Build the solution in Release (the
      analyzer gates only fire there) and run the fast suites.
      ```sh
      dotnet build -c Release
      dotnet test tests/Architecture.Tests/*.csproj
      ```
      **Stop the Aspire stack first** if one is running — it holds the service binaries
      and MSB3027 reads exactly like a broken build. **Note:** a Release analyzer error
      on a file outside the diff has been flaky here (S125); re-run the same SHA once
      before treating it as real.
      **Done when:** Release build clean, `WolverineDefaults.cs` under 300 LOC and
      `TryLoadApplicationAssembly` under 30 (NFR-001, NFR-002), Architecture.Tests green.
      **Depends on:** T012.

---

## Phase 5 — verify

- [ ] **T014** [US-1] **Observe it end to end**, per spec §*Independent end-to-end test
      procedure*: foreground `aspire run`, nine services green as the control, then
      corrupt `SmartSentinelEye.CameraCatalog.Application.dll` and confirm the resource
      refuses to start with a message naming that assembly.
      **Record where the message was readable** — dashboard console log, stderr, or
      neither. That is a finding, not a formality: the spec's §Cost section predicts the
      message may reach stderr only, and phase 5 is what settles it.
      **Preconditions, each of which has ended a run here before:** check free space on
      `C:` (the Docker vhdx lives there); confirm no second Aspire stack is running (a
      concurrent boot gives `FailedToStart` that reads exactly like this change working);
      after restoring the DLL, touch or rebuild it — a restored file keeps its old
      timestamp and MSBuild will skip the rebuild.
      **If `C:` is tight, defer with that reason written in the PR.** Do not fake it.
      **Depends on:** T013.

- [ ] **T015** [US-1] **The counterfactual.** On `origin/develop` (a read-only worktree
      or a stash — **do not switch this branch**), repeat T014's corruption step. The
      service must reach Running and report healthy with `consumers: 0` on its
      camera-catalog queues.
      This is what distinguishes "the guard works" from "the guard asserts something that
      was never in doubt". If the unpatched build also refuses to start, the test is
      measuring something else and the finding must be reported, not smoothed over.
      **Depends on:** T014. **May be skipped only if T014 was deferred**, and then the PR
      says both were deferred and why.

---

## Phase 6 — QA

- [x] **T016** [US-1] **`/code-review`** with `backend-reviewer`. Specific things to put
      in front of it, because they are the judgement calls rather than the mechanics:
      the throw-vs-log decision and whether the reviewer agrees no ADR is needed; whether
      the exception message stands alone given it may reach stderr only; whether the
      `Resolving` handler is scoped tightly enough to survive parallel test classes.
      **Not security-sensitive** — no auth, no trust boundary, no secret, no HTTP
      surface. `/security-review` is skipped; say so in the PR body.
      **Depends on:** T013.

---

## Phase 7 — PR

- [ ] **T017** [US-1] **Open the PR against `develop`.**
      ```sh
      gh pr create --base develop
      ```
      The body must carry, at minimum:
      - **T009's verbatim red output** — the only form of the phase-4a evidence a later
        reader can check.
      - **The throw-vs-log decision and its reason** — the issue asks for this
        explicitly: *"state which was chosen and why in the PR."*
      - **The no-ADR argument**, in one or two lines, linking spec §*Does this need an
        ADR?*
      - `Closes #2188` — a bare mention auto-closes roughly one time in three; use the
        keyword and check the issue state after the merge.
      - Phase 5's result or its deferral reason (T014/T015).
      Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086), rebase-merge
      only (ADR-0087), and **each commit must build on its own**.
      **Depends on:** T015, T016.

- [ ] **T018** [US-1] **Put the feature issue on Project #13** if it is not already
      there — #2188 is the feature-level issue and `tasks.md` is what the work is tracked
      against. No per-task issues (CLAUDE.md §Workflow, corrected at spec 045).
      ```sh
      gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2188
      ```
      `item-add` prints nothing on success and `item-list` defaults to 30 — verify with
      `--limit 2000` and match on `content.url`, not the number.
      **Depends on:** none; do it any time after the gate.

---

## Dependency summary

```
T001 [P] ─┐
T002 [P] ─┴─> T003 ─> T004 ─┬─> T005 ─┐
                            ├─> T006 ─┤
                            ├─> T007 ─┤
                            └─> T008 ─┴─> T009 ─> T010 ─> T011 ─> T012 ─> T013 ─┬─> T014 ─> T015 ─┐
                                                                                └─> T016 ─────────┴─> T017
T018 — independent
```

**The single foundational task is T003.** Nothing in phase 4a compiles until
`TryLoadApplicationAssembly` is `internal` and the IVT attribute exists, and T003 must
land as an access-modifier change *only* — if it alters behaviour, T007 and T008 stop
being red and the phase-4a gate is lost.
