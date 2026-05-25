# ADR-0094: Property and Variable Naming for Identifier-Typed Members

**Status:** Accepted
**Date:** 2026-05-25

## Context

Once `Identifier` is the canonical suffix on ID value objects
(ADR-0090), repeating it in every property and variable name
(`OwnerIdentifier OwnerIdentifier`, `CameraIdentifier cameraIdentifier`)
is noisy and obscures the call site. Yumney's pattern names the
member after the **noun the identifier refers to**.

## Decision

Properties and variables holding an `Identifier` value object are
named after the **noun the identifier refers to**, not after the
type or the suffix.

### Examples

```csharp
public sealed class Camera : AggregateRoot<CameraIdentifier>
{
    public OwnerIdentifier Owner { get; private set; }       // NOT OwnerId, NOT OwnerIdentifier
    public KioskIdentifier? BoundKiosk { get; private set; } // NOT BoundKioskId
}

public Task<Result<CameraIdentifier, RegisterCameraError>> HandleAsync(
    RegisterCameraCommand command,
    CancellationToken ct)
{
    var camera = CameraIdentifier.New();   // NOT cameraId, NOT cameraIdentifier
    var owner = currentUser.Identifier;    // NOT ownerId
    return ...;
}

public void Decommission(OperatorIdentifier decommissioner) { ... }
//                       ^^^ NOT decommissionerId, NOT operatorId
```

### Reads at the call site

- `camera.Owner` — "the camera's owner" (semantic, not technical)
- `await repository.GetByIdentifierAsync(camera, ct)` — `camera` is
  the noun; pass the identifier value.
- `command with { Owner = newOwner }` — the property is what the
  identifier represents.

### Conflicts within a scope

Two `Camera`-typed locals in the same method? Disambiguate by **role**,
not by reverting to `Id`:

```csharp
var primaryCamera = ...;
var fallbackCamera = ...;
var source = ...;       // when the noun is intuitively unambiguous
var destination = ...;
```

### When a member holds the aggregate itself (not the identifier)

Same rule: name after the noun (e.g. `Camera camera`, `Layout layout`).
The type tells you whether it's the entity or just the identifier;
the variable name describes the role.

## Consequences

- **Positive:** call sites read as plain English. `camera.Owner.Equals(other)`
  beats `camera.OwnerId.Equals(other.OwnerId)`.
- **Positive:** matches Yumney.
- **Negative:** when reading a method signature, you must look at
  the type, not the parameter name, to know whether you're holding
  the aggregate or just its identifier. Tooling (hover, IDE) handles
  this trivially; commenters need to be aware.
- **Negative:** an existing codebase migrating from `OwnerId` to
  `Owner` is a wide refactor. We're greenfield, so cost is zero.

## Alternatives Considered

- **Always `<Noun>Identifier`** (`OwnerIdentifier OwnerIdentifier`) —
  noisy.
- **Always `<Noun>Id`** — depends on `Id` suffix which ADR-0090
  rejects.
- **Mix per scope** — inconsistent.
