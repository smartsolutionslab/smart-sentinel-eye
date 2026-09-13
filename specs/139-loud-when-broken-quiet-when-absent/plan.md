# Plan 139 — Loud when broken, quiet when absent

**Spec:** [`spec.md`](spec.md) · **Issue:** #2188 · **Branch:**
`fix/2188-loud-when-broken-quiet-when-absent`

---

## Bounded context and layers

**None, and that is the significant fact.** `ServiceDefaults` is cross-cutting
infrastructure shared by all nine contexts — it is not a bounded context, has no Domain,
no aggregate, no value object, no repository, and publishes no event. ADR-0091's
`Identifier`-suffix rules, ADR-0092's per-aggregate folders, ADR-0093's per-message-kind
folders and constitution §II's primitive ban all apply to *domain models* and have no
purchase here.

| Layer | Touched |
|---|---|
| Domain | no |
| Application | no |
| Infrastructure | no |
| Api | no |
| `Shared.Kernel` / `Shared.Contracts` | no |
| **`ServiceDefaults`** | **yes — one method** |

**Boundary rules (constitution §III, NetArchTest):** unaffected. No cross-context project
reference is added; `ServiceDefaults` is already referenced by every Infrastructure
module and adds no reference of its own. `PrimitiveBoundaryTests`,
`HandlerDeconstructionTests` and the Architecture.Tests boundary rules are all untouched —
none of them scans `ServiceDefaults` composition code, and nothing new is introduced for
them to scan.

## Entities, value objects, invariants

**None.** The only invariant expressed by this change is a *contract on one private
helper*, and it is stated as FR-001…FR-004 in the spec rather than as a type:

> `null` means "this context legitimately has no Application handlers". It must never
> mean "loading them failed".

There is no domain concept to model. Introducing one (an `AssemblyLoadOutcome` result
type, an `ApplicationAssemblyName` value object) would be speculative generality against
CLAUDE.md §Karpathy — the method has one caller, in the same file, four lines away.

## Messaging — domain event → integration event

**None.** No domain event, no integration event, no Wolverine handler, no outbox row. The
change alters *whether Wolverine's handler discovery is configured at all*, which is a
precondition of messaging rather than a participant in it.

---

## The change, concretely

### `src/ServiceDefaults/WolverineDefaults.cs`

Replace the three-catch tail of `TryLoadApplicationAssembly` (lines 195–210) with:

```csharp
try
{
    return Assembly.Load(applicationName);
}
catch (FileNotFoundException)
{
    // The one legitimate absence: the context has no Application assembly,
    // so there are no handlers to discover.
    return null;
}
catch (Exception exception) when (exception is FileLoadException or BadImageFormatException)
{
    throw new InvalidOperationException(
        $"Assembly '{applicationName}' was found but could not be loaded, so every " +
        "message handler in it would go unregistered while the service reported healthy.",
        exception);
}
```

**Why one `when` filter and not two catch blocks.** ADR-0084 caps a method at 30 LOC.
The method body is 27 lines today; two separate wrap-and-throw blocks add roughly ten and
breach it. One filter is also honest about the design: the two exceptions are *the same
case* — the file is there and did not load — and differ only in why.

**Why `throw new`, not `throw;`.** A bare rethrow gives the operator a
`BadImageFormatException` with no statement of consequence. The whole point of the issue
is that the consequence (zero consumers, healthy service) is the part nobody infers. The
message carries it; `InnerException` carries the loader's own diagnosis.

Also update the XML doc comment (FR-005). The current text — *"Returns `null` if no
matching assembly is loadable"* — becomes false the moment this lands, and a stale
comment describing the old contract is the exact failure mode this file already
documents one screen above.

### `src/ServiceDefaults/SmartSentinelEye.ServiceDefaults.csproj`

`private static` → `internal static` on `TryLoadApplicationAssembly`, plus:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
    <_Parameter1>SmartSentinelEye.ServiceDefaults.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

**Why `internal` + IVT rather than reflection.** Four csproj files already do exactly
this, and `SmartSentinelEye.MigrationRunner.csproj:14–17` writes down the reasoning
verbatim: the type is "a composition detail rather than API … worth testing without
making the type public to do it." Reflection would work, but it turns a rename into a
runtime `NullReferenceException` in a test instead of a compile error, and it is not what
this repo does. The class stays `public static`; the *method* moves from `private` to
`internal`, which widens nothing outside the assembly.

**The alternative rejected:** testing through the public `AddWolverineForContext`. That
stands up the whole Wolverine pipeline — Postgres message store, RabbitMQ transport,
`AutoProvision()` — and needs live connection strings. It would be an integration test of
a one-line branch.

---

## How the red case is constructed — verified on this machine, not reasoned about

This was the load-bearing unknown, so it was **run** rather than argued. A throwaway
console app under the scratchpad, `dotnet run`, .NET 10:

```
P1: System.BadImageFormatException: Could not load file or assembly 'Probe.Application,
    Culture=neutral, PublicKeyToken=null'. An attempt was made to load a program with an
    incorrect format.
P2: System.IO.FileLoadException: Could not load file or assembly 'Probe2.Application,
    Culture=neutral, PublicKeyToken=null'. An operation is not legal in the current
    state. (0x80131509)
P3: System.IO.FileNotFoundException: Could not load file or assembly
    'Totally.Absent.Application, Culture=neutral, PublicKeyToken=null'. The system cannot
    find the file specified.
P4: System.BadImageFormatException: Bad IL format.
```

**All four exceptions are raised by the CLR loader. Nothing is mocked.** The mechanism:

### `AssemblyLoadContext.Default.Resolving` is the seam, and it is a real one

`Assembly.Load(string)` raises `Resolving` when default probing finds nothing, and **an
exception thrown by that handler propagates to the caller unchanged** — probe P1 confirms
it: a `BadImageFormatException` raised inside the handler by
`Assembly.Load(corruptBytes)` emerged from `Assembly.Load("Probe.Application")` still
typed `BadImageFormatException`, and re-labelled by the runtime with the *requested*
assembly's identity.

- **`BadImageFormatException`:** handler returns
  `Assembly.Load(new byte[] { 0x01, 0x02, 0x03, 0x04 })`. Genuinely corrupt bytes,
  genuine loader rejection (P4 shows the same call standing alone).
- **`FileLoadException`:** handler returns an assembly whose identity does **not** match
  the request — `typeof(object).Assembly` will do. The runtime itself raises
  `FileLoadException` (`0x80131509`). **This one is produced by the runtime, not thrown
  by the handler**, which is exactly the "found but will not load" condition the issue
  describes.
- **`FileNotFoundException`:** register no handler for that name at all (P3).

### The infrastructure assembly the method takes as its argument

`TryLoadApplicationAssembly(Assembly infrastructureAssembly)` only reads
`GetName().Name`, and it must end in `.Infrastructure`. A dynamic assembly supplies that
with no file on disk — confirmed in the second probe run:

```
dynamic name: 'Probe.Infrastructure'
caught BadImageFormatException, inner=BadImageFormatException,
  msg=Could not load file or assembly 'Probe.Application', …
```

```csharp
AssemblyBuilder.DefineDynamicAssembly(
    new AssemblyName($"Probe{Guid.NewGuid():N}.Infrastructure"),
    AssemblyBuilderAccess.Run);
```

### Two hazards the test must handle, or it will be flaky rather than red

1. **`Resolving` is process-global and xUnit runs test classes in parallel.** Every probe
   name must be unique per test (the `Guid` above), the handler must match on that exact
   name and return `null` for everything else, and it must be **unsubscribed in a
   `finally`**. A leaked handler that returns corrupt bytes for a name another test
   later needs is a cross-test failure that reproduces only under parallelism.
2. **`Assembly.Load` caches successes, not failures**, but a unique name per test removes
   the question entirely.

A small `IDisposable` helper (`sealed class ResolvingProbe`) that subscribes on construct
and unsubscribes on dispose keeps this to one `using` per test.

---

## Phase 4a — colour, and which tests are which

**Behaviour-changing → red** (ADR-0144). Declared here so there is no ambiguity, and the
declaration distinguishes the two kinds of test in the same file, because a green test in
a red phase is otherwise read as a failure:

| Test | Before the change | Colour |
|---|---|---|
| `An_application_assembly_that_is_present_but_unloadable_fails_the_host` (`FileLoadException`) | returns `null`, no throw | **RED — must be observed failing** |
| `A_corrupt_application_assembly_fails_the_host` (`BadImageFormatException`) | returns `null`, no throw | **RED — must be observed failing** |
| `An_absent_application_assembly_is_not_an_error` (`FileNotFoundException`) | returns `null` | green — **characterisation of the preserved branch** |
| `An_assembly_outside_the_naming_convention_is_not_probed` | returns `null` | green — **characterisation of the preserved branch** |

The two red tests fail with Shouldly's "expected `InvalidOperationException` but no
exception was thrown". **That text is the phase-4a evidence and must be quoted verbatim
in the PR body** — the engineer receives it as its brief and may not edit the tests to
pass.

The two green ones exist because the smallest change that satisfies FR-003 could also be
written to throw on *everything*, and nothing in the tree would catch it — all nine
contexts have their `.Application` pair, so no existing test exercises the absent branch.
They are the guard against the fix over-reaching.

---

## Alignment check

| Rule | Status |
|---|---|
| Constitution §II (no primitives on a domain model) | N/A — no domain model |
| Constitution §III (no cross-context refs) | unaffected; no new reference |
| Constitution §IV (latency budget) | N/A — startup path, cited in spec |
| Constitution §VII / ADR-0117 (dashboard per implemented leg) | N/A — no leg |
| Constitution §VIII (validate at trust boundaries) | this *is* the composition boundary; the change removes a swallow, adds no drive-by handling elsewhere |
| Constitution §Testing (new behaviour starts red) | declared red above |
| ADR-0084 (≤ 300 LOC file, ≤ 30 LOC method) | file ≈ 215; method within 30 by using one `when` filter — see NFR-002 |
| ADR-0105 (`Ensure.That` for argument guards) | not an argument guard; existing guards untouched |
| ADR-0141 (`Option<T>` over nullable) | **advisory, and deliberately not applied.** Scope is Domain and Application; `ServiceDefaults` is neither. Changing the return to `Option<Assembly>` would touch the `:133` consumer and is a second change. |
| ADR-0050 (`[LoggerMessage]`) | considered, rejected with a stated reason (spec §Why throw rather than log) |
| ADR-0109 (`[P]` disjoint files) | near-nil here — two files, one of which the other's compile depends on |
| ADR-0030 (Conventional Commits), 0086 (no `Co-Authored-By`), 0087 (rebase-only) | applied at phase 7 |

## Files to change

| Path | Change |
|---|---|
| `src/ServiceDefaults/WolverineDefaults.cs` | catch split + wrap-and-throw; doc comment corrected; `private` → `internal` |
| `src/ServiceDefaults/SmartSentinelEye.ServiceDefaults.csproj` | add the `InternalsVisibleTo` `AssemblyAttribute` item group |
| `tests/ServiceDefaults.Tests/WolverineApplicationAssemblyTests.cs` | **new** — four tests plus the `ResolvingProbe` helper |

No other file. No migration, no AppHost change, no contract change, no frontend, no e2e.
`tests/ServiceDefaults.Tests/SmartSentinelEye.ServiceDefaults.Tests.csproj` needs **no**
edit: it already references `SmartSentinelEye.ServiceDefaults`, xunit, Shouldly and Moq,
and its assembly name already matches the IVT target.
