# Tasks 185 — A filter that cannot select nothing

**Spec:** `specs/185-a-filter-that-cannot-select-nothing/spec.md`
**Plan:** `specs/185-a-filter-that-cannot-select-nothing/plan.md`
**Issue:** [#2289](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2289)
— **already on Project #13**, labels `bug`, `ci`, `agent:ready`, no
`agent:blocked`. Verified 2026-09-18 by `content.url` against a `--limit 2000`
dump (the number filter returns zero). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED**

Behaviour-changing on both halves, and not ambiguous — ambiguity would resolve
to red anyway.

- **US1** changes what a CI step does: a step that exits 0 having run nothing
  starts exiting 1. That is new behaviour.
- **US2** changes what the guard reads: a category set typed into a C# literal
  becomes a set derived from `ci.yml`, and the guard acquires two verdicts it
  cannot reach today.

**The red is asymmetric, and the asymmetry is the acceptance test for the red
itself.** Run before any `ci.yml` edit:

- **`The_workflow_reader_finds_every_dotnet_test_invocation` (F2) must PASS.**
  It is the positive control: it proves the reader actually opens `ci.yml` and
  parses its continuation lines. Its failure message should report **2** filtered
  invocations.
- **`Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing`
  (F1) must FAIL**, listing **both** invocations by their real line numbers
  (72 and 284 on `d7674d12`) as lacking
  `RunConfiguration.TreatNoTestsAsError=true`.

**If F2 fails, STOP and report.** The reader is not reaching `ci.yml` at all, no
workflow edit will fix that, and a red produced by a mis-wired reader is a
failure that looks like the filed defect and is not it.

**If F1 passes, STOP and report.** Something already supplies the flag, which
contradicts spec §1.2, and the premise needs re-checking before anything is
changed.

**If F1 fails listing zero or one invocation, STOP and report.** F1 over an
empty set is the exact defect this spec exists to close, reproduced inside its
own fix.

### The characterisation half, declared alongside the red

The **eight** pre-existing facts in `IntegrationTestSelectionTests` (listed in
`plan.md` §3.4) are green today and must be green after, with their
**assertions** unmodified. An assertion that has to be edited is evidence the
guard's verdict on the real tree moved — which this change claims it does not.
**Block, do not adjust.**

The single exemption, pre-declared so it is not mistaken for drift: assertion
**message** text quoting `CheapStep` / `ExcludeStep` is updated when those
constants are deleted. The `ShouldBeTrue` / `ShouldBeFalse` / `ShouldBeEmpty`
calls and their subjects do not change.

### How the CI-workflow half is verified, given it cannot be unit-tested

A workflow YAML file's runtime behaviour cannot be exercised from a test. This
spec does not pretend otherwise; it verifies that half **twice, by two
different mechanisms**, because either alone would be a half-truth:

1. **F1 asserts the workflow's *content*** — the flag is present on every
   filtered invocation. This is the existing repo pattern: spec 181's
   `AppHostStackStatusTests` validated `ci.yml`'s content by a no-boot model
   read, and `AgentBriefClaimTests` / `AppHostE2ESwitchTests` do the same for
   other claims. It makes the flag un-droppable.
2. **Phase 5 runs the real command and reads the real exit code** — the
   counterfactual in `spec.md` §6. F1 alone would prove only that the design was
   written down, not that it holds; a guard that reads an artefact must be paired
   with asking the running system once, and this is that once.

Both are required. The verification note quotes **four** exit codes: wrong
filter without the flag (`0`, the defect), wrong filter with it (`1`, the fix),
right filter with it (`0` plus a non-zero total, the positive control), and the
`Architecture.Tests` run with a flag deleted from a scratch `ci.yml` (red).

### Phase 4 agents

- **4a — `test-writer`.** Writes the workflow reader and the four new facts in
  `IntegrationTestSelectionTests.cs`, runs them against the **unedited**
  `ci.yml`, observes the asymmetric red above, and returns the **verbatim**
  output. That output is `infra-engineer`'s brief and is quoted in the PR body
  (ADR-0139).

  *Why the reader is the test-writer's, not the engineer's:* the reader is the
  apparatus that makes the red observable, exactly as spec 184's probe corpus
  was. Handing it to the engineer would mean the engineer authors the
  specification of its own fix.

- **4b — `infra-engineer`.** Adds the two `-- RunConfiguration.TreatNoTestsAsError=true`
  lines plus their comments to `.github/workflows/ci.yml`, then completes the
  guard's behaviour change in the same file `test-writer` touched: delete
  `CheapStep` and `ExcludeStep`, build `CategoryDeclaration` from the derived
  set, and re-word the citations in `Explain(...)` to report freshly-read line
  numbers.

  **May not edit the four new facts to pass.** They are the specification.

  *Why `infra-engineer` and not `backend-engineer`:* the centre of gravity is a
  CI gate's semantics — `infra-engineer`'s brief names CI workflows explicitly,
  and the one file it shares with the C# roster contains regex and text parsing
  in `Architecture.Tests`, not domain, persistence or messaging code. Splitting a
  two-file change across two implementers would cost a handoff and an extra red
  cycle for no gain. The C# is not left unexamined: `backend-reviewer` is on the
  phase-6 list precisely for it.

### Phase 6 reviewers: **`infra-reviewer` + `backend-reviewer`. `security-reviewer` is NOT warranted.**

- **`infra-reviewer` (primary).** Its brief is literally "whether a green CI run
  actually proves anything", which is this feature's whole subject. It owns the
  `ci.yml` diff: flag placement after `--`, the continuation-line escaping, and
  that no job name, `needs:`, `timeout-minutes` or `continue-on-error` moved
  (`AgentBriefClaimTests` asserts the briefs against those).

- **`backend-reviewer` (secondary).** Owns the C#: `Regex.Escape` on
  externally-sourced names, regex timeouts in line with the existing
  `AgentBriefClaimTests` usage, `Ensure.That` where a guard is warranted
  (ADR-0105), collection expressions with explicit types, sentence-style naming
  (ADR-0053), and — the one that matters most here — whether **any new assertion
  can pass over an empty set**.

- **`security-reviewer`: no.** Checked against this issue's specific shape rather
  than carried over from spec 184. The discriminator for a workflow change is
  whether it alters `permissions:`, adds or re-pins an action, introduces a
  secret or a `pull_request_target` trigger, or widens what runs on untrusted
  input. **This change does none of those** (`plan.md` §2.3): it adds one
  run-setting token to two existing `dotnet test` commands, and the C# half reads
  two files already in the repository. There is no token, scope, fab boundary,
  idempotency key or trust boundary anywhere in the diff. If review finds the
  `ci.yml` diff has grown a `permissions:` or action change, that conclusion is
  void and `security-reviewer` must be added.

### Files this feature may touch

- `.github/workflows/ci.yml`
- `tests/Architecture.Tests/IntegrationTestSelectionTests.cs`
- `specs/185-a-filter-that-cannot-select-nothing/**`

### Files this feature may NOT touch

- **`tests/Integration.Tests/**`** — the trait assignments are correct. If the
  guard goes red against a real class, that is a finding to report, not a trait
  to add.
- **`scripts/coverage-check.ps1`** — runs every project unfiltered; no exposure.
- **`.github/workflows/*` other than `ci.yml`.**
- **Anything under `src/`.** No production assembly changes; if one is proposed,
  the scope has been misread.
- **`tests/Architecture.Tests/AgentBriefClaimTests.cs`** — it parses the same
  file for a different claim. Tempting to extract a shared reader; do not. Two
  small readers with different jobs are cheaper than a shared abstraction with
  two callers (ADR-0036, no speculative generality), and touching it would put
  this PR in contention with any brief edit.

### Latency budget

**N/A.** No production assembly changes; none of the six legs of
`event arrival → overlay rendered` is touched and no leg's measurement status
moves (`spec.md` header).

---

## Task list

`[ID] [P?] [Story]` — `[P]` marks tasks that own disjoint files and may run
concurrently (ADR-0109). **There is almost no parallelism here by design:** the
red must be observed before the workflow is edited, so US1's fix cannot precede
US2's reader even though they live in different files.

### Phase 4a — the red (`test-writer`)

- **T001 [US1+US2]** Add the workflow reader to
  `tests/Architecture.Tests/IntegrationTestSelectionTests.cs`: `TestInvocation`,
  the continuation-line scan, `Filter(...)`, `Categories(...)`, and the
  `--`-separator check (`plan.md` §3.1). Reader only — do not yet touch
  `CategoryDeclaration`, `CheapStep` or `ExcludeStep`.
- **T002 [US2]** Add **F2**
  (`The_workflow_reader_finds_every_dotnet_test_invocation`): parity against a
  raw count of `dotnet test` lines, plus at least one filtered invocation. No
  frozen numbers. *Depends on T001.*
- **T003 [US1]** Add **F1**
  (`Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing`),
  with a message naming each offending invocation's line number, its filter, and
  the exact text to add. *Depends on T001.*
- **T004 [US2]** Add **F3**
  (`Every_category_the_workflow_filters_on_is_declared_by_a_test_class`).
  *Depends on T001.*
- **T005 [US2]** Add **F4** — the synthetic-input facts for the reader
  (`plan.md` §3.3): continuation-line joining, the flag without a preceding
  standalone `--` not counting, an unfiltered invocation not being asked for the
  flag, and `Category!=X&Category!=Y` yielding both names. Feed literal workflow
  text, mirroring how the existing facts feed `Describe(path, source)`.
  *Depends on T001.*
- **T006 [US1+US2]** Run the class against the **unedited** `ci.yml`. Confirm
  the asymmetry: F2 green, F1 red naming two invocations, F3/F4 green. Apply the
  three STOP conditions above. Return the **verbatim** output.
  *Depends on T002–T005.*

### Phase 4b — the fix (`infra-engineer`)

- **T007 [US1]** Add `-- RunConfiguration.TreatNoTestsAsError=true` as the final
  continuation line of the Docker-free step (`ci.yml:72`), with the reasoning
  comment from `plan.md` §2.2.
- **T008 [US1]** The same on the integration step (`ci.yml:284`), with a
  one-line back-reference and the exclusion-filter caveat from `spec.md` §1.5.
  *May run with T007 — same file, so not `[P]`.*
- **T009 [US2]** Delete `CheapStep` and `ExcludeStep`; build
  `CategoryDeclaration` from the derived set with `Regex.Escape` on every name;
  re-word `Explain(...)` and fact 6's message to cite freshly-read line numbers.
  *Depends on T006. Same file as T001–T005 — not `[P]`.*
- **T010 [US1+US2]** Re-run the class. **All twelve facts green** (eight
  pre-existing with unmodified assertions, four new). Confirm no assertion in
  the eight was edited. *Depends on T007–T009.*

### Phase 5 — verify (`/verify`)

- **T011 [US1]** Build `Integration.Tests` in Release, then run the four
  counterfactuals from `spec.md` §6 and record every exit code **read without a
  pipe**. Write `specs/185-…/verification.md` with the verbatim transcripts.
  *Depends on T010.*
- **T012 [US2]** On a scratch copy of `ci.yml`, (a) misspell the cheap filter and
  confirm F3 goes red, (b) delete the flag from one step and confirm F1 goes red
  naming that step's current line number. Restore. Record both.
  *Depends on T010. `[P]` with T011 — different commands, no shared file, and
  T012 needs no Release build.*

### Phase 6 — review

- **T013 [P]** `infra-reviewer` on the `ci.yml` diff.
- **T014 [P]** `backend-reviewer` on the `IntegrationTestSelectionTests.cs` diff.
  *`[P]` with T013 — both read-only, disjoint concerns.*
- **T015** Address or accept in writing every finding. *Depends on T013, T014.*

### Phase 7 — PR

- **T016** `gh pr create --base develop`. The PR body quotes T006's **verbatim
  red** (ADR-0139), the four exit codes from T011, and both counterfactuals from
  T012. Closing keyword for #2289, and the issue's state re-checked after the
  merge. Commits follow ADR-0030, no `Co-Authored-By` (ADR-0086).

---

## Dependency summary

```
T001 ──┬─ T002 ─┐
       ├─ T003 ─┤
       ├─ T004 ─┼─ T006 ─┬─ T009 ─┐
       └─ T005 ─┘        │        ├─ T010 ─┬─ T011 ─┐
                  T007 ──┼────────┤        └─ T012 ─┤ [P]
                  T008 ──┘        │                 │
                                                    ├─ T013 [P] ─┐
                                                    └─ T014 [P] ─┴─ T015 ─ T016
```

**Foundational:** T001. Everything else in 4a depends on it, and 4b's T009
depends on 4a completing. T007/T008 are independent of the reader in principle,
but must not land before T006 has observed the red — otherwise F1 is green on
arrival, which ADR-0144 calls a phase-4 failure, not a shortcut.

**The only genuine parallelism** is T011 ‖ T012 and T013 ‖ T014. Two files is
not enough surface for fan-out, and saying so is more useful than decorating
sequential tasks with `[P]`.
