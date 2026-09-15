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
// nothing implements RealtimeClient. Zero implementors, zero consumers — all
// seven real import sites use `shared/realtime/layoutHub`, the SignalR client,
// whose `start`/`stop`/`state` surface does not satisfy connect/subscribe/
// disconnect and never claimed to. `src/Realtime.Abstractions/` holds zero
// `.cs` files. Whether this interface should survive is tracked by #2400 and
// is not decided here.

type Exact<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;

// ADR-0076 promises a v2 transport drops in behind this interface without
// changing consumers. Adding or removing a member breaks that promise, so the
// shape is pinned: tsc fails in BOTH directions, which a `keyof`-typed array
// does not (a subset satisfies it). See spec 162 §3.3.
export const realtimeClientSurfaceIsExhaustive: Exact<keyof RealtimeClient, 'connect' | 'subscribe' | 'disconnect'> =
  true;
