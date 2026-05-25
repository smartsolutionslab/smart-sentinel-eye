# ADR-0053: Test Naming — Sentence-Style with Underscores

**Status:** Accepted (confirmed against Yumney divergence by ADR-0062)
**Date:** 2026-05-25

## Context

Test method names are the first thing reviewers read. They double as
behaviour specifications when the test suite is enumerated.

## Decision

Test method names read as **full English sentences with underscores
between words**.

```csharp
public class CameraRegistrationTests
{
    [Fact]
    public void Register_a_camera_with_valid_input_returns_the_new_id() { ... }

    [Fact]
    public void Register_a_camera_with_a_duplicate_name_returns_a_conflict() { ... }

    [Theory, MemberData(nameof(InvalidUrls))]
    public void Register_a_camera_rejects_an_invalid_RTSP_URL(string url) { ... }
}
```

- One test class per aggregate or per behaviour cluster.
- Methods describe scenario and expected outcome.
- Generated test output reads as a behavioural specification:

```
Register a camera with valid input returns the new id     [PASS]
Register a camera with a duplicate name returns a conflict [PASS]
Register a camera rejects an invalid RTSP URL              [PASS]
```

## Consequences

- **Positive:** the test suite is itself a living spec.
- **Positive:** reviewers know what a failure means without reading
  the test body.
- **Negative:** method names are long. Acceptable.

## Alternatives Considered

- **`Method_Scenario_Expected`** (Yumney's choice) — terser, less
  readable as prose.
- **`Should_X_When_Y`** (BDD-style) — verbose and slightly stilted.
- **Nested classes by scenario** — strongest organisation; more files
  per behaviour cluster.
