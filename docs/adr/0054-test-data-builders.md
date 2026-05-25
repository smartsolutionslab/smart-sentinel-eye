# ADR-0054: Test Data — Hand-Written Fluent Builders

**Status:** Accepted
**Date:** 2026-05-25

## Context

Maximalist value objects (ADR-0038) mean test construction calls are
long. Tests should describe scenarios, not recapitulate every
constructor parameter the production code happens to require.

## Decision

Each aggregate, value object with non-trivial constructor, and request
DTO gets a **hand-written fluent builder** in the test project under
`Builders/`. Builders carry sensible defaults; tests override only the
fields relevant to the scenario.

```csharp
// tests/.../Builders/CameraBuilder.cs
public sealed class CameraBuilder
{
    private CameraName _name = CameraName.From("Cam-Default");
    private RtspUrl _url = RtspUrl.From("rtsp://10.0.0.1/stream");
    private Option<OnvifProfileToken> _profile = Option<OnvifProfileToken>.None;

    public CameraBuilder WithName(string name) { _name = CameraName.From(name); return this; }
    public CameraBuilder WithUrl(string url) { _url = RtspUrl.From(url); return this; }
    public CameraBuilder WithProfile(string p) {
        _profile = Option<OnvifProfileToken>.Some(OnvifProfileToken.From(p));
        return this;
    }
    public Camera Build() => Camera.Register(_name, _url, _profile).Value;
}

// in a test
var cam = new CameraBuilder()
    .WithName("Line-1-Entrance")
    .Build();
```

- **No AutoFixture.** Builders are explicit; failures are predictable;
  refactors are local.
- Builders live alongside the tests that need them; if multiple test
  projects need the same builder, promote it to a shared
  `Tests.Common` project.

## Consequences

- **Positive:** tests read declaratively — `new CameraBuilder().WithName(...).Build()`
  conveys exactly what's varied.
- **Positive:** when an aggregate constructor signature changes, only
  builders break — tests don't.
- **Negative:** small upfront cost per aggregate to write the builder.

## Alternatives Considered

- **AutoFixture** — reflection-based generation. Test failures
  become magical when types evolve. Customisations sprawl.
- **`new Camera(...)` everywhere** — every signature change cascades
  through the suite.
- **Mix** (AutoFixture for primitives, builders for aggregates) — two
  patterns to remember.
