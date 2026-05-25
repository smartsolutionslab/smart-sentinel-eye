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
