# Spec 139 — Loud when broken, quiet when absent

**Issue:** #2188 — *A `.Application` assembly that exists but cannot be loaded is
treated as "this context has no handlers" — the service starts healthy with zero
consumers on every queue*
**Branch:** `fix/2188-loud-when-broken-quiet-when-absent`
**Status:** Phase 3 complete — awaiting gate
**Lane:** autonomous (ADR-0144)
**ADRs:** 0037 (phases and gates), 0144 (the lane; phase 4a colour **red**), 0036
(smallest change, no drive-by error handling), 0088 (Wolverine defaults — this is the
method that applies them), 0042 (Wolverine as the dispatcher whose discovery this
feeds), 0105 (`Ensure.That` for argument guards — *not* what this is), 0050
(`[LoggerMessage]` logging, considered and rejected here for a stated reason), 0084
(≤ 300 LOC per file, ≤ 30 LOC per method), 0052/0053 (xUnit + Shouldly, sentence-style
test naming), 0109 (`[P]` disjoint-file parallelism), 0028 (GitFlow), 0086 (no
`Co-Authored-By`)

**Spec number 139.** Derived from the highest number on **any** ref, not the working
tree. `git log --all --name-only --pretty=format: -- specs/ | grep -oE 'specs/[0-9]{3}'
| sort -u | tail -3` gives 136, 137, **138**. `git ls-tree -d --name-only origin/develop
specs/` tops out at `137-one-copy-of-the-admin-helpers`, so 138 is unmerged: it lives on
`origin/fix/2187-the-aggregate-enforces-its-own-grid`
(`specs/138-the-aggregate-enforces-its-own-grid`). No remote branch is named
`^[0-9]{3}-`, and `create-new-feature.ps1`'s own comment (lines 70–79) says the branch
scanners contribute **0** in this repo because branch digits are issue numbers — the
number comes from `Get-HighestNumberFromGitSpecDirs`. 138 + 1 = **139**.

**The script was not run**, deliberately: the branch already exists and is checked out,
and `create-new-feature.ps1` cuts a branch. The derivation above reproduces exactly what
`Get-NextBranchNumber` computes — `max(branches = 0, specsDir = 137, gitSpecDirs = 138) + 1`.

---

## Phase 1 — every claim in the issue and in the brief, checked against the tree

### The defect is present, at the lines the issue cites

`src/ServiceDefaults/WolverineDefaults.cs`, `TryLoadApplicationAssembly` (lines 184–211;
the two offending catches are at **`:203`** and **`:207`**, exactly as filed):

```csharp
try
{
    return Assembly.Load(applicationName);
}
catch (FileNotFoundException)
{
    return null;
}
catch (FileLoadException)
{
    return null;
}
catch (BadImageFormatException)
{
    return null;
}
```

Consumer at **`:133`**:

```csharp
if (applicationAssembly != null)
{
    opts.Discovery.IncludeAssembly(applicationAssembly);
}
```

So `null` means "nothing to include" and all three failures produce it. **Verified by
reading the file, not taken from the issue.**

### The doc comment justifies exactly one of the three catches

Lines 177–183: *"Returns `null` if no matching assembly is loadable — keeps the
convention silent when a future context legitimately has no Application handlers to
discover."* That is `FileNotFoundException`. It is not `FileLoadException` (the file is
there and would not load) and not `BadImageFormatException` (the file is there and is
not a valid managed image for this runtime).

### What it costs, and why it is invisible

`AddWolverineForContext` continues, `UseWolverine` runs, the RabbitMQ transport is
configured with conventional routing and `AutoProvision()` — so **the queues are still
created** and simply never acquire a consumer. The service binds, `/health` answers, and
the only signal is queue depth. Nothing is written to any log: the three catch blocks
contain a bare `return null`.

### The file already argues against this, 130 lines earlier

Lines 70–82, on `ServiceLocationPolicy`:

> Wolverine 6 defaults this to NotAllowed, which sounds stricter but **fails quietly**:
> when codegen cannot build a dependency it drops that handler and the service still
> starts and reports healthy. Measured, not assumed — removing a single opt-in left the
> build clean, /health green and the e2e suite passing while **cameras stopped
> provisioning streams entirely**.

The same file diagnoses the class of defect in one place and reproduces it in another.
That is the strongest single argument in this spec and it is quoted from the tree.

### The constraint the brief raised — confirmed, and it decides the fix

`AddWolverineForContext<TDbContext>` is `public static … this IHostApplicationBuilder`
(line 40). `TryLoadApplicationAssembly` is called at **line 66**, inside that extension
method, which every caller invokes on `builder` **before `builder.Build()`**. Confirmed
at all nine call sites:

| Context | Call site |
|---|---|
| AuditObservability | `AuditObservabilityInfrastructureModule.cs:93` |
| Automation | `AutomationInfrastructureModule.cs:87` |
| CameraCatalog | `CameraCatalogInfrastructureModule.cs:74` |
| EventIngestion | `EventIngestionInfrastructureModule.cs:150` |
| Identity | `IdentityInfrastructureModule.cs:108` |
| LayoutComposition | `LayoutCompositionInfrastructureModule.cs:103` |
| OverlayDesigner | `OverlayDesignerInfrastructureModule.cs:72` |
| StreamDistribution | `StreamDistributionInfrastructureModule.cs:110` |
| SystemVariables | `SystemVariablesInfrastructureModule.cs:92` |

**There is no logger reachable at that point, and this repository has never built one.**
`grep -rn "LoggerFactory.Create\|NullLogger" src/ --include=*.cs` returns **zero**
matches outside `obj/`; the only `builder.Logging` use in `src/` is `Extensions.cs:63`,
which *configures* the OpenTelemetry pipeline rather than obtaining a logger.
`src/ServiceDefaults/Log.cs`'s `[LoggerMessage]` methods are all extension methods on
`ILogger`, which must come from DI (ADR-0050). Satisfying the issue's "at minimum log
it" would therefore require introducing a throwaway `LoggerFactory` — a pattern with
**no precedent anywhere in this codebase**, whose output would not reach the OTLP
exporter configured later in `Build()`, and which is a larger change than the
alternative.

### The precedent sits two lines above the call — same method, same class of condition

Lines 62–64:

```csharp
string rabbitConnection =
    builder.Configuration.GetConnectionString(rabbitConnectionName)
    ?? throw new InvalidOperationException($"Connection string '{rabbitConnectionName}' is required for Wolverine RabbitMQ transport.");
```

"Something about this deployment is not wired up" already **throws
`InvalidOperationException` from this exact method, at host-builder time, without a
logger.** The fix does not choose a posture; it makes an inconsistent branch consistent
with the one the method already has.

### All nine contexts have an `.Application` pair today

`ls -d src/*/Application` and `ls -d src/*/Infrastructure` return the same nine names.
So **no current caller reaches any of the three catches** — the `FileNotFoundException`
branch is genuinely forward-looking, exactly as its comment says, and this change cannot
regress a shipping context. It changes what happens the day one of them breaks.

### `InvalidOperationException` is not reserved for a narrower meaning here

98 occurrences across `src/`. Sampling (`AelInterpreter.cs:33,86,135,158,168`, and the
sibling at `WolverineDefaults.cs:64`) shows it used for "the state or configuration
reaching this code is not one this code can proceed from" — general, not a claimed
domain meaning. Nothing conflicts. **`Result<T, Error>` / `ApiError` (ADR-0047, 0089) do
not apply**: this is composition-root wiring, not a request crossing a trust boundary,
and there is no caller to hand a `Result` to — every call site is a fire-and-forget
statement in a module registration method.

### Claims from the brief that did **not** verify

- **"the two `.Application`-suffix checks"** — `grep -rn '"\.Application"' src/ tests/
  --include=*.cs` returns **one** match, `WolverineDefaults.cs:193`. The second is
  presumably the `const string InfrastructureSuffix = ".Infrastructure"` on line 186, in
  the same method. Either way there is nothing elsewhere in the tree to collide with.
- **Nothing in flight touches this file.** `git log --all --name-only --
  src/ServiceDefaults/WolverineDefaults.cs` shows ten commits, the newest already on
  `develop`; none of the eighteen live remote branches modifies it.
- **The issue's line numbers `:203`/`:207` are correct** — checked, not assumed.

---

## Decision — throw, wrapping the loader exception

**Chosen: let the two broken cases propagate as `InvalidOperationException` with the
original loader exception as `InnerException`. Keep `FileNotFoundException` silently
returning `null`.**

### Why throw rather than log

1. **No logger exists at the call point** (verified above), and building one would be a
   novel pattern in service of a weaker signal.
2. **The method already throws for the sibling condition**, two lines above. Log-here /
   throw-there in one method is worse than either alone.
3. **It fails before the process serves anything.** The throw is at host-builder time,
   so the outcome is "a service that never started", not "a running service that
   stopped". Constitution §NFR *Availability* wants 24/7 operation and zero-downtime
   rolling updates; a process that exits at startup is precisely what a rolling update's
   readiness gate is for — the previous revision keeps serving. A process that starts
   healthy with no consumers is what defeats it. **Stated as reasoning, not as an
   observed rollout**: the k8s publisher has never been run here and the only Helm chart
   is Mosquitto (`specs/047-the-decisions-we-made/audit.md:373`, issue 1015), so this
   argument is forward-looking. The immediate, real environment is Aspire run mode and
   the `AspireFixture`, where a throwing builder makes the resource go `FailedToStart`
   and a silent `null` does not.
4. **The issue itself frames throw as arguably right** for a 24/7 system where a silent
   handler gap is invisible for hours, and asks only that the choice be stated. It is
   stated here and will be restated in the PR body.

### The one cost, recorded rather than hidden

An exception thrown before the logging pipeline exists reaches **stderr only**. Aspire's
`FailedToStart` state is known not to carry the process log in the dashboard, so the
message may need `aspire run` in the foreground (or `docker logs`) to read. This is
already true of the sibling `InvalidOperationException` on line 64, so the change adds
no new class of problem — but it does mean **the exception message must stand alone**,
and it is why the loader exception is wrapped rather than summarised. FR-004 and the
phase-5 procedure both turn on this.

### Exception shape

- **Type:** `InvalidOperationException` — matching line 64 exactly.
- **Inner:** the original `FileLoadException` / `BadImageFormatException`, **preserved**.
  A developer debugging a broken deploy needs the CLR's own "the located assembly's
  manifest definition does not match the assembly reference" or "an attempt was made to
  load a program with an incorrect format", not a paraphrase.
- **Message:** names the assembly that failed, says the file was found (so it is not
  confused with the absent case), and says what the silent alternative would have cost.

---

## User stories

### US-1 (P1) — A broken `.Application` assembly stops the host instead of the handlers

**As** an operator rolling out a build,
**I want** a service whose bounded-context handler assembly is present but unloadable to
fail to start and say which assembly,
**so that** I find out in the deploy rather than from a queue-depth graph hours later.

This is the whole slice. It is independently shippable, observable end to end, and
touches one production file.

### US-2 (P3, not delivered as new work) — the absent case stays silent

Preserved behaviour. Covered by characterisation tests in this slice (see §Phase 4a) so
the change cannot quietly widen into "every context must have an Application assembly".

---

## Functional requirements

- **FR-001** `TryLoadApplicationAssembly` MUST return `null`, without throwing and
  without logging, when `Assembly.Load` raises `FileNotFoundException`.
- **FR-002** `TryLoadApplicationAssembly` MUST return `null` when the infrastructure
  assembly's simple name does not end in `.Infrastructure` (unchanged, lines 188–191).
- **FR-003** `TryLoadApplicationAssembly` MUST throw `InvalidOperationException` when
  `Assembly.Load` raises `FileLoadException` or `BadImageFormatException`.
- **FR-004** That `InvalidOperationException` MUST carry the raised loader exception as
  `InnerException`, and its `Message` MUST contain the derived `*.Application` assembly
  name.
- **FR-005** The XML doc comment on `TryLoadApplicationAssembly` MUST be corrected: it
  currently promises `null` "if no matching assembly is loadable", which after this
  change is false. **A comment left describing the old contract is how this defect
  reproduced itself in the first place** — the file's own `ServiceLocationPolicy`
  comment diagnoses the pattern, so leaving a stale one here is not cosmetic.
- **FR-006** No behaviour outside this method changes. The `:133` consumer is untouched;
  `null` still means "nothing to include".

## Non-functional requirements

- **NFR-001** `WolverineDefaults.cs` stays under 300 LOC (ADR-0084). It is 213 today; the
  chosen shape **removes** three lines before the doc comment and message are added, so
  the file lands near 215.
- **NFR-002** `TryLoadApplicationAssembly` stays under 30 LOC (ADR-0084). Merging the two
  broken cases into one `when` filter keeps it at roughly its current size. This is a
  real constraint, not a formality — two separate wrap-and-throw blocks would breach it.
- **NFR-003** No new package reference.

## Latency budget impact

**N/A.** This is startup-path composition, before any message is handled. It touches no
leg of the event-to-overlay path (constitution §IV). Indirectly it *protects* the
`Event → overlay state` leg — that leg is precisely what goes to infinity when
`SystemVariableValueChangedV1` has no consumer — but it adds no time to it, so no figure
is claimed and no dashboard obligation (§VII / ADR-0117) attaches.

---

## Does this need an ADR? — **No.** The argument, so the gate can overrule it

**For "needs an ADR":** "a bad deploy crashes the host rather than degrading" is an
operational posture for a 24/7 system, and ADR-0144 forbids the autonomous lane from
writing one. If this is architecture, the run is blocked.

**Against, and this is the conclusion:**

1. **The posture is already this method's.** Line 64 throws `InvalidOperationException`
   at host-builder time for a comparable "this deployment is not wired up" condition.
   The change does not introduce a posture; it removes an inconsistency with one.
2. **It is a defect against a rule CLAUDE.md already states**: *"No drive-by error
   handling. Validate at trust boundaries only. **Swallowed exceptions are review
   blockers.**"* Three bare `return null` catches are the named anti-pattern. Fixing a
   documented violation is implementation, not decision.
3. **Repo precedent at exactly this shape.** Spec 128 / issue #2137, *"A failed migration
   is loud"*, made the same trade — a broken deploy must fail loudly rather than boot
   healthy — across three files including `AppHost.cs` and `ci.yml`, and cited only
   existing ADRs (0067, 0024/0025, 0109, 0108, 0103). No new ADR was written for it. The
   same reasoning covers a smaller, single-method case.
4. **The blast radius is bounded by construction.** It can only throw during
   `AddWolverineForContext`, which runs before `Build()`. It cannot terminate a running
   service, cannot affect a request, and cannot fire for any of the nine contexts as they
   stand today (all nine have their `.Application` pair).

**Recorded honestly:** point 4 is what makes 1–3 sufficient rather than merely
suggestive. If a reviewer judges that "crash the host" deserves an ADR anyway, phase 4
must not start — that is the gate's call, and this section exists so it is a call and not
an omission.

---

## Acceptance scenarios (Gherkin)

### Happy — the legitimate absence stays silent

```gherkin
Given an infrastructure assembly named "Probe.Infrastructure"
  And no assembly named "Probe.Application" can be resolved
 When AddWolverineForContext derives and loads the Application assembly
 Then Assembly.Load raises FileNotFoundException
  And TryLoadApplicationAssembly returns null
  And no exception escapes
```

### Conflict — the file is there and will not load

```gherkin
Given an infrastructure assembly named "Probe.Infrastructure"
  And resolving "Probe.Application" yields an assembly whose identity does not match
 When AddWolverineForContext derives and loads the Application assembly
 Then Assembly.Load raises FileLoadException
  And an InvalidOperationException escapes
  And its message contains "Probe.Application"
  And its InnerException is that FileLoadException
```

### Bad request — the file is there and is not a managed image

```gherkin
Given an infrastructure assembly named "Probe.Infrastructure"
  And resolving "Probe.Application" attempts to load bytes that are not a valid assembly
 When AddWolverineForContext derives and loads the Application assembly
 Then Assembly.Load raises BadImageFormatException
  And an InvalidOperationException escapes
  And its message contains "Probe.Application"
  And its InnerException is that BadImageFormatException
```

### Not applicable — the naming convention does not hold

```gherkin
Given an assembly whose simple name does not end in ".Infrastructure"
 When TryLoadApplicationAssembly is called with it
 Then it returns null without attempting any load
```

### Auth

**N/A, stated rather than omitted.** This code path has no caller identity, no HTTP
surface and no scope check. Nothing in `sse.*` applies to a composition-root extension
method. No `RequireScope`, no fab authorization, no idempotency key.

---

## Independent end-to-end test procedure (phase 5)

Unit tests prove the branch. They do not prove an operator sees anything, and the §Cost
note above is exactly that risk. So:

1. `aspire run` **in the foreground** (not backgrounded — a background `dotnet run`
   swallows startup output here).
2. Confirm the nine context services reach Running and `/health` is green. **This is the
   control**: the change must not break the ordinary path, where all nine have their
   `.Application` assembly.
3. Provoke the failure on one context: with the stack stopped, overwrite
   `src/CameraCatalog/Api/bin/.../SmartSentinelEye.CameraCatalog.Application.dll` with a
   file of arbitrary bytes, then start again. **Keep a copy of the original** — and note
   that a restored file keeps its old timestamp, so MSBuild will skip the rebuild; touch
   it or rebuild explicitly.
4. **Observe:** the `camera-catalog` resource does not reach Running. Capture the
   `InvalidOperationException` text — it must name
   `SmartSentinelEye.CameraCatalog.Application` and carry the `BadImageFormatException`
   as inner. Record **where** it was readable (dashboard console log, stderr, or neither)
   — that is the finding the §Cost note asks for.
5. Restore the assembly, restart, confirm green.
6. **The counterfactual, and it is not optional** (memory: *prove a guard by
   counterfactual*): on `origin/develop`, repeat step 3. The service must reach Running
   and report healthy with its camera-catalog queues at `consumers: 0` — confirming the
   test catches the real defect and is not a construction artefact.

**Machine-state precondition:** check free space on `C:` before booting. The Docker vhdx
lives there and the known failure is the engine ceasing to answer until a GUI restart,
which ends the run. Also: **one machine, one Aspire stack** — a second concurrent boot
produces `FailedToStart` that reads exactly like this change's success. If `C:` is tight,
phase 5 is **deferred with that reason written in the PR**, not faked.

---

## Locked tech choices

- .NET 10, `System.Reflection.Assembly.Load(string)` — unchanged, not replaced.
- `InvalidOperationException`, matching `WolverineDefaults.cs:64`.
- xUnit + Shouldly (ADR-0052), sentence-style test names (ADR-0053).
- `internal` + `InternalsVisibleTo` via the csproj `AssemblyAttribute` element, the
  pattern used by `SmartSentinelEye.MigrationRunner.csproj:18`,
  `ScenarioSimulator.csproj:21`, `EventIngestion.Infrastructure.csproj:8`,
  `StreamDistribution.Infrastructure.csproj:8`. **Not reflection** — see plan.
- No `Ensure.That` change: ADR-0105 governs *argument* preconditions, and this is not
  one. The existing `Ensure.That` guards at lines 51–54 are untouched.
