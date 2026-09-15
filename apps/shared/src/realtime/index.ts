// Realtime channel abstraction per ADR-0076.
// v1 implementation = native WebSocket.
// v2 candidate = Server-Sent Events; pluggable behind this interface.

export type RealtimeMessage = {
  readonly type: string;
  readonly payload: unknown;
  readonly traceId: string;
  readonly ts: string;
};

export type RealtimeSubscription = {
  readonly close: () => void;
};

export interface RealtimeClient {
  connect(): Promise<void>;
  subscribe(topic: string, handler: (message: RealtimeMessage) => void): RealtimeSubscription;
  disconnect(): void;
}

// Adoption gap, recorded rather than rediscovered (spec 162 §1.8 / #2397):
// nothing implements RealtimeClient. Zero implementors, zero consumers — of
// the seven references to `shared/realtime/layoutHub` (the SignalR client),
// one is a production import site
// (apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts) and six are
// test references; layoutHub's `start`/`stop`/`state` surface does not
// satisfy connect/subscribe/disconnect and never claimed to.
// `src/Realtime.Abstractions/` holds zero `.cs` files. Whether this
// interface should survive is tracked by #2400 and is not decided here.
//
// The interface's member set is pinned against silent drift — see
// surface.pin.ts, kept as a sibling file so the pin's text does not change
// merely because this one does.
