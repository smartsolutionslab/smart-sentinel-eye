# ADR-0046: Value Object Code Generation — Hand-Written with Strong Base Infrastructure

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0038 commits us to maximalist value objects — 150–300 types across
the 9 contexts. Source generators (Vogen, StronglyTypedId) would
generate the boilerplate for free. Hand-writing every type produces
significant repetition.

## Decision

**Hand-written VOs, with heavy investment in shared base infrastructure**
in `Shared.Kernel/Primitives/`.

- No source-generator dependency (no Vogen, no StronglyTypedId).
- Base patterns provided as small `record struct` / `record` shapes for
  the common backing types: string-backed, Guid-backed, decimal-backed,
  int-backed, enum-backed.
- Each concrete VO becomes a short partial type with its validator,
  using the `Ensure.That(...)` chain (ADR-0059).
- Common cross-cutting wiring (JSON converters, EF/Marten value
  converters, OpenAPI schema generation) is registered once over
  `IValueObject<T>` (ADR-0066) and applies to every VO.

```csharp
// Shared.Kernel/Primitives/StringValueObject.cs
public abstract record StringValueObject(string Value)
    : IValueObject<string>
{
    public override string ToString() => Value;
}

// CameraCatalog.Domain/CameraName.cs
public sealed record CameraName : StringValueObject
{
    public const int MaxLength = 200;
    private CameraName(string value) : base(value) { }
    public static CameraName From(string value) =>
        new(Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(MaxLength).AndReturn().Trim());
}
```

## Consequences

- **Positive:** zero external code-gen dependency; full control over
  every VO's shape and validation.
- **Positive:** strong base infrastructure keeps each concrete VO at
  ~5–8 lines.
- **Negative:** higher total line count than a Vogen-generated codebase.
  Acknowledged trade-off.
- **Negative:** must maintain the base infrastructure ourselves.

## Alternatives Considered

- **Vogen source generator** — least code, zero runtime cost. Rejected:
  external dependency on a single-maintainer project at this scale.
- **StronglyTypedId for IDs only + Vogen for the rest** — two tools
  to learn; mixed style.
- **No abstraction, copy-paste each VO** — even higher line count
  without the consistency.
