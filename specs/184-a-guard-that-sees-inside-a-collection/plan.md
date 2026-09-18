# Plan 184 — A guard that sees inside a collection

**Spec:** `specs/184-a-guard-that-sees-inside-a-collection/spec.md`
**Issue:** [#2291](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2291)

---

## 1. Bounded context and layers

**None.** This is the unusual case where the honest answer to "which bounded
context?" is that there isn't one. The diff is confined to the cross-cutting
architecture-test project:

| Project | File | Change |
|---|---|---|
| `tests/Architecture.Tests` | `PrimitiveBoundaryTests.cs` | the walk unwraps constituents; assembly set becomes a parameter; two new facts; doc block updated |
| `tests/Architecture.Tests` | `PrimitiveBoundaryProbe.cs` *(new)* | the deliberately-violating probe corpus |

**No `src/` file changes.** No Domain, Application, Infrastructure or Api layer
is touched, no `Shared.Contracts` message is added or versioned, no migration,
no Aspire resource, no DI registration. The boundary rules (constitution §III,
NetArchTest) are unaffected because nothing new references anything.

**Consequence for ADR-0109 parallelism:** the two files are disjoint but the
work is not. The probe corpus is the red-test mechanism for the walk change, so
it must exist and be *observed red* before the walk changes. That is a hard
sequence, not a `[P]` pair. `tasks.md` marks it accordingly.

## 2. The walk change

### 2.1 What is wrong, in one sentence

`Banned` is consulted on a property's **declared** type only; a banned type that
arrives as a generic argument or array element is enqueued into a queue whose
entry condition (`type.Namespace?.StartsWith("SmartSentinelEye")`) then discards
it.

### 2.2 The shape of the fix

Replace the property loop's either/or with a **constituent closure**. For each
property, compute the set of types the declared type is built out of — itself,
its generic arguments (recursively), its array element type (recursively), each
with `Nullable<>` stripped — then:

- **record** every constituent that is in `Banned`, as a `StateMember`;
- **enqueue** every constituent that is not, exactly as today.

Sketch, for the reviewer's benefit — the engineer writes the real thing:

```csharp
foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    if (property.Name == "PendingEvents")
    {
        continue;
    }

    foreach (Type constituent in Constituents(property.PropertyType))
    {
        if (Banned.Contains(constituent))
        {
            members.Add(new StateMember(type, property.Name, constituent, isValueObject, IsComputed(type, property)));
        }
        else
        {
            pending.Enqueue(constituent);
        }
    }
}
```

`Constituents` is the recursive successor to `Unwrap`:

- strip `Nullable<>` first (preserving `:187`'s behaviour, now applied at every
  level rather than only the top);
- if the type is an array, recurse into `GetElementType()` **and** yield the
  array type itself (so the array type is still enqueued, preserving today's
  behaviour);
- if the type is a constructed generic, recurse into each
  `GetGenericArguments()` entry **and** yield the constructed type itself;
- otherwise yield the type.

**Three properties this must have, each because something can go wrong:**

1. **Termination.** A self-referential generic (`Node<T>` whose argument closure
   reaches `Node<T>`) must not loop. The enclosing walk's `seen` set does not
   protect `Constituents` because that runs before anything is dequeued, so
   `Constituents` needs its own visited set or a depth bound. Spec AS-6.
2. **No throw on a generic parameter.** `typeof(Option<>).GetGenericArguments()`
   yields `T`, whose `Namespace` is null. Today that is harmless (it is enqueued
   and dropped); it must stay harmless.
3. **De-duplication within one property.** `IReadOnlyDictionary<string, string>`
   must not record `Tags : String` twice. The existing `.Distinct()` on the
   offender projection (`:70`) already collapses it, but `members` itself should
   not carry the duplicate — the `A_value_objects_own_backing_values_are_exempt`
   fact projects without `Distinct` into `exempted` (it does `.Distinct()` too,
   `:103`), so this is belt-and-braces rather than load-bearing. Prefer the
   closure being a set.

### 2.3 What deliberately does **not** change

| Thing | Why it stays |
|---|---|
| `Banned` | §II's set is correct; this spec is not amending it (that would need an ADR) |
| `IsComputed`, restricted to `bool` | the restriction is reasoned at `:215-233`; widening it to collections would re-open the `Tile.Row` hole. A computed-looking collection of primitives is storage. Spec §2 |
| `IsIdentityReferenceInsideValueObject` | works unchanged once the **constituent** is what is recorded — `IReadOnlyList<Guid>` inside a composite VO now records `Guid`, which is exactly the type it tests |
| the `DeclaringTypeIsValueObject` exemption filter | unchanged; it is what keeps AS-3 green |
| `PendingEvents` skip by name | unchanged |
| the `System`-namespace drop at `:173` | **unchanged, and that is the point.** The fix does not make the walk *enter* BCL types; it makes the ban apply to their arguments before the queue ever sees them. Making the walk descend into `System` types would pull in the whole BCL graph |
| `DomainAssemblies()`'s `LoadFrom` glob | unchanged; spec §8 A3 |

### 2.4 The offender message

`StateMember.PropertyType` becomes the banned **constituent**, so today's
projection `$"{DeclaringType.Name}.{Name} : {PropertyType.Name}"` renders
`ProbeAggregate.Tags : String` rather than `… : IReadOnlyList\`1`.

**That is the right primary string** — it is what `IsIdentityReferenceInsideValueObject`
needs, it matches the issue's own transcript format, and it keeps the existing
three facts' assertions untouched. **Recommended addition, engineer's call:**
carry the declared type as a second field on `StateMember` and append it when it
differs, so the message reads
`ProbeAggregate.Tags : String (via IReadOnlyList<String>)`. A developer told
"`Tags : String`" on a property declared `IReadOnlyList<string>` will look for a
`string` property and not find one. If the second field is added, the probe
facts assert the **primary** form so the assertion does not become a test of
string formatting.

## 3. The probe corpus

### 3.1 How the walk is pointed at it

`Roots()` and `WalkAggregateState()` gain an assembly-set parameter, with the
existing behaviour as the no-argument overload:

```csharp
private static List<StateMember> WalkAggregateState() => WalkAggregateState(DomainAssemblies());

private static List<StateMember> WalkAggregateState(IReadOnlyList<Assembly> assemblies) { … }
```

`allDomainTypes` (the abstract-subtype expansion at `:205`) is computed from the
**same** set, so a probe discriminated union would expand within the probe.

The probe facts call `WalkAggregateState([typeof(PrimitiveBoundaryTests).Assembly])`.

**Nullable is not used for the parameter** — two overloads, not
`IReadOnlyList<Assembly>? = null`. ADR-0141's preference is advisory and scoped
to Domain/Application, but an overload is clearer here and costs one line.

### 3.2 The corpus

New file `tests/Architecture.Tests/PrimitiveBoundaryProbe.cs`, namespace
`SmartSentinelEye.Architecture.Tests` (so `:173`'s `SmartSentinelEye` prefix
check passes), carrying a file-level doc comment stating in the first sentence
that **these types violate constitution §II on purpose, exist to be caught, and
must never be "fixed"**.

| Probe type | Shape | Role |
|---|---|---|
| `ProbeIdentifier` | `readonly record struct (Guid Value) : IStronglyTypedId<Guid>` | satisfies `AggregateRoot<T>`'s `struct, IStronglyTypedId<Guid>` constraint; must be **exempt** |
| `ProbeName` | `sealed record (string Value) : IValueObject<string>` | single-valued VO; `Value` must be **exempt** |
| `ProbeTagSet` | `sealed record (IReadOnlyList<string> Values) : IValueObject` | composite VO whose backing value **is** a collection of primitives — must be **exempt** (§II: "backing **values** — plural") |
| `ProbeHighlight` | `sealed record (IReadOnlyList<Guid> Overlays, ProbeName Label) : IValueObject` | identity references in a collection inside a composite VO — must be **flagged** (ADR-0140) |
| `ProbeAggregate` | `sealed class : AggregateRoot<ProbeIdentifier>` | the roots-bearing type |

`ProbeAggregate`'s members:

| Member | Expected |
|---|---|
| `public int RawCount` | **flagged** — the positive control the *unfixed* walk already catches |
| `public IReadOnlyList<string> Tags` | **flagged** — the issue's exact shape |
| `public string[] Labels` | **flagged** — the array spelling |
| `public IReadOnlyDictionary<ProbeName, int> Counters` | **flagged** on the value argument only |
| `public IReadOnlyList<IReadOnlyList<DateTimeOffset>> Windows` | **flagged** — two levels of nesting |
| `public IReadOnlyList<int?> Optionals` | **flagged** — `Nullable<>` stripped inside an argument |
| `public IReadOnlyList<ProbeName> Names` | **not flagged** — collection of a domain type |
| `public ProbeTagSet Tagging` | **not flagged** (reaches `ProbeTagSet.Values`, exempt) |
| `public ProbeHighlight Highlight` | reaches `ProbeHighlight.Overlays`, **flagged** |

Inherited from `AggregateRoot<ProbeIdentifier>`: `Id` (reaches
`ProbeIdentifier.Value : Guid`, exempt as an `IValueObject<Guid>`), `Version`
(reaches `AggregateVersion`'s backing value, exempt), `PendingEvents` (skipped
by name).

**Expected offender list, exact — 7 entries, ordered as `.Order()` produces:**

```
ProbeAggregate.Counters : Int32
ProbeAggregate.Labels : String
ProbeAggregate.Optionals : Int32
ProbeAggregate.RawCount : Int32
ProbeAggregate.Tags : String
ProbeAggregate.Windows : DateTimeOffset
ProbeHighlight.Overlays : Guid
```

**Asserted as an exact sequence, not a superset.** A knowable population gets a
pinned count ([[prove-a-guard-by-counterfactual]]); a `ShouldContain`-only
assertion would let an over-eager fix start flagging `ProbeAggregate.Names` or
`ProbeTagSet.Values` without anything going red, which is the precise regression
AS-3 exists to prevent. Exactness costs an edit whenever the probe grows — that
is the intended cost, and the file's doc comment says so.

Spec A4 notes the inherited members are believed exempt. **The exact list is
what confirms it**: if `Version` or `Id` contributes an entry, the list will not
match and the engineer learns it from the test rather than from an argument.

### 3.3 The two new facts

```csharp
[Fact]
public void The_walk_sees_a_primitive_reached_through_a_collection_or_array()
```

Runs the walk over the probe assembly with the same filter chain as the real
fact (`!Computed`, `!DeclaringTypeIsValueObject || IsIdentityReferenceInsideValueObject`,
`Distinct`, `Order`) and asserts the exact 7.

```csharp
[Fact]
public void A_value_objects_own_backing_collection_is_exempt()
```

Asserts the probe's exempt projection contains `ProbeTagSet.Values` and
`ProbeName.Value`, and that the offender list contains neither.

**Both are red before the walk change**, for the same root cause and with
different symptoms:

- the first: 1 entry (`RawCount`) instead of 7;
- the second: `ProbeTagSet.Values` absent from the exempt list, because a
  member that is never recorded lands in neither list.

The filter chain should be shared with the real fact rather than duplicated —
extract it once and call it from both — so that a future change to the exemption
logic cannot apply to the real corpus but not to the probe.

## 4. Entities, value objects, invariants

**No production entity, value object, aggregate or invariant is added or
changed.** The probe types are test fixtures, not domain models; they live in
the test assembly, are not persisted, are not serialized, and are not reachable
from any production assembly.

The **invariant being enforced** is constitution §II itself, already stated
there and in ADR-0139/ADR-0140. This spec does not restate it as a new rule; it
widens the mechanism that fails the build when it is broken:

> **§II, as the guard can now see it.** A domain model does not carry state of a
> banned type, **whether the banned type is the declared type of the member or a
> constituent of it** — a generic argument at any depth, or an array element.
> The four exemptions are unchanged and apply to the constituent exactly as they
> applied to the declared type.

## 5. Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` addition,
no `V<N>` version bump (ADR-0073), no Wolverine registration, no outbox
interaction. Recorded explicitly so its absence reads as a decision.

## 6. Boundary rules

- **No cross-context project reference** is introduced. `Architecture.Tests`
  already references every context's four projects plus `MigrationRunner` and
  `ApiGateway` — that is its job, and the new file adds no reference.
- The probe types derive from `Shared.Kernel`'s `AggregateRoot<T>` and implement
  `Shared.Kernel.Primitives.IValueObject`, both of which the test project
  already has transitively through the Domain references. **No `.csproj` change
  is expected.** If the engineer finds one is needed, that is a signal worth
  reporting, not a silent edit.
- The probe namespace must start with `SmartSentinelEye` or the walk drops it at
  `:173`. Using the test project's own namespace satisfies that without a new
  one.

## 7. Risks, and what each is mitigated by

| Risk | Mitigation |
|---|---|
| The probe leaks into the real corpus and fails the build | `DomainAssemblies()`'s glob does not match the test assembly name; `roots.Count.ShouldBe(12)` is the tripwire if it ever does (spec §4) |
| The widened walk starts flagging a legitimate value object | AS-3, plus the exact-list assertion; plus AS-5's real-domain run measured empty |
| The fix flags something real on `develop` | spec §1.2 says none exists; T007 measures it. If one appears: raise as a scope question, do **not** fix in this PR |
| A future reader "fixes" the probe's deliberate violations | file-level doc comment says so in its first sentence; the exact-list assertion turns any such edit red |
| The red is observed on the wrong thing | SC-002/SC-003: the red must be asymmetric — control present, `Tags` absent |
| The walk changes but the guard still does not guard real code | §6 step 5's counterfactual on `Camera`, which is a different experiment from the probe test and is required separately |
| The revert in step 5 appears not to work | forced rebuild — a restored file keeps its old timestamp and MSBuild skips it |

## 8. What this plan does **not** decide

- Whether `reached > 100` should become a pinned exact count (spec §2.1) — left
  alone; recommended to the orchestrator as a follow-up.
- Whether §II's guard should also read constructor parameters and fields
  (spec §2) — a pre-existing declared limit, unchanged.
- Whether `Option<T>`'s leak-through-`Value` behaviour should be made explicit
  rather than incidental (spec §1.1) — it works today and the fix does not
  disturb it; noting it in the class doc is enough.

None of the three needs an ADR; all three are recorded so a reviewer can see
they were considered.
