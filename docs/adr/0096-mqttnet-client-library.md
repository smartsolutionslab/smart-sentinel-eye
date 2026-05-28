# ADR-0096: MQTTnet as the .NET MQTT client library

**Status:** Accepted
**Date:** 2026-05-28
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0095 selects Mosquitto as the MQTT broker. EventIngestion
(spec 006) needs a .NET client to subscribe to the wildcard
`fab/+/+/+` topic and feed payloads into the bounded ingest
channel. The client lib is the second tech-stack addition for
spec 006 and so requires its own ADR per constitution §II.

Constraints:

- .NET 10 / `net10.0` target framework.
- Long-lived persistent session (QoS 1 + clean session = false).
- TLS-optional connection (TLS in prod, plaintext in dev).
- Async API that integrates with `IHostedService` + `Channel<T>`
  backpressure (we need an `await`able publish + a way to
  *defer* MQTT ACK until our persistence loop has succeeded).
- Active maintenance + a clear release cadence (the broker is on
  the critical path for the camera → event → overlay loop).
- MIT or equivalent permissive license (per constitution §II
  preference for permissive OSS in the data plane).

## Decision

**MQTTnet 4.3.x** (https://github.com/dotnet/MQTTnet) is the
.NET MQTT client library for EventIngestion.

- Released under MIT.
- Most-downloaded .NET MQTT lib on nuget.org by an order of
  magnitude.
- Maintained under the `dotnet` GitHub org (Microsoft adopted
  the project in 2024).
- v4 API surface is async-first and integrates cleanly with
  `IHostedService` lifecycles + `CancellationToken` chains
  (matches ADR-0049).
- `MQTTnet.AspNetCore` adds DI helpers that map naturally onto
  our per-context `Add<Context>Infrastructure()` extension method
  pattern (ADR-0051).

## Consequences

**Positive:**

- Battle-tested at scale (tens of thousands of GitHub-deps;
  used in production by IoT-platform vendors and industrial
  control systems).
- v5 of the MQTT spec is supported (we use v3.1.1 / v5
  capability set in v1 but room to grow).
- Async API maps naturally to our handler pattern.
- Connection-state observable so we can wire health checks /
  Aspire dashboard surfacing of the broker connection state.
- Per-message `ApplicationMessageReceivedAsync` callback lets
  us defer the ACK by awaiting our persistence loop — exactly
  what spec 006 §FR-022 needs for the "MQTT subscriber stops
  ACKing when channel is full" backpressure model.

**Negative:**

- Documentation is thinner than mainstream Microsoft libs;
  pattern discovery via the GitHub examples folder rather than
  rich docs.
- The library carries a small set of features beyond what we need
  (in-process broker, WebSocket transport) — we accept the
  unused surface; the assembly is small (~700 KB).

## Alternatives Considered

**M2Mqtt — REJECTED.** The original .NET MQTT lib (pre-MQTTnet).
Effectively unmaintained since ~2018. No .NET 10 build, lacks
async-first API.

**HiveMQ .NET client — REJECTED.** Commercial license required
for the production-grade variants; would conflict with
constitution §II's preference for permissive OSS in the data
plane. The community-licensed variant is feature-restricted.

**Raw `System.Net.Sockets` + a hand-rolled MQTT codec — REJECTED.**
Not worth the maintenance burden. MQTT v3.1.1 is non-trivial
(QoS state machines, session storage, keepalive heartbeats);
solving it ourselves would consume engineering bandwidth that
should go to the spec 006 domain work.

## Implementation Notes

- Added as `PackageReference Include="MQTTnet" Version="4.3.x"`
  to `EventIngestion.Infrastructure.csproj` (spec 006 task T011).
- The Aspire AppHost surfaces the broker endpoint as a connection
  string `ConnectionStrings:mosquitto = tcp://host:1883`; the
  `MosquittoConnectionFactory` (task T047) parses it into an
  `MqttClientOptions` builder.
- Reconnect-with-backoff is configured via `WithAutoReconnectDelay`
  on the MQTTnet `ManagedMqttClient` so transient broker outages
  recover without operator intervention (matches NFR-005).
