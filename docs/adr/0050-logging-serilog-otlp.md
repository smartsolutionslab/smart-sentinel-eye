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

### Convention (2026-06-01) — `[LoggerMessage]` catalogs are `ILogger` extension methods

Each project layer that logs owns one **central catalog**: an
`internal static partial class Log` (one per `Application` /
`Infrastructure`, plus `ServiceDefaults` and `MigrationRunner`), marked
`[ExcludeFromCodeCoverage]`, holding every `[LoggerMessage]` method for
that layer with its template, level, and (generated) EventId in one
greppable place.

Each method is an **extension method on `ILogger`** — the first parameter
is `this ILogger logger`. Call sites therefore read as a method on the
injected logger, not a static helper that takes the logger as an argument:

```csharp
// catalog — Infrastructure/Log.cs
[LoggerMessage(Level = LogLevel.Information,
    Message = "Archived audit chunk {ChunkIdentifier} ({RowCount} rows) to {ObjectKey}.")]
public static partial void ArchivedAuditChunk(
    this ILogger logger, Guid chunkIdentifier, int rowCount, string objectKey);

// call site
logger.ArchivedAuditChunk(chunk.ChunkIdentifier, rows.Count, objectKey);   // ✓
Log.ArchivedAuditChunk(logger, chunk.ChunkIdentifier, rows.Count, objectKey);  // ✗ old shape
```

Rules:

- **`this ILogger` first parameter**, always. The generator emits the
  extension method; the call site never passes the logger explicitly.
- **Exception-carrying methods** keep the `Exception` parameter (any
  position after the logger); call as `logger.Foo(ex, …)`.
- **Keep the catalog centralized** — do *not* scatter messages as private
  `partial` methods on individual services (instance-mode
  `[LoggerMessage]`). The per-layer catalog is the point: one inventory of
  messages + EventIds per bounded context layer.
- The structured-fields-only rule (no string interpolation) from the
  Addendum still applies.

This is a call-shape convention within the existing `[LoggerMessage]`
decision — no behavioural change to emitted logs.

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
