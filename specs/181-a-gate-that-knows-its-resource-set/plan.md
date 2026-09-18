# Plan 181 — A gate that knows its resource set

**Spec:** `specs/181-a-gate-that-knows-its-resource-set/spec.md`
**Issue:** #2268
**ADRs:** ADR-0108, ADR-0033, ADR-0103, ADR-0068, ADR-0037, ADR-0144, ADR-0109
(parallelism), ADR-0105 (`Ensure.That`), ADR-0050 (logging), ADR-0049
(`CancellationToken` last)

---

## 1. Bounded context and layers

**None.** This slice touches no bounded context, no Domain, no Application, no
Infrastructure and no Api. There is no aggregate, no value object, no
invariant, no domain event and no integration event.

The three surfaces it does touch:

| Surface | Project / file | Role |
|---|---|---|
| Composition root | `src/AppHost` | Reports the resource set it composed and the state each reached |
| CI gate | `scripts/wait-for-e2e-stack.sh` | Decides whether the Playwright suite may run |
| CI workflow | `.github/workflows/ci.yml` | Wires the two together |

**Boundary rules that still apply.** `src/AppHost` references every context's
`Api` project already (it is the composition root, and ADR's no-cross-context
rule binds contexts, not the root). The new type introduces **no** reference
to any context and **no** reference to `Shared.Contracts` or `Shared.Kernel` —
it depends only on `Aspire.Hosting`. NetArchTest's existing rules are unaffected;
verify rather than assume (T009).

---

## 2. The design

### 2.1 Why the AppHost writes it, in one sentence

The only process that knows both *which resources the composition contains* and
*what state each has reached* is the AppHost, and it already exposes both
through public Aspire API the repo uses elsewhere. Everything else in spec §5's
rejected table is an attempt to reconstruct from outside what one process holds
inside.

### 2.2 `StackStatusReport` — shape

New file `src/AppHost/StackStatusReport.cs`. A static class with one public
entry point, called between `Build()` and `RunAsync()`:

```
StackStatusReport.Attach(app, path)
```

Responsibilities, in order:

1. **Seed the expected set from the model.** Take
   `DistributedApplicationModel.Resources`, keep everything that is **not**
   `IResourceWithoutLifetime`, and record each with state `(not reported)`.
   This is the half that makes the set derived rather than listed: a resource
   DCP never creates still has a line, which is the `minio` case.

   > **Correction (T001, phase 4b):** `IResourceWithoutLifetime` does not hold
   > this role in Aspire.Hosting 13.5.3, the version `Aspire.AppHost.Sdk`
   > pins. Decompiling `Aspire.Hosting.dll` during the live-boot spike showed
   > `ParameterResource` does **not** implement it, and a full-source grep
   > found nothing in the assembly that does — the interface is vestigial,
   > referenced only in two internal `is` checks. Filtering on it excludes
   > zero resources, so every secret parameter (`PostgresPassword`,
   > `KeycloakPassword`, the four `*ClientSecret` parameters, etc. — 10 total)
   > would have landed in the report. The implementation filters on
   > `resource is not ParameterResource` instead — still a type filter, not a
   > name list, which is what this section's reasoning actually requires; it
   > just names the type that exists in this SDK version. This AppHost has no
   > `AddConnectionString` resource, so `ParameterResource` is the only
   > lifetime-less type in play. See `src/AppHost/StackStatusReport.cs` and
   > the PR body for the full finding.
2. **Write the seeded report immediately**, before any resource starts. A gate
   that finds the file already present and full of `(not reported)` is reading a
   live boot; a gate that finds nothing knows the switch was not set.
3. **Watch and overwrite.** `ResourceNotificationService.WatchAsync` updates
   `state` and `exitCode` per resource; rewrite the whole file on each event.
4. **Write atomically** — temp file in the same directory, then
   `File.Move(temp, path, overwrite: true)`. The gate polls this file every few
   seconds; a half-written read must be impossible, not merely unlikely.

### 2.3 The one hazard phase 4 must resolve, with the evidence already found

`AspireFixture.cs:305` records, in the repo's own words, that before
`StartAsync` *"WatchAsync has nothing to watch and completes"*. So `Attach`
must not start watching on the calling thread at composition time.

Two candidate hooks, both public Aspire API:

- `builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(...)`, or
- `IHostApplicationLifetime.ApplicationStarted` from `app.Services`.

**Phase 4 must pick by observation, not by reading docs** (T007): boot the
stack for real, `cat` the file, and confirm lines appear *while* the stack is
still coming up. A writer that only produces a file after everything is Running
is useless — it can never describe a failure.

If neither hook yields a live watch, that is the escalation in spec §8: stop,
do not reach for the gRPC resource service.

### 2.4 Report format — the contract between the two halves

Fixed here because three tests and two programs depend on it byte-for-byte.

```
# stack-status v1 <ISO-8601 UTC timestamp>
<name>\t<state>\t<exit code or empty>
```

- One header line beginning `#`; the gate ignores every `#` line.
- Tab-separated. Resource names cannot contain tabs (Aspire names are
  DNS-label-shaped), so no quoting is needed and none is invented.
- `state` is the snapshot's `State?.Text`, verbatim, or `(not reported)` when
  no event has arrived. Verbatim on purpose: a state the gate does not
  recognise must read as *not started*, and must print with its real name so the
  log says something true.
- `exit code` is empty when null. Never `0` for "unknown" — that would claim a
  clean exit, the exact error `FormatResourceDeathMessage`'s doc comment
  records having avoided.
- Lines sorted by name, ordinal, so a diff between two boots is readable.

**What the file must never contain** (spec §3, and the reason the type filter is
`IResourceWithoutLifetime` rather than a name list): parameter values,
connection strings, endpoint URLs, environment variables, or anything from
`Snapshot.Properties`. Three fields, nothing else. It is printed into a public
CI log.

### 2.5 The gate's new first section

`scripts/wait-for-e2e-stack.sh` gains a section **before** the migration probe,
because it is the question whose failure explains the others — the same
reasoning the migration probe's own comment gives for sitting first today.

Logic:

- Path from `${STACK_STATUS_FILE:-${GITHUB_WORKSPACE:-$PWD}/stack-status.tsv}`.
  The override exists so the node harness can point at a fixture; CI uses the
  default.
- Poll every 5 s for up to ~10 min (120 iterations), matching the file's
  existing loop idiom.
- **Ready** when the file exists and every non-comment line is `Running`, or
  `Finished` with exit code `0`.
- **Fatal immediately**, without spending the rest of the budget, when any line
  is `Finished` or `Exited` with a non-zero exit code. Mirrors the fixture's
  migration short-circuit and its stated reason.
- **Fatal on timeout**, naming every line that is not started, as
  ` <name>(<state>)` — the same rendering idiom `unmigrated_databases` already
  uses, so the log reads consistently.
- **Fatal when the file never appears**, with a message naming
  `StackStatusFile=` and the boot step. Not best-effort: "I could not ask" is
  not "the stack came up". This reuses the migration probe's stance and should
  reuse its sentence.

The gateway probe at the bottom stays best-effort. Nothing else in the script
changes.

### 2.6 Workflow wiring

In the e2e job's boot step, before `nohup dotnet run`:

```sh
rm -f "$GITHUB_WORKSPACE/stack-status.tsv"
```

and the run gains `StackStatusFile="$GITHUB_WORKSPACE/stack-status.tsv"` after
the existing `ScenarioSimulator=false`.

**The absolute path is load-bearing.** `dotnet run` gives the AppHost process a
working directory of `src/AppHost` — `AppHost.cs` relies on this today with
`Path.GetFullPath("Resources/clips")`. A relative `stack-status.tsv` would land
in `src/AppHost` and the gate, which looks in `$GITHUB_WORKSPACE`, would report
"no status report" on every run. Write the absolute path; do not resolve it in
the AppHost.

Add the file to the "on failure or cancellation" diagnostic step alongside
`apphost.log` — a red gate should leave its evidence in the job, not only in
the step that failed.

### 2.7 US3's guard

New `tests/Integration.Tests/AppHostStackStatusTests.cs`, `[Trait("Category",
"FixtureLogic")]` so it runs in the Docker-free step (`ci.yml:67`), not the
30-minute Docker job. Direct sibling of `AppHostE2ESwitchTests`, which already
reads `.github/workflows/ci.yml` and asserts the boot command carries
`-- ScenarioSimulator=false`; mirror that class's approach rather than inventing
one.

Two facts:

1. The e2e job's boot command sets `StackStatusFile=`.
2. The expected set computed from a model built with the e2e job's arguments
   contains the project and container resources and **excludes** every
   `IResourceWithoutLifetime` resource. `DistributedApplicationTestingBuilder.
   CreateAsync` builds without starting, so this costs no containers — the
   property `AppHostE2ESwitchTests` documents and relies on.

Fact 2 needs the filter to be reachable from the test. Expose it as an
`internal static` member on `StackStatusReport` plus `InternalsVisibleTo`, **or**
have the test apply `IResourceWithoutLifetime` itself and assert the writer
agrees. Phase 4 picks; prefer whichever avoids adding `InternalsVisibleTo` to
the AppHost if the AppHost does not have one already — check before adding.

---

## 3. What each test is allowed to prove

Named explicitly because this slice's risk is a guard that reads the artefact
rather than the behaviour.

| Test | Proves | Does **not** prove |
|---|---|---|
| mjs: gate refuses a report naming a FailedToStart resource | The script's decision | That the AppHost ever writes such a report |
| mjs: gate refuses a missing report | The not-best-effort stance | Anything about the AppHost |
| mjs: the three existing tests | The migration probe is unchanged | — |
| xUnit: boot command sets the switch | The wiring cannot silently drop | That the switch does anything |
| xUnit: expected set excludes lifetime-less resources | The filter is the composition's | That the file is written |
| **Phase 5, by hand** | **That the AppHost writes a live file during a real boot, and that the gate goes red on a resource that did not start** | — |

The bottom row is the only one that closes the loop. A PR that ships the top
five and skips it has shipped a guard that reads a design artefact — the exact
failure the standing lesson names. **Phase 5 is not optional here.**

---

## 4. Boundary and convention checks phase 4 must satisfy

- `Ensure.That(x).IsNotNull()` for argument guards (ADR-0105) — **not**
  `ArgumentNullException.ThrowIfNull`. The AppHost exemption in the stack table
  covers the composition file itself; a new class with a public entry point
  should use the house guard. If `Shared.Kernel`'s `Ensure` is not referenced by
  the AppHost project, do **not** add the reference for this — use the
  composition-root exemption and say so in the PR.
- No primitive-typed domain state is introduced — there is no domain model here,
  so `PrimitiveBoundaryTests` is not in play. Confirm it stays green anyway.
- Collections declared with an explicit type and a collection expression
  (`List<string> lines = [];`), enforced at `warning` in Release.
- Private fields carry no leading underscore.
- `CancellationToken` last parameter on any async method (ADR-0049); no
  `ConfigureAwait`.
- Handler deconstruction rule: N/A, no handlers.
- `dotnet format --verify-no-changes` and the Release build with
  `TreatWarningsAsErrors` must both be clean. SonarAnalyzer's 300-LOC/30-LOC
  advisory limits apply to the new file.
- Bash: the script is `set -uo pipefail` (note: **no `-e`**, deliberately — the
  probes tolerate non-zero curl). Match the existing style; do not add `-e`.
- ShellCheck is not in CI today; do not add it as part of this slice.

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| The watch hook yields nothing until the stack is fully up, so the report can never describe a failure | T007 observes a live boot before the gate is written to depend on it. This is the make-or-break unknown, scheduled early. |
| Lifetime-less resources still appear with a never-Running state and wedge the gate permanently | The `ParameterResource` filter (corrected from `IResourceWithoutLifetime` — §2.2's note) is applied at seed time, not by state. T007's live dump confirms the filter is sufficient. |
| Worst-case job time grows past `timeout-minutes: 45` | The added wait is 10 min worst case and overlaps waits that already exist. T010 records the observed figure from the first green run; if it is material, the fix is to shorten the downstream waits, not to weaken the gate. |
| A resource legitimately not `Running` in the e2e shape (beyond `migrations`) | **Resolved (T007).** Every `AddProject<T>()` resource gets an Aspire-built-in `<name>-rebuilder` companion (`ExplicitStartupAnnotation`, stays `NotStarted` unless the dashboard's Rebuild command is invoked) — 11 of them in this composition, confirmed live. Exempted at the model level, in `StackStatusReport.ExpectedResourceNames`, by filtering `resource.HasAnnotationOfType<ExplicitStartupAnnotation>()` — Aspire's own marker for "does not start on its own", not a name list, and not the script (the "exempted by state semantics in the script" idea above turned out not to be where this needed handling: the script sees a resource set that's already correct, and stays free of name-based special cases). |
| The gate becomes flaky and someone weakens it | ADR-0144 forbids weakening a gate to reach green. Recorded here so the next reader sees it before reaching for a `|| true`. |
| The three existing mjs tests need editing | They must not. An edit is evidence the change reached the migration probe — block, do not adjust (spec §7). |
