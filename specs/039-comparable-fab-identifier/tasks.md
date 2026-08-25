# Tasks: A fab identifier can be sorted, in every context that has one

**Feature**: `039-comparable-fab-identifier` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1849 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**24 tasks across five phases.** Eight nearly-identical edits that look like one
task and are not.

**Why it is not one task**: eight Domain assemblies, each with its own **≥ 90%
coverage gate**; one context with no test file to extend; a convention test whose
discovery mechanism was a real decision; and three deliberate breaks, because
this feature's whole value is assertions that fire.

**Nothing to add**: no new dependency, no migration, no ADR, and **no production
behaviour change**. The deployed listing already sorts correctly — this makes the
same behaviour reachable from a test.

**Coverage is the most likely way this PR goes red**, and it is not
hypothetical. `Identity.Domain` measures **91.7%** against a **≥ 90%** gate over
~250 non-blank lines; five uncovered members take it to roughly **89.8%**. That
is why Phase 2 exists and why it is eight tasks rather than one.

**`AuditObservability` is the trap.** It is the **one** context with no
`FabIdentifierTests.cs`, and the **one** whose record body has already drifted.
Seven tasks are *edit a file*; one is *create one*.

---

## Do not

- **Do not fix the `nameof` drift.** `AuditObservability`'s guard reads
  `Ensure.That(value)` where the other seven read `Ensure.That(value, nameof(value))`.
  One word, in a file this feature already edits, which is exactly what makes it
  tempting — and it would turn eight identical edits into seven and one, in a diff
  whose reviewability rests on their being identical. **Raise it in the PR.**
- **Do not change the tie-break** in `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs`
  (**FR-010**). Removing it is the tempting way to make a failing test pass, and
  it trades a test-only exception for a real paging defect.
- **Do not change `From`, `IsValid`, `MinimumLength`, `MaximumLength` or any
  existing doc comment** in the eight. This feature adds; it does not tidy.
- **Do not change the three `.Value` + `StringComparer.Ordinal` call sites** —
  `GetOverlaySnapshotQueryHandler` and `ListVariablesQueryHandler`'s two. They are
  explicit, correct, and outside a translated query.

---

## Phase 1: The comparison

**Goal**: Eight identical edits. Verbatim from
[contracts/fab-ordering.md](./contracts/fab-ordering.md).

Each task adds `IComparable<FabIdentifier>` to the record declaration, a
`CompareTo` comparing **`Value`** ordinally and returning `1` against `null`, and
the four comparison operators via `Comparer<FabIdentifier>.Default` — placed after
the private constructor and before `From`, matching `CameraName`'s layout.

**Compare `Value`, not a normalised form.** `CameraName` normalises because it
preserves display casing and its `Equals` compares the normalised form. A fab
identifier's grammar admits one spelling, so a normalisation step here would be a
rule with no input that exercises it. The code must carry that reason, or the
difference from `CameraName` reads as an oversight.

- [x] T001 [P] [US1] `src/CameraCatalog/Domain/Camera/FabIdentifier.cs` — the one context with a live caller
- [x] T002 [P] [US1] `src/Identity/Domain/RegisteredClient/FabIdentifier.cs`
- [x] T003 [P] [US1] `src/EventIngestion/Domain/Event/FabIdentifier.cs`
- [x] T004 [P] [US1] `src/Automation/Domain/Rule/FabIdentifier.cs`
- [x] T005 [P] [US1] `src/SystemVariables/Domain/Variable/FabIdentifier.cs`
- [x] T006 [P] [US1] `src/LayoutComposition/Domain/Layout/FabIdentifier.cs`
- [x] T007 [P] [US1] `src/StreamDistribution/Domain/Stream/FabIdentifier.cs`
- [x] T008 [P] [US1] `src/AuditObservability/Domain/AuditEvent/FabIdentifier.cs` — **the drifted copy**. Add the comparison exactly as the other seven, and leave its `Ensure.That(value)` alone

**Checkpoint**: eight files changed, nothing else under `src/`.

---

## Phase 2: The coverage that keeps eight gates green

**Goal**: Every comparison exercised, in its own context.

**Not optional and not padding.** An untested `CompareTo` in seven contexts is
the "seven gain something nothing exercises" objection made real, and Identity's
gate would fail outright.

Each task asserts three things, and the second and third are the ones that catch
a wrong implementation:

1. two different fabs order the way ordinal says;
2. **two equal fabs compare `0`** — without it, `return 1` always passes an
   ordering test;
3. **comparing against `null` returns a positive number** — the case implementers
   forget, that no ordinary sort reaches.

- [x] T009 [P] [US1] Extend `tests/CameraCatalog.Domain.Tests/Camera/FabIdentifierTests.cs`
- [x] T010 [P] [US1] Extend `tests/Identity.Domain.Tests/RegisteredClient/FabIdentifierTests.cs` — **the tightest gate**: 91.7% before this change
- [x] T011 [P] [US1] Extend `tests/EventIngestion.Domain.Tests/Event/FabIdentifierTests.cs`
- [x] T012 [P] [US1] Extend `tests/Automation.Domain.Tests/Rule/FabIdentifierTests.cs`
- [x] T013 [P] [US1] Extend `tests/SystemVariables.Domain.Tests/Variable/FabIdentifierTests.cs`
- [x] T014 [P] [US1] Extend `tests/LayoutComposition.Domain.Tests/Layout/FabIdentifierTests.cs`
- [x] T015 [P] [US1] Extend `tests/StreamDistribution.Domain.Tests/Stream/FabIdentifierTests.cs`
- [x] T016 [P] [US1] **Create** `tests/AuditObservability.Domain.Tests/AuditEvent/FabIdentifierTests.cs`. **This file does not exist.** The only context without one, and the same one whose value object has drifted — two independent signs it was added apart from the others. Mirror a sibling's structure rather than inventing one

**Checkpoint**: eight comparisons covered; eight gates still green.

---

## Phase 3: The guard

**Goal**: A ninth context cannot forget, and neither can an edit to one of the
eight.

- [x] T017 [US2] Create `tests/Architecture.Tests/FabOrderingConventionTests.cs`, reading source via the repository-root walk `tests/Architecture.Tests/StaleCodeConventionTests.cs` already uses — up from `AppContext.BaseDirectory` to `SmartSentinelEye.slnx`, then `src/**/*.cs` excluding `obj/` and `bin/`. **Reads source rather than reflecting** even though all eight Domain projects are referenced: a ninth context added *without* a reference is invisible to reflection, and the test exists for the ninth context
- [x] T018 [US2] Assert the **record declaration** names `IComparable<FabIdentifier>`, in `tests/Architecture.Tests/FabOrderingConventionTests.cs`. Match the declaration line, **not the bare word** — otherwise a mention in a doc comment satisfies the guard
- [x] T019 [US2] Assert each file names `StringComparison.Ordinal`, in `tests/Architecture.Tests/FabOrderingConventionTests.cs`. **This is the structural replacement for a behavioural assertion that could not be written**: the spec asked for a pair whose ordinal and culture-sensitive orderings disagree, and under this grammar no such pair exists on this platform (research §5). Do not "improve" it back into a behavioural test — and note it is *stronger*, holding for every input rather than one. It is also why this test reads source: a `StringComparison` argument has no assembly-level artefact
- [x] T020 [US2] Assert the scan **found at least one file**, in `tests/Architecture.Tests/FabOrderingConventionTests.cs`. A source scan that silently matches nothing passes forever, which is the standard failure mode of this kind of test
- [x] T021 [US2] Make every failure **name the offending file and say what breaks**, in `tests/Architecture.Tests/FabOrderingConventionTests.cs` (**FR-008**). The runtime failure this prevents — `At least one object must implement IComparable`, from inside LINQ — names neither the sort field nor the query, which is why it cost half an hour. A guard that fails with a bare assertion hands the next reader the same problem in a new place

**Checkpoint**: the convention is enforced, not merely followed.

---

## Phase 4: The behaviour that motivated it

**Goal**: The test that could not be written, written.

- [x] T022 [US1] Add the tying test to `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs`: two cameras that **tie on the primary sort key** and differ by fab, asserted to come back **in fab order**, on **both** tie-breaking sort paths (`name` and `registeredAt` — separate expressions, so one test exercises one of them). **Assert the order, not the absence of an exception**: a `CompareTo` returning `0` for everything also stops the throw, while leaving exactly the paging defect the tie-break exists to prevent
- [x] T023 [US3] Delete the workaround comment at `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs` lines 239-241 — *"A distinct instant, not cosmetic…"* inside `The_default_listing_omits_retired_cameras` — and confirm that test still passes. **The trap it warns about is gone, and a warning that outlives its hazard costs a reader time and teaches them something false.** There is exactly **one** such comment; the issue said two

**Checkpoint**: SC-001 met — the tying test passes with no workaround.

---

## Phase 5: Evidence

- [x] T024 **Three deliberate breaks, then full verification.** An assertion that has never failed is a claim, not a check.
  - **(a) The interface.** Remove `IComparable<FabIdentifier>` from one copy, run `tests/Architecture.Tests`, record **which file the failure names** and what it says, revert. This checks T021's message as well as T018's assertion.
  - **(b) The ordinality — the one that matters most.** Change one copy's `StringComparison.Ordinal` to `StringComparison.InvariantCulture`, run the guard, record the failure, revert. This is the assertion that *replaced* one the spec asked for and Phase 0 found unwritable, so it carries more weight than usual: if it does not fire, ordinality is unguarded.
  - **(c) The order, not the throw.** Make one `CompareTo` return `0` unconditionally, run `tests/CameraCatalog.Application.Tests`, and record that **T022 fails** — it would still not throw, which is precisely the wrong fix this guards against. Revert.
  - Then: `dotnet build -c Release` clean with analyzers; the affected test projects; **all eight Domain coverage figures reported individually**, not merely "the gate passed" — `scripts/coverage-check.ps1` needs PowerShell 7, so replicate it if unavailable (every non-integration test project with coverage, merged through `reportgenerator` with the gate's assembly filter).
  - **`git diff origin/develop -- src/` must show only the eight `FabIdentifier.cs` files.** Anything else under `src/` is a **finding to raise, not a change to keep**.
  - Verification note on the PR per [quickstart.md](./quickstart.md), including the `AuditObservability` drift raised and left alone.

---

## Dependencies

```
T001 … T008  (the eight comparisons)
   │
   ├──▶ T009 … T016   (the eight coverage tests)
   │
   ├──▶ T022 ─▶ T023  (the tying test, then the comment)
   │
   └──▶ T024(a)(b)(c) (the breaks need something to break)

T017 ─▶ T018, T019, T020, T021   (the guard — written against the
                                  requirement, so it does NOT depend
                                  on Phase 1 to be authored)
   │
   └──▶ T024(a)(b)
```

**Phase 1 blocks Phase 4** — the tying test needs the comparison to exist.

**Phase 1 does not block Phase 3.** The guard is written against the requirement,
not against the code, so it can be authored first. It will fail until Phase 1
lands, which is the correct order if you want to watch it work.

**T023 needs T022** only in the sense that deleting the warning before writing the
test it warned about leaves a window where neither exists.

---

## Parallel opportunities

- **T001–T008.** Eight files, no shared state, no ordering between them. The most
  parallel work in the feature — and the reason to do them as eight tasks is that
  a single "add it to all eight" task is one an implementer can finish having done
  seven.
- **T009–T016.** Eight more files, likewise. Note **T016 creates** rather than
  extends.
- **Phase 3 as a whole** runs alongside Phases 1–2; only its deliberate breaks
  need Phase 1 finished.
- **T022** is independent of Phase 2 — it exercises `CameraCatalog`'s comparison
  through the handler rather than directly.

---

## Implementation strategy

**MVP is T001 + T022.** One comparison and the test that could not be written:
the defect is gone and SC-001 is met. Everything else extends it to the other
seven, guards it, and proves the assertions fire.

**Do all eight before running coverage.** Doing one context at a time and
checking the gate after each is slower and tells you nothing extra — the gate is
per-assembly and the edits do not interact.

**Write the guard early and watch it fail.** T017–T021 before or alongside Phase
1 means the guard's first run is a real failure naming eight files, which is a
better check of T021's message than the deliberate break will be.

**Do T023 last.** Deleting the warning before the test it warns about exists
leaves the trap unmarked and unguarded, briefly.

---

## Three things most likely to go wrong

1. **A Domain coverage gate fails.** Not hypothetical: Identity has ~2% of
   headroom over ~250 lines and five uncovered members spend more than that. This
   is the most likely way the PR goes red, and it is why Phase 2 is eight tasks
   rather than a note. Mitigated by covering the comparison everywhere and by
   T024 reporting all eight figures individually — a run that says only "the gate
   passed" hides which context is now at 90.1%.

2. **`AuditObservability` is half-done.** It is the one context where Phase 2 is
   *create a file* rather than *edit one*, and the one whose value object has
   already drifted. The easy failure is adding the comparison (T008) and not the
   test (T016), which the convention test will not catch — it checks the
   interface, not the coverage. The gate would catch it, eventually, as a number.

3. **The comparison is copied from `CameraName` including its normalisation.** It
   would pass every test in this feature, because the fab grammar admits one
   spelling — a normalisation step with no input that exercises it, which is
   exactly the code nobody can justify a year later. Mitigated by the contract
   giving the comparison verbatim and by the code carrying the reason the two
   types differ.
