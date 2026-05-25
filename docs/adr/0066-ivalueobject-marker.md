# ADR-0066: IValueObject Marker Interface

**Status:** Accepted
**Date:** 2026-05-25

## Context

Maximalist VOs (ADR-0038) means cross-cutting wiring (JSON converter,
EF/Marten value converter, OpenAPI schema generator) needs to apply
to every value object uniformly. A marker interface gives a single
hook for that wiring.

## Decision

`Shared.Kernel` defines two marker interfaces (Yumney pattern):

```csharp
public interface IValueObject;

public interface IValueObject<TValue> : IValueObject
{
    TValue Value { get; }
}
```

- **Every value object implements one of them.**
- Wiring registers once over all `IValueObject<T>` implementations:
  - System.Text.Json converter (one converter per backing type).
  - EF Core / Marten value converters.
  - OpenAPI schema generator (maps to the backing primitive type).
- Generic constraints like `where T : IValueObject<string>` become
  expressible in shared infrastructure code.

## Consequences

- **Positive:** one-time registration covers every current and
  future VO.
- **Positive:** `git grep IValueObject<` enumerates the entire
  domain vocabulary.
- **Negative:** tiny additional interface inheritance per VO.

## Alternatives Considered

- **No marker** — every cross-cutting wiring duplicates type
  enumeration.
- **Attribute (`[ValueObject]`)** — reflection-based; weaker than
  static-type-based.
