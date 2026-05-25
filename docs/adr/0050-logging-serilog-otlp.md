# ADR-0050: Logging — Serilog behind ILogger&lt;T&gt;, OTLP Exporter

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0026 commits us to OpenTelemetry-instrumented services exporting
OTLP to both the Aspire dashboard and a Grafana stack. We need a
concrete logging library and a uniform structured-logging shape.

## Decision

Use **Serilog as the concrete logger, behind `ILogger<T>` from
`Microsoft.Extensions.Logging`**.

- Application code injects and uses `ILogger<T>` — the framework
  abstraction. **No direct `Log.ForContext<T>()` calls**.
- Serilog provides the implementation via
  `builder.Host.UseSerilog(...)` in each Api host's `Program.cs`.
- **Mandatory enrichers** (configured in `ServiceDefaults`):
  - `TraceId` and `SpanId` from the active OpenTelemetry activity.
  - `ServiceName` (Aspire resource name).
  - `BoundedContext` (constant per project).
  - `Environment` (development / staging / production).
- **Output:** JSON in production via `Serilog.Sinks.OpenTelemetry`
  shipping to the OTel collector → Aspire dashboard + Loki.
- **Structured fields only** — no string interpolation in log
  messages. Roslyn analyzer (`Serilog.Analyzers`) enforces.

```csharp
private readonly ILogger<RegisterCameraHandler> _log;

_log.LogInformation(
    "Registered camera {CameraId} with name {CameraName}",
    cameraId, name);  // structured fields, not $"...{cameraId}..."
```

## Consequences

- **Positive:** logs are first-class structured data, queryable in
  Grafana Loki by field.
- **Positive:** correlation with traces is automatic via TraceId
  enricher.
- **Negative:** developers must remember to use placeholders, not
  string interpolation. Analyzer catches.

## Alternatives Considered

- **Microsoft.Extensions.Logging native (no Serilog)** — Aspire's
  default. Smaller enricher ecosystem. Acceptable but Serilog buys
  meaningful capability for marginal cost.
- **Serilog with `Log.ForContext<T>()`** — couples to Serilog API;
  loses framework abstraction benefit.
