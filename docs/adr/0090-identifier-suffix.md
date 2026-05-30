# ADR-0090: Supersedes ADR-0039 — ID Value Objects Suffix is "Identifier"

**Status:** Accepted (supersedes ADR-0039)
**Date:** 2026-05-25

## Context

ADR-0039 originally used the `Id` suffix on aggregate identifier
types (e.g. `CameraId`, `LayoutId`). Yumney uses the **`Identifier`**
suffix (`RecipeIdentifier`, `OwnerIdentifier`, `IngredientIdentifier`).
The longer form pairs with ADR-0091 (no shortcuts) and reads more
clearly at call sites.

## Decision

**Every ID value object ends in `Identifier`.**

```csharp
public readonly record struct CameraIdentifier(Guid Value)
    : IValueObject<Guid>
{
    public static CameraIdentifier New() => new(Guid.CreateVersion7());
    public static CameraIdentifier From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

Applies to:

- Every aggregate root identifier: `CameraIdentifier`,
  `LayoutIdentifier`, `OverlayIdentifier`, `RuleIdentifier`, etc.
- Every reference to another aggregate's identifier: `OperatorIdentifier`,
  `KioskIdentifier`, `OwnerIdentifier`.
- Every entity identifier inside an aggregate: `CellIdentifier`,
  `OverlayPrimitiveIdentifier`.

Other ADR-0039 properties carry over: Guid v7 backing, idempotent
generation, Postgres `uuid` column, `IValueObject<Guid>` marker.

## Consequences

- **Positive:** consistent with Yumney; `git grep Identifier` finds
  every ID type.
- **Positive:** reads better at call sites: `OwnerIdentifier owner`
  reads as "owner identifier"; `OwnerId owner` reads as "owner id".
- **Negative:** longer type name. Trade-off accepted.

## Implementation Notes

- Pairs with ADR-0094 (property and variable naming for identifier-
  typed members uses the noun, not the suffix).
- Pairs with ADR-0091 (no shortcuts).

## Amendment (2026-05-30): implicit primitive operator + comparability

Identifier value objects are persisted through an EF Core value
converter (`HasConversion(id => id.Value, v => X.From(v))`), which maps
the whole VO to a single scalar column. This has a sharp edge in LINQ
read queries: **member access on the converted CLR type does not
translate**. `source.Where(e => e.Id.Value == g)` or
`OrderBy(e => e.Id.Value)` throws *"could not be translated"* at
execution time, because EF cannot decompose `.Value` of a converted
property back to the column. Comparing the value object itself does
translate (`e.Id == identifier`, `OrderBy(e => e.Id)`).

To make sortable, range-comparable, and ordered read queries express
naturally **and** translate, every Guid-backed identifier VO carries:

```csharp
public readonly record struct CameraIdentifier(Guid Value)
    : IStronglyTypedId<Guid>, IComparable<CameraIdentifier>
{
    // unwrap so EF orders/compares on the underlying column, and so
    // callers can pass the id where a Guid is expected
    public static implicit operator Guid(CameraIdentifier id) => id.Value;

    // Guid v7 sorts in insert order; keep in-memory sorts == SQL ORDER BY
    public int CompareTo(CameraIdentifier other) => Value.CompareTo(other.Value);
    public static bool operator <(CameraIdentifier l, CameraIdentifier r) => l.CompareTo(r) < 0;
    public static bool operator <=(CameraIdentifier l, CameraIdentifier r) => l.CompareTo(r) <= 0;
    public static bool operator >(CameraIdentifier l, CameraIdentifier r) => l.CompareTo(r) > 0;
    public static bool operator >=(CameraIdentifier l, CameraIdentifier r) => l.CompareTo(r) >= 0;
}
```

`IComparable<T>` without the four comparison operators trips CA1036, so
they travel together.

This convention extends to **non-identifier scalar value objects that
appear in EF read-query predicates or ordering** — e.g. the
EventIngestion `OccurredAt` / `IngestedAt` time VOs carry an implicit
`operator DateTimeOffset` for the same reason. (Identifiers compared
only for equality, such as the string-backed `FabIdentifier` /
`DeviceIdentifier` or the older `IValueObject`-shaped audit
`ActorIdentifier` / `EventIdentifier`, do not strictly need the
operator — equality on a converted property already translates — and
are left unchanged until a query needs them.)

Read handlers must therefore compare value objects, never their
`.Value`, inside `IQueryable` expressions; `.Value` is fine on
already-materialised rows (after `ToListAsync`). The EventIngestion
read path has an offline `ToQueryString` translation test
(`ListEventsTranslationTests`) guarding this.
