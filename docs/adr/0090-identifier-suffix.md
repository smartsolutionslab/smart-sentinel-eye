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
