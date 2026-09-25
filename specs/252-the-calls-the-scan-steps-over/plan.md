# Plan 252 — The calls the scan steps over

**Spec**: [spec.md](spec.md) · **Issue**: #2470 · **Phase**: 2 (Plan)

## Constitution / ADR check

| Gate | Result |
|---|---|
| §II primitives in domain | N/A — no domain model touched |
| No cross-context references | N/A — `Architecture.Tests` only; no new project reference |
| §IV latency | N/A — `tests/` only |
| §Testing: new behaviour red first (ADR-0139, ADR-0144) | Red: three probes missing before the fix |
| ADR-0105 guards | No argument guards added (private static helpers over reflection) |
| New ADR needed? | No — test-guard scoping (spec header) |

## Bounded context and layers

None. Files:

| File | Change |
|---|---|
| `tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs` | +3 probe types, doc update |
| `tests/Architecture.Tests/OutboxCommitTests.cs` | probe fact expected list +3; `BodiesOf` transitive; `ReferencesSaveChanges` second operand position; docs |

No `src/` change. No entities, value objects, messaging, contracts or Aspire
resources.

## Design

### 1. Probes (`OutboxCommitProbe.cs`, namespace `…Architecture.Tests.Persistence`)

Names end in `Repository` so they are candidates under **both** the current
filter and #2469's (every top-level type).

```csharp
public sealed class MethodGroupOffenderRepository(ProbeDbContext dbContext)
{
    public Func<int> Save() => dbContext.SaveChanges;          // ldvirtftn (0xFE 0x07)
}

public sealed class BaseMethodGroupOffenderRepository : DbContext
{
    public Func<int> Save() => base.SaveChanges;               // ldftn (0xFE 0x06)
}

public sealed class AsyncLambdaOffenderRepository
{
    public Task SaveAsync(ProbeDbContext dbContext, Func<Func<CancellationToken, Task>, Task> run) =>
        run(async cancellationToken => await dbContext.SaveChangesAsync(cancellationToken));
}
```

Load-bearing details, each measured in spec §1.1:

- `Func<int>` selects the parameterless `SaveChanges()` overload.
- `BaseMethodGroupOffenderRepository` must derive from `DbContext` (so `base.`
  is legal) and uses `base.` explicitly — `this.SaveChanges` / bare
  `SaveChanges` is a virtual load (`ldvirtftn`) and would duplicate the first
  probe instead of exercising `ldftn`. Never instantiated, like `ProbeDbContext`.
- `AsyncLambdaOffenderRepository` takes the context as a **method parameter**
  and holds **no fields**. A captured parameter forces a display class
  (`+<>c__DisplayClass…+<<SaveAsync>b__…>d`, depth 2). A primary-constructor
  capture would lift the lambda onto the type (depth 1) and the probe would be
  caught today — green on arrival.
- The lambda takes and forwards the `CancellationToken` so CA2016 has nothing
  to say in Release.

Each gets a `<summary>` in the file's existing voice: what shape it is, the
opcode/nesting it compiles to, and why that shape escaped before spec 252. The
file-level doc's "deliberately violate … never fixed … exact list" paragraph
already covers them; add one sentence naming spec 252.

### 2. Expected list (`The_rule_sees_both_spellings_of_a_direct_commit`)

Add three rows, keep `ignoreOrder: true`:

```
SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository
SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository
SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository
```

Add a `<para>` to its doc: spec 252 — these three are the shapes the scan
stepped over (delegate loads, depth-2 nesting), and what each row proves. Name
kept (renaming a test-writer-owned fact in the fix commit muddies the red/green
diff); it still sees both spellings.

`AsyncLambdaOffenderRepository` must appear **once** — the assertion is exact,
and `Offenders` only admits top-level types (current filter: its nested types
are not named `…Repository`; #2469: `!type.IsNested`), so the depth-2 state
machine is reported under its top-level name.

### 3. `BodiesOf` — transitive nesting

```csharp
IEnumerable<Type> types = [type, .. NestedTypesOf(type)];

private static IEnumerable<Type> NestedTypesOf(Type type) =>
    type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .SelectMany(nested => (IEnumerable<Type>)[nested, .. NestedTypesOf(nested)]);
```

Recursion over a tree (nesting cannot cycle); depth is bounded by source
nesting plus compiler lifting (observed max 2). Any equivalent shape is fine;
keep it a pure function of `Type`, no new state.

Doc: extend "The nested types are the point" with the depth-2 case — an async
lambda's state machine is nested in its closure class, not in the type — and
say the walk is transitive for that reason.

### 4. `ReferencesSaveChanges` — the delegate-load operand position

Today the loop tests `il[i] is 0x28 or 0x6F` and reads the token at `i + 1`.
Generalise to "where does a method token start, if one could start here":

```csharp
for (int i = 0; i + 4 < il.Length; i++)
{
    int operand = il[i] switch
    {
        0x28 or 0x6F => i + 1,                                            // call, callvirt
        0xFE when il[i + 1] is 0x06 or 0x07 => i + 2,                     // ldftn, ldvirtftn
        _ => -1,
    };

    if (operand < 0 || operand + 4 > il.Length)
    {
        continue;
    }

    int token = BitConverter.ToInt32(il, operand);
    // resolve + compare + catch: unchanged
}
```

Bounds: the loop guarantees `i + 4 < il.Length`, so `il[i + 1]` is in range;
`operand + 4 > il.Length` guards the two-byte form's token. The existing
comparison (exact name membership, `IsAssignableFrom(DbContext)`) and the
existing catch clause are **not** touched — the spec-193 control
(`FailureSubscriptionRepository`) must stay unflagged. Update the `// 0x28 call,
0x6F callvirt` comment to name all four opcodes and why the delegate loads are
included (a method group is a commit deferred, not avoided).

Any equivalent formulation is acceptable; the behaviour contract is: the same
token comparison, applied at two more opcode positions.

### 5. Blind-spot record (US3)

Append a `<para>` block to `ReferencesSaveChanges`' doc headed **What this does
not see, on purpose** (spec 252 / #2470):

- **Interface dispatch** — a `DbContext` behind an interface resolves to the
  interface's method; none exists in `src/`. Introducing such an abstraction is
  a design change: revisit this guard with it.
- **`Database.ExecuteSql*` / `ExecuteUpdate*` / `ExecuteDelete*`** — they join
  the ambient transaction rather than bypass it, so whether one escapes the
  outbox is a runtime fact, not a call-site fact. Eight live `ExecuteSql*`
  sites on 2026-09-25, four in scanned assemblies, none announcing anything. A
  rule for raw-SQL writes to announcing aggregates would be a different guard.
- **Reflection, `dynamic`, expression trees** — no IL call site to match.

Keep it short: one or two lines each; the reasoning in full lives in spec §2.

## Commit sequence (each builds on its own — ADR-0087)

1. `test(architecture): probe the outbox guard with a method-group and an async-lambda commit`
   — probes + expected list. Probe fact **red**, theory green. Builds.
2. `fix(architecture): read delegate loads and nested closures in the outbox guard's IL scan`
   — §3, §4, §5. All green; commit-1 files unmodified (`git diff HEAD~1 --
   tests/Architecture.Tests/Persistence/OutboxCommitProbe.cs` empty; the
   expected list unchanged).

## Verification

Spec §5: filtered run red then green, full `Architecture.Tests`, Release build,
three counterfactuals (drop `0x07`; drop `0x06`; one-level nesting), each
reverted, each showing exactly one probe missing. Output verbatim into
`verification.md`. No Aspire stack needed.

## Risks

- **#2469 (PR #2587) edits the same file** — spec §7 lists the four textual
  collisions. Whichever lands second rebases; the stale "#2470 tracks
  nested-of-nested" sentence in #2469's `Offenders` doc must not survive.
- **Other guards walk the test assembly** (`PrimitiveBoundaryTests`,
  `NameMutabilityConventionTests`, `StaleCodeConventionTests`, …). A second
  `DbContext` subclass or a `Func<…>`-typed parameter might trip one. Mitigation:
  full-project run after commit 1, not just the filtered one.
- **A random `0xFE 0x06/0x07` pair inside operand data** now yields one more
  resolution attempt; an unresolvable token is swallowed by the existing catch,
  and a resolvable one would still have to be `DbContext.SaveChanges*`.
  Negligible, same class of risk the scan already accepts for `0x28`/`0x6F`.
- **Locked binaries**: if an AppHost from this worktree holds `src/*/Api/bin`,
  build with `--artifacts-path` outside the repo; do not stop a stack you did
  not start.
