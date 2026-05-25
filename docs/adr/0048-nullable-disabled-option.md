# ADR-0048: Null Handling — NRT Disabled, Option&lt;T&gt; Everywhere

**Status:** Accepted (confirmed against Yumney divergence by ADR-0056)
**Date:** 2026-05-25

## Context

.NET 10 ships with Nullable Reference Types enabled by default. Most
NuGet packages and the BCL assume that. The alternative is the
functional-purist style: every potentially-absent value wears an
`Option<T>` wrapper.

## Decision

**Disable NRT at the solution level**
(`<Nullable>disable</Nullable>` in `Directory.Build.props`) and
**use `Option<T>` everywhere** for missing values.

```csharp
// Shared.Kernel/Option.cs
public readonly record struct Option<T>
{
    public bool HasValue { get; }
    private readonly T _value;
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException(...);

    public static Option<T> Some(T v) => new(v, true);
    public static Option<T> None { get; } = new(default!, false);
    // ... Map, FlatMap, Match, GetOrDefault ...
}
```

- Domain absences (`Camera.BoundOperator`,
  `Layout.AssignedToKioskId`) use `Option<T>`.
- Repository lookups return `Option<Aggregate>`.
- Pattern matching: `option.Match(some: v => ..., none: () => ...)`.
- Framework boundaries wrap or unwrap explicitly.

## Consequences

- **Positive:** absence is a domain concept with semantics
  (`OperatorId.None` vs `OperatorId.Some(x)`), not a null sentinel.
- **Positive:** pattern matching is exhaustive; no `?.` silently
  short-circuiting through a long chain.
- **Negative:** friction at every NuGet boundary — BCL null
  annotations are ignored. Explicit wrapping at every framework seam.
- **Negative:** diverges from Yumney and from the .NET ecosystem
  default. Acknowledged trade-off for consistency across this
  codebase.

## Alternatives Considered

- **NRT enabled + Option<T> for explicit domain absence** (middle
  ground) — considered, rejected in favour of consistency.
- **NRT enabled, no Option<T>** (Yumney + .NET default) —
  rejected in favour of the explicit-absence semantic.
- **Option<T> only, NRT disabled** = this ADR.
