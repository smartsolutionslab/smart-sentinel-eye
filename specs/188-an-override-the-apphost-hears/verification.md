# Verification 188 — An override the AppHost hears (#2254)

**Latency: N/A.** `src/AppHost/` is a composition root, not a runtime
participant in `event arrival → overlay rendered`; no leg's measurement
status moves (spec.md, front matter).

## Summary

`AddParameter(name, literal, secret)` — the overload every one of AppHost.cs's
ten parameter call sites used — never reads `builder.Configuration`, so a
`Parameters:<name>=<value>` command-line argument was silently inert,
including for `MigrationRunnerClientSecret`, the one parameter spec 128
actually needed to inject a failing dependency by hand. The fix adds
`AddOverridableParameter` (`src/AppHost/OverridableParameters.cs`), which
reads `Parameters:<name>` from configuration and falls back to the literal
only when the configured value is null or empty, then switches all ten call
sites to it. `ParameterResource.Default` stays `null`, so nothing changes for
`aspire publish`.

This note consolidates verification already performed across phase 4a
(`test-writer`), phase 4b (`infra-engineer`), the orchestrator, and phase 6
(`infra-reviewer`) — re-confirming several of the load-bearing claims directly
rather than only repeating them — and records, explicitly, the one procedure
(spec §7's real-stack injection) that was **not** run locally and why.

---

## Verified

### 1. Red-then-green sequence (ADR-0139)

**Phase 4a, first run** (commit `4e8edfd7`, tests as originally written, no
production code touched yet) — **2 red, 3 green**, matching tasks.md's
phase-4a table exactly. Re-run independently in this pass, in a throwaway
worktree pinned to that commit, to confirm the red output is real and not
merely asserted:

```
Failed AppHostParameterOverrideTests.Every_declared_parameter_honours_its_own_override
  Shouldly.ShouldAssertException : resolved[name]
    should be "override-value-for-PostgresUser"
    but was "postgres"
  'PostgresUser' did not resolve to the value its own 'Parameters:PostgresUser='
  argument supplied (resolved 'postgres' instead).

Failed AppHostParameterOverrideTests.A_parameter_argument_overrides_the_declared_default
  Shouldly.ShouldAssertException : value
    should be "deliberately-wrong"
    but was "dev-only-migration-runner-secret"
  'Parameters:MigrationRunnerClientSecret=deliberately-wrong' on the command
  line must change what the AppHost composes — that is the whole mechanism
  #2254 was filed for. A literal-valued 'AddParameter' overload never reads
  'builder.Configuration', so this argument is inert until
  AddOverridableParameter replaces it.

Passed A_parameter_with_no_argument_keeps_its_default
Passed An_empty_parameter_argument_keeps_its_default
Passed An_override_does_not_change_the_composed_resource_set

Total tests: 5, Passed: 3, Failed: 2
```

**After the fix** (commit `f678e531`, all ten call sites switched to
`AddOverridableParameter`) — **5/5 green**. Re-run independently in this pass
on the current branch tip:

```
Passed An_empty_parameter_argument_keeps_its_default [317 ms]
Passed Every_declared_parameter_honours_its_own_override [61 ms]
Passed A_parameter_with_no_argument_keeps_its_default [59 ms]
Passed An_override_does_not_change_the_composed_resource_set [107 ms]
Passed A_parameter_argument_overrides_the_declared_default [107 ms]

Total tests: 5, Passed: 5
```

### 2. The `secret:` flag threading check

`git show f678e531 -- src/AppHost/AppHost.cs` diffs all ten call sites; every
line changes only `AddParameter` → `AddOverridableParameter`, with the
`secret:` argument (or its absence) byte-identical before and after. Verified
directly against `AppHost.cs` at the current branch tip: `PostgresUser` alone
carries no `secret:` argument; the other nine (`PostgresPassword`,
`KeycloakPassword`, `IdentityAdminClientSecret`,
`MigrationRunnerClientSecret`, `RabbitMqPassword`,
`ScenarioSimulatorClientSecret`, `EventIngestionMqttClientSecret`,
`StreamDistributionAttributionClientSecret`,
`SystemVariablesSeederClientSecret`) all carry `secret: true` — matching
spec.md §2's table of ten exactly.

### 3. Publish-mode safety (plan §5 R2)

Independently re-run in this pass, not merely re-cited: generated the Aspire
manifest (`dotnet run ... -- --publisher manifest`) three ways —

- on this branch, no `Parameters:` arguments,
- on this branch, **with** `Parameters:MigrationRunnerClientSecret=deliberately-wrong`
  and `Parameters:PostgresPassword=testpassword` present,
- on a throwaway `origin/develop` worktree (`4588d2c0`), no arguments.

All three parameter entries in every manifest are the unresolved input
placeholder shape (`"value": "{PostgresPassword.inputs.value}"`, `"secret":
true` under `inputs`) — no entry carries a `"default"` key at any of the ten
parameter resources. The branch-with-overrides and branch-without-overrides
manifests are byte-identical. The branch and `origin/develop` manifests are
byte-identical after normalizing the worktree path prefix (`D:/Github/sse-2254`
vs. the develop worktree's own path), which is the only difference a `diff`
reports. This confirms plan §5 R2 (`aspire publish` never gains a secret
default) both by direct inspection and by the develop-baseline comparison the
phase-6 reviewer performed.

### 4. The composition-derived-count counterfactual

Reported by phase-6 `infra-reviewer`: adding an eleventh parameter via plain
`AddParameter` (not `AddOverridableParameter`) in a scratch worktree caused
`Every_declared_parameter_honours_its_own_override` to fail and **name** the
new parameter — proving the fact's parameter count comes from
`builder.Resources` at run time (tasks.md T004's "never from a list written
in the test" constraint), not from a count baked into the assertion. Not
independently re-run in this pass: reproducing it requires an executable-logic
edit to `AppHost.cs`, which this task's own hard constraints forbid touching
even transiently, so the reviewer's report is taken as the evidence rather
than re-derived.

### 5. Comment-only diffs — five, now six

The five files US3 originally touched (`AppHostE2ESwitchTests.cs`,
`AppHostStackStatusTests.cs`, `AppHostMediaMtxImageTests.cs`,
`AppHostMigrationGateTests.cs`, `AppHostReplicaCountTests.cs`, commit
`5630a1b1`) were confirmed comment-only by two independent readers plus a
hash comparison at the time.

**This pass's phase-6 follow-up fixes a sixth file**,
`tests/Integration.Tests/AppHostContainerImagePinTests.cs:54`, which carried
the same false claim (*"A developer's `aspire run`, and the shape the
end-to-end job boots"*) and was missed by the original sweep despite landing
on `origin/develop` (spec 187) 47 minutes before spec 188's own spec commit.
Fixed the same way: the comment now says

> Run mode: the largest population, including the two resources
> (`fixture-video`, `camera-sim`) a published manifest omits because it
> publishes in publish mode, not run mode (spec 187 §2.1). Not the shape CI's
> end-to-end job or `aspire run` boots — neither passes a `Parameters:`
> argument.

Verified comment-only by the same discipline as the other five: stripped
`/* */` and `//` (which covers `///`) from both the pre-edit and post-edit
text of the file and hashed the remainder.

```
before.cs  sha256: 2ccf91e837e9b6bbc569dd6477d97f0da62e6174e40e2bd76d2c0b7f543a905d
after.cs   sha256: 2ccf91e837e9b6bbc569dd6477d97f0da62e6174e40e2bd76d2c0b7f543a905d
```

Identical. The arrays themselves (`RunModeArguments`, `FixtureArguments`) and
all executable code in the file are byte-for-byte unchanged; only the doc
comment's prose moved.

**spec.md §1.2's audit table is also corrected in this pass.** The real count
at HEAD, re-run with the same `Parameters:` grep the spec's own audit used
(excluding `bin/`, `obj/`, `node_modules/`):

| File | Arrays | `Parameters:` args |
|---|---|---|
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | 1 | 4 |
| `tests/Integration.Tests/AppHostE2ESwitchTests.cs` | 2 | 8 |
| `tests/Integration.Tests/AppHostStackStatusTests.cs` | 1 | 4 |
| `tests/Integration.Tests/AppHostMediaMtxImageTests.cs` | 1 | 4 |
| `tests/Integration.Tests/AppHostMigrationGateTests.cs` | 1 | 4 |
| `tests/Integration.Tests/AppHostReplicaCountTests.cs` | 1 | 4 |
| `tests/Integration.Tests/AppHostContainerImagePinTests.cs` | 2 | 4 |

**7 files, 9 arrays, 32 arguments** — not the spec's original "28 inert
arguments across 6 files and 7 arrays". `AppHostContainerImagePinTests.cs`
landed after spec 188's own audit was written (spec 187, merged to develop
before spec 188's branch point but audited incompletely), which is why it was
missed rather than deliberately excluded. spec.md §1.2 has been corrected in
place to the table above, and its "complete and bounded" claim is now false
as originally worded — the closing statement in that section has been
softened to record that this correction itself was needed, rather than
re-asserting completeness a second time on the same unverified footing.

### 6. Existing suites, unmodified, still green

`154/154` `Category=FixtureLogic` facts pass on the current branch tip
(`dotnet test ... --filter "Category=FixtureLogic"`, re-run in this pass):

```
Passed!  - Failed: 0, Passed: 154, Skipped: 0, Total: 154, Duration: 4 s
```

Release build clean (re-run in this pass, both `src/AppHost` and
`tests/Integration.Tests`): 0 errors. `AppHost.cs` and
`StackStatusReport.cs` each carry one pre-existing advisory metric warning
(`S1541`/`S104` cyclomatic complexity and file length on `AppHost.cs`; `S107`
parameter count on `StackStatusReport.cs`) — confirmed present, unchanged,
identically worded before and after this pass's edits, and carved out of
`TreatWarningsAsErrors` per ADR-0084 (advisory, not enforced).

`dotnet format --verify-no-changes` on the three files this phase-6 follow-up
touched reports the same warnings (two pre-existing `IDE1006` naming
warnings on `AppHostContainerImagePinTests.cs`'s two array fields) with and
without this pass's edit — confirmed by running it against the pre-edit
commit as well, so the exit code is not a regression this pass introduced.

---

## NOT verified, and why

### Spec §7's real-stack injection / control-run / develop-counterfactual procedure

**Not performed locally in this repository at any point in this spec's
delivery**, including in this phase-6 follow-up pass. Spec §7 calls for
booting a real Aspire stack with
`Parameters:MigrationRunnerClientSecret=deliberately-wrong`, observing
`migrations` fail with the Keycloak token error, a control run without the
override succeeding, and the same command line on `origin/develop` also
succeeding (proving the override was truly inert there).

**Reason:** this machine currently has a persistent, unrelated Docker stack
that has been running for approximately 46 hours. This repository's own
recorded experience (`one-machine-one-aspire-stack` in the operator's
memory) is that a second concurrent Aspire boot produces `FailedToStart`
failures that read exactly like a code defect. Attempting spec §7's
procedure against that background would risk a false-negative or
false-positive result more than it would prove anything real about this
change.

**This is deferred to CI's blocking `integration` job** (tasks.md T008),
which boots a real Aspire stack via `AspireFixture` in an isolated CI runner
and genuinely exercises all ten parameters end to end, including
`MigrationRunnerClientSecret` via the fixture's own arguments. **If that job
is not green when this PR is evaluated, that is the signal spec §7 would
have given locally, and the PR should not merge on the strength of this note
alone.**

### The persistent-volume trap (finding 2) is a documentation fix only

The comment added to `src/AppHost/OverridableParameters.cs` documents a real
operational trap: `PostgresPassword`, `KeycloakPassword`, and
`RabbitMqPassword` are container-initialization credentials, and postgres,
keycloak, and rabbitmq all run `ContainerLifetime.Persistent` +
`WithDataVolume()` in run mode (`isRunMode && !isE2ETests`). Overriding one of
these three via `Parameters:` in a developer's `aspire run` changes what the
*service* expects without changing what the *already-initialized persistent
container* holds, producing stack-wide auth failures.

**Nothing in this PR's own test suite exercises this trap**, and nothing
could without contradicting the rest of the suite's design:
`AspireFixture.cs` always passes `E2ETests=true`, which this AppHost's own
run-mode conditional (`isRunMode && !isE2ETests`) uses to select **ephemeral**
containers for exactly the integration/CI lane — so the fixture never
attaches a persistent volume in the first place, and the trap only manifests
in a human's own `aspire run` against a volume that already exists from a
prior boot. This is stated plainly rather than implied: the comment is a
warning for the next reader who overrides one of these three parameters by
hand, not a functional fix, and no automated check in this repository would
catch a regression in it.

---

## Files touched in this phase-6 follow-up pass

| File | Change |
|---|---|
| `tests/Integration.Tests/AppHostContainerImagePinTests.cs` | Comment-only (the sixth stale-comment fix; hash-verified above) |
| `src/AppHost/OverridableParameters.cs` | Comment-only: documents the `Parameters__<name>`/user-secrets alternative to a literal command-line argument, and the persistent-volume trap for the three container-initialization credentials |
| `src/AppHost/StackStatusReport.cs` | Comment-only: `builder.AddParameter(...)` → `builder.AddOverridableParameter(...)` in the doc comment, naming the helper actually in use |
| `specs/188-an-override-the-apphost-hears/spec.md` | §1.2's audit table and argument-count claim corrected to the sixth file (7 files, 9 arrays, 32 arguments) |

No file in this list touches `AppHost.cs`'s executable logic or
`AppHostParameterOverrideTests.cs`, per this pass's hard constraints.
