# ADR-0052: Test Stack — xUnit, Shouldly, Moq, Hand-Written Fakes, Testcontainers

**Status:** Accepted (Shouldly confirmed by ADR-0060; Moq confirmed by ADR-0061)
**Date:** 2026-05-25

## Context

ADR-0033 mandates integration tests via Aspire AppHost + Testcontainers
on every code PR. We need a uniform test stack: framework, assertion
library, mocking strategy.

## Decision

| Concern | Choice |
|---|---|
| Test framework | **xUnit** (the Aspire test template default) |
| Assertions | **Shouldly** — MIT-licensed, fluent, free |
| Mocking | **Moq** for interfaces requiring narrow doubles |
| Stateful collaborators | **Hand-written fakes** (`InMemoryCameraRepository`, etc.) |
| Integration infrastructure | **Testcontainers** (real Postgres, RabbitMQ, Keycloak) via the Aspire fixture pattern (ADR-0068, deferred) |

**Heuristic for choosing a test double:** prefer fakes over mocks
(closer to real behaviour, refactor-friendly); prefer Testcontainers
over fakes when the test cost is low enough to justify it.

```csharp
// hand-written fake
var cameras = new InMemoryCameraRepository();
var bus = new InMemoryMessageBus();
var handler = new RegisterCameraHandler(cameras, bus);

var result = await handler.HandleAsync(cmd, ct);

result.IsSuccess.ShouldBeTrue();
result.Value.ShouldBe(expectedId);
```

## Consequences

- **Positive:** consistent stack across all tests; reviewers know what
  to expect.
- **Positive:** Shouldly avoids the FluentAssertions licensing cost.
- **Negative:** Moq had a 2024 SponsorLink controversy (reverted).
  Acknowledged; team is comfortable with the trade-off.
- **Negative:** Diverges from Yumney's NSubstitute + FluentAssertions
  pair; some context-switch cost.

## Alternatives Considered

- **FluentAssertions** — now commercial; rejected.
- **NSubstitute (Yumney's pick)** — equally fine technically; we
  chose to keep our prior pick.
- **AwesomeAssertions** (FA community fork) — newer; long-term
  maintenance unknown.
- **No mocking library, fakes only** — pure but more test code.
