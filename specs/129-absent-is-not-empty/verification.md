# Verification 129 — absent is not empty

Phases 1–4 only. Phase 5 (`/verify` against a running stack) and phase 6 are
the orchestrator's.

## T001 — the baseline, which is also the refutation

`MigrationRunner.Tests`, on this branch before any edit, Release:

```
Passed ...Throws_when_the_realm_has_no_fab_groups_at_all [206 ms]
Passed ...Propagates_a_failure_to_reach_the_realm [19 ms]
Passed ...Deduplicates_repeated_names [6 ms]
Passed ...Tells_a_realm_with_no_fab_groups_apart_from_one_whose_groups_are_unusable [26 ms]
Passed ...Skips_a_group_whose_name_is_not_a_usable_fab_and_keeps_the_rest [11 ms]
Passed ...Throws_rather_than_returning_empty_when_nothing_is_usable [11 ms]
Passed ...Returns_every_fab_group_under_fabs [1 ms]
Passed ...MigrationRunTests.The_failure_names_the_context_it_happened_in [109 ms]
Passed ...MigrationRunTests.A_migrator_that_throws_is_reported_rather_than_escaping_the_process [10 ms]
Passed ...MigrationRunTests.A_run_that_applies_every_migration_reports_success [29 ms]
```

**206 ms, not 30 s.** #2139 says this test "now takes 30 s" because of a
wait #2062 added; the wait was reverted inside #2062 itself (`0df30ac4`,
`56777dbb`) and `56777dbb` records the same figure at 27 ms. After this
spec's change it is **39 ms**. The only thing that ever cost 30 s here was
removed eleven commits before this branch was cut.

## T002 — the characterisation net, GREEN and unmodified

After the shape change (`defeb25b`), the same ten tests: **10 passed**. The
diff of `KeycloakProvisionedFabSourceTests.cs` in that commit is one `using`
line and two fake signatures — **no test body, no assertion and no test name
is touched**, which is the evidence the refactor preserved behaviour rather
than the tests being adjusted to it.

## T003 / T008 — `dotnet build -c Release`

`Build succeeded. 0 Warning(s) 0 Error(s)`, at the shape change and again at
the tip. AppHost confirmed not running first (MSB3027): the only `dotnet.exe`
processes were Rider's and MSBuild's node reuse.

## T004 — the phase-4a RED, verbatim

```
[xUnit.net 00:00:00.74]     ...Tells_an_absent_fabs_group_apart_from_one_that_has_no_children [FAIL]
  Failed ...Tells_an_absent_fabs_group_apart_from_one_that_has_no_children [78 ms]
  Error Message:
   Shouldly.ShouldAssertException : absent.Message
    should not be
"Nothing at all under '/fabs' in the realm: the group is absent, or present with no children. It is imported with the realm and nothing creates it later, so this is a realm that was imported without fabs rather than one still importing. Provisioning cannot continue: every event written by any fab would be lost, and proceeding would report success while doing nothing."
    but was

Failed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 252 ms
```

The failure message is itself the defect: the verdict hedges between two
faults because the value it reasons over cannot tell them apart.

## T005 — every new assertion proved by counterfactual

Each patch was applied in the worktree, built, run, then reverted with
`git checkout --` **and `touch`** — a restored file keeps its old timestamp
and MSBuild would skip the rebuild, leaving the counterfactual's result
standing. Worktree confirmed clean afterwards, and the full sweep below was
run on the reverted tree.

| Counterfactual | Expected | Observed |
|---|---|---|
| 404 arm reverted to `Some([])` | only `A_fabs_group_that_is_not_in_the_realm_is_absent` fails | exactly that: `Shouldly.ShouldAssertException : tree.HasValue should be / but was`, with the other three `FabGroupTreeTests` passing |
| `ChildlessGroupVerdict` collapsed onto the unusable-names string | assertion 2 fails, assertion 1 passes | `childless.Message should not be` at **line 145** |
| `AbsentGroupVerdict` collapsed onto the unusable-names string | assertion 3 fails, assertions 1 and 2 pass | `absent.Message should not be` at **line 146** |

Assertion 1 (line 144) needed no separate counterfactual: the natural red in
`61c55d5c` is that counterfactual, and it named line 144. So all three
`ShouldNotBe` pairs fail independently and **none is carrying another**.

**One test here is a control and does not discriminate**, stated rather than
left to be discovered: `Throws_when_the_fabs_group_itself_is_not_in_the_realm`
passes before and after, because an absent group aborted the run before this
change too. It guards FR-005/FR-011 across the change, and it is also what
would catch a future reading of "absent" as "wait and ask again" — that would
hang it rather than fail it. It is not offered as evidence of the fix.

## T006 / T007 — GREEN

```
MigrationRunner.Tests            12 passed, 0 failed   (244 ms)
Identity.Infrastructure.Tests    19 passed, 0 failed   (861 ms)
Identity.Application.Tests       61 passed, 0 failed   (494 ms)
Architecture.Tests              361 passed, 0 failed   (3 s)
```

`Throws_when_the_realm_has_no_fab_groups_at_all`: **39 ms** (SC-003).

## The five fakes, one by one

Four files, five implementations — #2139 counts four, which is the file
count. Each mapping is a contract change stated at the fake, not a fit to
make something pass:

| Fake | Change | Why it is the contract, not a fit |
|---|---|---|
| `KeycloakProvisionedFabSourceTests.StubKeycloakAdminClient` | takes `Option<IReadOnlyList<string>>` instead of `string[]` | The stub's whole job is to hand the source a group tree; a tree that can only be present is the thing this issue is about. `Source(names)` still means present, so every existing test keeps its meaning |
| `KeycloakProvisionedFabSourceTests.ThrowingKeycloakAdminClient` | signature only | It throws; it never produced a value |
| `Identity.Infrastructure.Tests.UnreachableKeycloakAdminClient` | signature only | Same |
| `Identity.Infrastructure.Tests.EnrolledKiosksKeycloakAdminClient` | signature only | Same |
| `Identity.Application.Tests.FakeKeycloakAdminClient` | dictionary miss becomes `None`, was `[]` | A path the fake's realm does not hold **is** absent — this is the defect restated at the fake. Nothing reads or writes `SubGroups`: no test on any branch sets it, so no assertion depends on either reading |

**None left unchanged.**

## Security

Read-only realm and group resolution, and it stays read-only. No new call,
no widened lookup, no changed scope or token. Every input that aborted the
run before aborts it still — the absent case gained its own sentence, not
its own outcome — so nothing became more permissive, and a fab source that
cannot enumerate still refuses rather than proceeding (spec 019 FR-011).

## Latency

**No §IV leg is touched.** All six run from event arrival to overlay
rendered; this is startup fab resolution in `MigrationRunner`, which has
finished before any camera streams. No figure in §IV moves, and none of the
four states in its table changes.

## Noted, not fixed

- `src/Shared.Kernel/Option.cs`'s docstring still says "NRT is disabled at
  the solution level", stale since ADR-0141 enabled it. `Shared.Kernel` is a
  contention file (ADR-0109) and this slice does not own it.
- #2139's premise about the tree is refuted in three places; see `spec.md`.
  The wait it asks for is declined rather than implemented.
