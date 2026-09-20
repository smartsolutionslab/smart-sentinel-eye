# Tasks 187 — A pin the model resolves

**Spec:** `specs/187-a-pin-the-model-resolves/spec.md`
**Plan:** `specs/187-a-pin-the-model-resolves/plan.md`
**Issue:** [#2270](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2270)
— **already on Project #13**, status Todo, labels `bug`, `agent:ready`, no
`agent:blocked`. Confirmed by `gh issue view 2270` (`projects: Smart Sentinel
Eye (Todo)`). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED** (US1) + **characterisation green** (US2)

**US1 — RED.** Behaviour-changing: a tree the build accepts today (the
order-swapped one) stops being accepted.

**The red is produced by counterfactual, not by the tree as it stands.**
`AppHost.cs:300-303` is already correct, so a correctly written guard passes on
the first run. A guard that has never been observed failing has not been shown
to discriminate — `prove-a-guard-by-counterfactual` is this repository's named
remedy and is the technique that found #2270. The sequence is **green → red →
green**, all three captured verbatim, all three quoted in the PR body.

**US2 — CHARACTERISATION, observed green.** `ContainerImagePinTests`' three
facts are green today and must be green after with their **assertions
unmodified**. An assertion that has to be edited is evidence the scan's verdict
moved; **block, do not adjust**. Pre-declared exemption: the method *names*
change, and message text quoting a changed method name changes with them. The
`ShouldBeEmpty` / `ShouldBe` / `ShouldBeGreaterThan` calls and their subjects do
not.

### Roles

| Phase | Agent |
|---|---|
| 4a | **test-writer** |
| 4b | **infra-engineer** (not backend-engineer — no bounded context, no domain, no persistence) |
| 6 | **infra-reviewer** only (backend-reviewer and security-reviewer skipped; reasons in `plan.md` §4) |

### Blocked outcomes, restated so they are not rediscovered

- Deleting, narrowing or disabling `ContainerImagePinTests`.
- Adding a resource-name exclusion list to the new guard.
- Widening what counts as floating (that would red-line `rabbitmq:4-management-alpine`
  on day one — see spec §9, and file it separately).
- Changing any tag, any image, or any executable line of `AppHost.cs`.

---

## Foundational — blocks everything

Nothing in this feature is foundational in the ADR-0109 sense: no
`Shared.Kernel`, no `Shared.Contracts`, no AppHost resource, no Aspire wiring.
The three files are independent of one another except where §Dependencies says
otherwise.

---

## US1 — A floating tag cannot reach the composed model (P1)

**Ships alone and closes the issue.**

| ID | P | Story | Task |
|---|---|---|---|
| **T001** | | US1 | **Capture the baseline green.** Run `ContainerImagePinTests` (3 facts) and `AppHostMediaMtxImageTests` (2 facts) on the unmodified branch; save verbatim output. This is US2's characterisation baseline **and** the proof that the tree is correct before anything is added. |
| **T002** | | US1 | **Write `tests/Integration.Tests/AppHostContainerImagePinTests.cs`** — `[Trait("Category", "FixtureLogic")]`; two argument arrays copied from `AppHostMediaMtxImageTests` with a comment saying the duplication is chosen (ADR-0036); model composed via `DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)`, **no `StartAsync`**. Population and the pinned/floating definition per `plan.md` §2.2–2.3 — the definition is `ContainerImagePinTests.IsFloating`'s, reproduced exactly, plus "absent or empty tag is floating". |
| **T003** | | US1 | **Write the four facts** (`plan.md` §2.4): F1 run-mode, F2 fixture shape, F3 the population control naming `fixture-video` and `camera-sim`, F4 the `DockerfileBuildAnnotation` exclusion. Floors are `ShouldBeGreaterThanOrEqualTo` (7 run-mode, 6 fixture), never equality. Failure messages pasteable into `docker pull`, `Registry` included when set. |
| **T004** | | US1 | **Observe green on the unmodified tree.** All four facts pass. Capture verbatim. A fact failing here means the plan's measurement was wrong — **STOP and report**, do not adjust the guard. |
| **T005** | | US1 | **Counterfactual, step 1 — the red.** Swap `AppHost.cs:301`/`:302` so `WithImage("minio/minio")` follows `WithImageTag(...)`. Run the new class: **F1 and F2 must FAIL**, naming `minio` and a reference ending `:latest`. Capture verbatim. If they pass, **STOP and report** (`plan.md` §5). |
| **T006** | | US1 | **Counterfactual, step 2 — the contrast.** With the same broken tree, run `ContainerImagePinTests`: **it must PASS**. Capture verbatim. This is #2270's claim re-proven on this tree and the one-line justification for US2. If it fails, **STOP and report**. |
| **T007** | | US1 | **Revert and re-prove green.** `git checkout src/AppHost/AppHost.cs`; force a rebuild before re-running (`restoring-a-file-keeps-its-old-timestamp` — a restored file keeps its mtime and MSBuild skips it); all four facts green again. Confirm `git diff src/AppHost/AppHost.cs` is empty. |

**US1 exit:** the new class is green on the real tree, was observed red on the
counterfactual, and `AppHost.cs` is byte-identical to `develop`.

---

## US2 — The source guard says what it checks (P2)

| ID | P | Story | Task |
|---|---|---|---|
| **T008** | | US2 | **Rename one fact** in `tests/Architecture.Tests/ContainerImagePinTests.cs`: `No_container_image_in_the_app_host_runs_a_floating_tag` → `Every_literal_image_tag_written_in_the_app_host_is_pinned`. The other two facts and the **class name** are unchanged (`DatabaseCommandLogLevelTests.cs:44` holds a `<see cref>`; `docs/adr/0147` names it in prose). Assertions byte-identical. |
| **T009** | | US2 | **Document the scan's two limits** in the class doc (`plan.md` §3.2): literals only, so package-supplied coordinates are invisible; no notion of call order, so a `WithImage` after a `WithImageTag` passes (#2270). Point at `AppHostContainerImagePinTests` as the authority for the composed claim, and state what this scan remains the authority for — the written references in `scripts/` and in prose, which no application model contains. |
| **T010** | [P] | US2 | **Correct `src/AppHost/AppHost.cs:293`** — the paragraph explains the `WithImage`-before-`WithImageTag` order and names the guard that cannot check it. Name `AppHostContainerImagePinTests` as the enforcement. Comment text only. |
| **T011** | [P] | US2 | **Extend `src/AppHost/AppHost.cs:159`** — *"ContainerImagePinTests fails the build when any of this drifts apart"* stays true of the three MediaMTX literals; add the model guard as what makes the *resolved* claim. Comment text only. |
| **T012** | | US2 | **Re-run the characterisation baseline.** All three `ContainerImagePinTests` facts green, assertions unchanged — prove it with `git diff` on the file, not by assertion. Any edited assertion: **block and report**. |

T010 and T011 are `[P]`: two comment blocks 130 lines apart in one file, both
pure text, neither touching a statement. They are marked parallel-safe for
*reasoning*, not for two agents editing `AppHost.cs` at once — one agent, one
index (ADR-0109's disjoint-file rule is about ownership, and this file has one
owner in this feature).

---

## Phase 5 — verification (separate agent, `/verify`)

| ID | P | Story | Task |
|---|---|---|---|
| **T013** | | — | **Run spec §5's independent end-to-end procedure verbatim**, including step 4 (the old guard passing on the broken tree). Record every command and its real output in `specs/187-a-pin-the-model-resolves/verification.md`. Write the results down — a measurement reported only to the orchestrator is invisible to every later grep. |
| **T014** | | — | **Record the cost.** Wall-clock of the new class, and of the whole `Category=FixtureLogic` step before and after. Two compositions are added to a step that already does two; if the figure is surprising, report it rather than absorbing it. |
| **T015** | | — | **Confirm the CI placement is real, not designed.** After the PR opens, read the backend job's log and confirm the four new facts appear in the `Docker-free fixture logic tests` step's output. A trait asserted in source is `guards-that-read-the-design-artefact` again if nobody ever reads the job that consumes it. |

---

## Dependencies

```
T001 ──> T002 ──> T003 ──> T004 ──> T005 ──> T006 ──> T007
                                                       │
                              T008 ──> T009 ──┐        │
                                              ├──> T012 ──> T013 ──> T014 ──> T015
                              T010 [P] ───────┤
                              T011 [P] ───────┘
```

- **T001 before everything.** It is the characterisation baseline; captured
  after an edit, it is worthless.
- **T004 before T005.** Green-then-red; a guard first seen red might be red for
  a reason unrelated to the swap.
- **T007 before any US2 task.** `AppHost.cs` must be back to `develop`'s content
  before T010/T011 touch its comments, or the revert would undo them.
- **T008/T009 before T010/T011** in spelling only: the comments name the new
  class, so its final name must be settled (it is, in T002).

## Parallelism summary

Small feature, little to fan out. `[P]` is marked only on T010/T011. US1 and US2
are **not** parallel: US2's documentation asserts what US1's counterfactual
proves, and writing it first would be writing a claim before its evidence — the
defect this whole spec exists to remove.

## Definition of done

1. Four new facts green on the tree; the verbatim red from the counterfactual
   and the verbatim pass of the old guard on the same broken tree both quoted in
   the PR body.
2. `ContainerImagePinTests` green, assertions byte-identical (`git diff` shown).
3. `git diff src/AppHost/AppHost.cs` contains comment lines only.
4. No CI workflow file changed.
5. `verification.md` written, with real output.
6. Follow-up recommended in the PR body: floating-within-major
   (`4-management-alpine`) is still unguarded — spec §9.
