# ADR-0076: Real-Time Transport — Replaceable Abstraction, WebSocket First

**Status:** Accepted
**Date:** 2026-05-25

## Context

Backend pushes variable changes, event arrivals, and overlay state
updates to connected kiosks and operator workstations. Native
WebSocket is fastest; SSE is simpler and cacheable through proxies.
Both have valid use cases; we want to pick one now without locking
out the other later.

## Decision

**The real-time transport is replaceable behind an abstraction.**

### Server side

A new project `SmartSentinelEye.Realtime.Abstractions` defines:

```csharp
public interface IRealtimeChannel
{
    Task BroadcastAsync(KioskId kiosk, RealtimeMessage message, CancellationToken ct);
    Task BroadcastToSubscribersAsync(string topic, RealtimeMessage message, CancellationToken ct);
}

public interface IRealtimeTransport // per-connection
{
    Task SendAsync(RealtimeMessage message, CancellationToken ct);
    Task<Option<RealtimeMessage>> ReceiveAsync(CancellationToken ct);
}
```

### Client side

`apps/shared/realtime/` exports a parallel TypeScript interface:

```typescript
export interface RealtimeClient {
  subscribe(topic: string, handler: (msg: RealtimeMessage) => void): Unsubscribe;
  connect(): Promise<void>;
  disconnect(): void;
}
```

### v1 implementation

**Native WebSocket** with a typed JSON envelope:

```typescript
type RealtimeMessage = {
  type: string;
  payload: unknown;
  traceId: string;
  ts: string; // ISO
};
```

- Backend `WebSocketRealtimeChannel` subscribes to RabbitMQ on behalf
  of each connected kiosk; filters per kiosk's subscription set;
  forwards over the WebSocket.
- Auth handshake validates the Keycloak access token before promoting
  the HTTP connection to WebSocket.

### v2 candidate

**Server-Sent Events** (`SseRealtimeChannel` + `SseRealtimeClient`)
plugs in behind the same abstraction without changing consumers.

### Out of scope for the abstraction

**Operator commands** (PTZ, switch camera, set overlay) ride a
separate REST endpoint regardless of subscribe transport. The
abstraction covers **server→client push only**.

## Consequences

- **Positive:** transport choice is per-deployment, not per-codebase.
- **Positive:** SSE plug-in is additive — no consumer code changes.
- **Negative:** abstraction layer costs a tiny amount of indirection.
  Acceptable.

## Alternatives Considered

- **WebSocket only, no abstraction** — locks out SSE permanently.
- **SignalR** — auto-reconnect, hubs, groups — but heavier and
  couples to the framework's protocol envelope.
- **MQTT-over-WebSocket** — interesting for OT-native integration;
  diverges from RabbitMQ-everywhere (ADR-0010).
