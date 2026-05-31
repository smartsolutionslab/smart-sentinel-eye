# ADR-0050: Logging — `ILogger<T>` + OpenTelemetry OTLP (Serilog not adopted)

**Status:** **Amended 2026-05-31** — implemented MEL-native; Serilog was not adopted (see Addendum)
**Date:** 2026-05-25

## Addendum (2026-05-31) — as-built: MEL-native, no Serilog

The original decision below (Serilog behind `ILogger<T>`) was **not
implemented**. The codebase uses the **Microsoft.Extensions.Logging
provider with OpenTelemetry** — `builder.Logging.AddOpenTelemetry(...)`
plus `UseOtlpExporter()` in `ServiceDefaults` — which is exactly the
"native, no Serilog" path this ADR had listed under *Alternatives
Considered*. There is no `UseSerilog`, no `Serilog.Sinks.OpenTelemetry`,
and no Serilog enrichers anywhere in the tree.

**Why the as-built path stands:**

- It is the Aspire-native default and needs no extra dependency.
- OTLP log export is batched off the request/ingest threads by default.
- Structured-logging discipline is preserved and now enforced at the
  source level via **`[LoggerMessage]` source generators** (a MEL
  feature) per project — strongly-typed, allocation-free, level-gated
  log methods. This supersedes the Serilog-analyzer enforcement the
  original decision relied on; the "no string interpolation /
  structured fields only" rule still holds.
- Trace/span correlation comes from the OpenTelemetry logging provider
  (`IncludeScopes` + activity correlation), not Serilog enrichers.

The remainder of this ADR is retained for historical context; treat the
Serilog-specific mechanics (UseSerilog, sinks, enrichers,
Serilog.Analyzers) as **not in effect**.

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
