# Verification 185 — A filter that cannot select nothing (#2289)

**Latency: N/A.** CI-workflow and test-infrastructure change; no production
assembly changes, no leg of constitution §IV's event→overlay path moved.

## Phase 4a — red (ADR-0139), independently reproduced

Quoted from commit `f0124e06`, run against the unedited `ci.yml`:

```
[FAIL]  Every_filtered_test_step_in_the_workflow_fails_when_it_selects_nothing
  Shouldly.ShouldAssertException : missing
    should be empty but had
2
    items and was
[TestInvocation { Line = 72, ... Filter = Category=FixtureLogic, ... FailsOnNoTests = False },
 TestInvocation { Line = 284, ... Filter = Category!=Measurement&Category!=Disruptive&Category!=Maintenance, ... FailsOnNoTests = False }]

Additional Info:
    2 filtered `dotnet test` invocation(s) in .github/workflows/ci.yml can select zero tests and still
    exit 0, because none carries `-- RunConfiguration.TreatNoTestsAsError=true` after a standalone `--`

  .github/workflows/ci.yml:72 — --filter "Category=FixtureLogic" — add `-- RunConfiguration.TreatNoTestsAsError=true`
  .github/workflows/ci.yml:284 — --filter "Category!=Measurement&Category!=Disruptive&Category!=Maintenance" — add ...

Total tests: 18
     Passed: 17
     Failed: 1
```

Asymmetric as required: the positive control (F2, proving the reader genuinely
finds and parses `ci.yml`'s invocations) passed; the real defect (F1) failed,
naming **exactly** the two real filtered invocations — not zero (which would
mean a mis-wired reader), not one (which would mean an incomplete fix). The
eight pre-existing facts stayed green throughout.

## Phase 5 — the real-project counterfactual, performed directly by the orchestrator

Spec.md §6 asks for three checks. Both reviewers performed the equivalent of
step 3 (the guard's own counterfactual) and step 1/2's *mechanism* against a
stand-in project (`Shared.Kernel.Tests`) — real evidence, but not against the
actual project these two `ci.yml` steps run. Performed here against the real
`tests/Integration.Tests` project, exit codes read **without a pipe** (the
exact mistake this issue is about):

**1. The zero-match counterfactual, with the fix:**
```
$ dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
    -c Release --no-build --filter "Category=FixtureLogick" \
    --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min \
    -- RunConfiguration.TreatNoTestsAsError=true
No test matches the given testcase filter `Category=FixtureLogick` in ...SmartSentinelEye.Integration.Tests.dll
exit=1
```

**Without the fix (the defect, reproduced against the real project this PR protects):**
```
$ dotnet test ... --filter "Category=FixtureLogick" --blame-hang ... (no RunConfiguration flag)
No test matches the given testcase filter `Category=FixtureLogick` in ...SmartSentinelEye.Integration.Tests.dll
exit=0
```

**2. The positive control** — same command, correct filter, with the flag:
```
Passed!  - Failed: 0, Passed: 145, Skipped: 0, Total: 145, Duration: 4 s
exit=0
```
The flag does not interfere with a legitimate matching run, and 145 is the
observed answer to "did anything run" for the FixtureLogic category today.

**3. The guard's counterfactual** — performed by `infra-reviewer` on an
isolated scratch copy of the tree (not the working copy, per spec.md §6's own
instruction not to touch it): baseline 18 facts passed; flag deleted from the
cheap step → 1 failed, naming the freshly-read current line number (not a
frozen one); `Category=FixtureLogic` misspelled to `FixtureLogick` in the
workflow only → 4 failed, F3 correctly naming the undeclared category; both
`--filter` lines deleted entirely → 6 failed, including
`Every_integration_test_class_declares_where_it_runs` (an empty derived
category set correctly refuses to vacuously credit every class, rather than
matching everything). Independently re-verified by the orchestrator that the
current tree (post phase-6 fixes) still produces the same shape.

## Full-suite confirmation

Independently re-verified by the orchestrator at the final commit
(`818aefb1`): `dotnet build tests/Architecture.Tests -c Release
--no-incremental` — 0 errors; full `Architecture.Tests` suite — **414/414
green**; `IntegrationTestSelectionTests` alone — **19/19** (8 pre-existing +
11 new: phase 4a's F1-F4 — F4 implemented as 7 separate facts per the
engineer's own judgement — plus one regression test phase 6 added).

## Phase 6 — review, findings closed

`infra-reviewer` (primary) and `backend-reviewer` (secondary, the C# side)
both ran. `security-reviewer` explicitly declared not warranted — no
`permissions:` change, no new or re-pinned action, no secret, no
`pull_request_target`; both reviewers independently re-checked this against
the diff rather than accepting the declaration, and agreed. **No blockers
from either.** Three genuine bugs found and closed, plus several should-fix
accuracy items:

1. **The filter-detection regex only recognized the double-quoted spelling**
   (`--filter "..."`) — an unquoted, `=`-joined, or single-quoted rewrite of
   either `ci.yml` step would have silently exempted it from F1's coverage,
   and the existing parity check (counting invocations, not filter
   *recognition*) wouldn't have caught it, since the unrecognized-filter step
   still counts as "found." Fixed: F2 now also asserts filter-parity — every
   invocation containing the literal substring `--filter` must be one the
   reader actually recognized as filtered, so an unanticipated spelling fails
   loudly instead of silently dropping out of coverage.
2. **The citation helpers (`InclusionStepCitation`/`ExclusionStepCitation`)
   threw an opaque, message-less exception** — and ran on every *passing* run
   of the primary facts too, since Shouldly's message argument is evaluated
   eagerly. Fixed: `FirstOrDefault` with a fallback message naming the
   probable cause (a stale reader) instead of a bare `InvalidOperationException`.
3. **A crafted `--filter` value containing a literal `-- RunConfiguration.TreatNoTestsAsError=true`
   inside its quotes would be misread as the real flag** — the token-search
   tokenized on plain whitespace, so a standalone `--` sitting *inside* a
   quoted filter string still counted as the real separator. Fixed: quoted
   spans are now blanked out before tokenizing, and a new regression fact
   (`A_standalone_separator_inside_a_quoted_filter_value_does_not_count_as_present`)
   uses the reviewer's exact adversarial input — the existing sibling fact's
   own passing message had admitted it tested a different, easier case.

Should-fix, closed: two stale `ci.yml` line-number citations in doc-comment
prose removed (one of which the PR's own T007 comment addition had made
newly wrong — the exact defect class SC-007 exists to close, reappearing
inside its own fix); `Explain(...)`'s hard-coded category-name list replaced
with one derived from `DerivedCategoryNames()`; F3 now calls
`DerivedCategoryNames()` directly instead of duplicating its LINQ chain under
a comment claiming the two "can never drift apart"; `CategoryDeclaration`'s
regex given the same 5-second timeout every other externally-sourced pattern
in the class already carries. Bundled in: `ParseInvocations` no longer throws
an unhandled `IndexOutOfRangeException` on a workflow whose last line ends in
an unterminated continuation; the field-ordering constraint between
`CategoryDeclaration` and `FilterArgument`/`CategoryTerm` is now documented
on `CategoryDeclaration`'s own doc comment, not only 130 lines away.

**This document's own T010 correction**: `tasks.md` originally said "all
twelve facts" — measured, it's 19 (8 pre-existing + 11 added across phase 4a
and phase 6). Corrected in `tasks.md` directly, with a note that the wrong
number is exactly the mistake this whole feature exists to prevent, now
caught in its own planning document rather than left uncorrected.

## Accepted in writing, not fixed

- **F2's invocation-parity check cannot see a second `dotnet test` chained
  onto the same line with `&&`** (a text-parsing limit, acknowledged in
  spec.md's own risk register — this reader is deliberately text-based, not a
  YAML/shell parser, matching the three existing precedents for this kind of
  workflow-content check in this repo).
- **`RunConfiguration.TreatNoTestsAsError=true` is a VSTest-specific
  mechanism** — confirmed correct for this repo's pinned SDK (`global.json`
  10.0.300, resolved 10.0.401, VSTest mode confirmed via the absence of any
  Microsoft.Testing.Platform opt-in and the presence of
  `Microsoft.TestPlatform.*.dll` in test output) — but if this repository
  ever migrates to the Microsoft.Testing.Platform runner, this exact flag
  would become silently inert (the MTP equivalent is
  `--minimum-expected-tests`), and nothing in this fix guards that
  transition. Out of scope for #2289; recorded so a future migration doesn't
  quietly reopen this issue.
- A few smaller doc-comment precision notes from `backend-reviewer` (three
  synthetic facts have an undocumented dependency on real `ci.yml` content;
  a duplicated file-enumeration helper) — cosmetic, not correctness.
