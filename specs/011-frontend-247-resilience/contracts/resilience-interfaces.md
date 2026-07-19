# Contracts: Frontend 24/7 Resilience

Interface contracts for the extended shared-package surfaces. These are
the seams other code (both apps, tests) programs against; implementations
may not narrow them without a spec change.

## 1. `WhepClient` (apps/shared/src/streaming/WhepClient.ts)

```ts
export interface WhepClientOptions {
  whepUrl: string;
  getToken: () => Promise<string | null>;
  /** NEW — fires on every RTCPeerConnection connectionState change. */
  onConnectionStateChange?: (state: RTCPeerConnectionState) => void;
}

export class WhepClient {
  /**
   * Single-use (unchanged). NEW behaviour:
   * - waits ≤250 ms for ICE gathering completion before POSTing
   * - captures the WHEP `Location` response header as the session URL
   */
  connect(videoEl: HTMLVideoElement, signal?: AbortSignal): Promise<void>;

  /**
   * NEW behaviour: if a session URL was captured, fire-and-forget
   * `DELETE <Location>` (current bearer, keepalive) BEFORE local
   * teardown. Local teardown always completes; DELETE outcome is
   * ignored. Must be safe to call multiple times and mid-connect.
   */
  close(): void;
}
```

**Contract tests**: state-change callback fires for `connected`/`failed`;
`close()` issues DELETE exactly once with the captured URL; `close()`
without a Location performs local teardown only; abort-mid-connect never
leaves a live PC.

## 2. Layout hub (apps/shared/src/realtime/layoutHub.ts)

```ts
export type LayoutHubConnectionState = 'connecting' | 'connected' | 'degraded';

export interface LayoutHubCallbacks {
  // …existing message callbacks unchanged…
  onReconnected?: () => void;
  /** NEW — every connection-state transition, incl. initial connect. */
  onStateChange?: (state: LayoutHubConnectionState) => void;
}

export interface LayoutHubHandle {
  /**
   * CHANGED semantics: resolves when the connection is STARTED-or-
   * SCHEDULED; internal retry (unbounded ladder 0/2/5/10/30 s ±20%
   * jitter) owns both initial-start failures and post-`onclose`
   * restarts. `start()` never leaves the connection in a permanent
   * give-up state.
   */
  start: () => Promise<void>;
  stop: () => Promise<void>;   // cancels all pending retries
  state: () => HubConnectionState;
}
```

`LayoutHubHandle` remains the ADR-0076 transport seam: consumers depend
only on this module's exports, never on `@microsoft/signalr` types
beyond re-exported ones.

**Contract tests**: retry policy never returns null; `onclose` schedules
a restart; `stop()` during a pending retry cancels it; `onStateChange`
sequence for connect → drop → reconnect is
`connecting, connected, degraded, connected`.

## 3. `useLayoutLifecycle` (apps/kiosk-web)

```ts
export interface UseLayoutLifecycleResult {
  /** true whenever the hub is not currently connected (FR-007). */
  degraded: boolean;
}
export function useLayoutLifecycle(options: UseLayoutLifecycleOptions): UseLayoutLifecycleResult;
```

Reconciliation contract (on every reconnect): invalidates
`{LayoutList,'ALL'}`, `{OverlayList,'ALL'}`, `{OverlaySnapshot,'ALL'}`
(now matched — see §5), and the bare `Overlay` type.

## 4. Gateway auth (apps/shared/src/api/gateway.ts)

```ts
/** Unchanged. */
export const setAccessTokenProvider: (p: () => string | undefined) => void;

/** NEW — app registers its silent-renew action; resolves true on success. */
export const setSessionRenewer: (renew: () => Promise<boolean>) => void;

/** NEW — app registers the escalation for unrecoverable expiry. */
export const setOnSessionExpired: (handler: () => void) => void;

/**
 * CHANGED: gatewayBaseQuery now wraps fetchBaseQuery with reauth:
 * 401 → await renewer() once → retry original request once →
 * still-401/renewer-false → invoke onSessionExpired and return the 401.
 * Non-401 results pass through untouched. Concurrent 401s share ONE
 * in-flight renewal (mutex) — no renewal stampede.
 */
export const gatewayBaseQuery: (route: string) => BaseQueryFn;
```

## 5. `systemVariables.api.ts` tag contract

`getOverlaySnapshot.providesTags` returns
`[{type:'OverlaySnapshot', id}, {type:'OverlaySnapshot', id:'ALL'}]`.
Every existing `id:'ALL'` invalidation (mutations, reconnect) thereby
becomes effective. No endpoint shapes change.

## 6. Environment configuration (deploy contract)

| Variable | Dev default | Prod behaviour |
|---|---|---|
| `VITE_API_GATEWAY_URL` | same-origin fallback (unchanged) | **required — module-load throw if absent** |
| `VITE_KEYCLOAK_URL` | `http://localhost:8080` fallback (unchanged) | **required — module-load throw if absent** |
| `VITE_LAYOUT_HUB_URL` | `/hubs/layouts` (Vite dev proxy, unchanged) | **required — module-load throw if absent**; deploy layer supplies the absolute hub URL |

"Prod" = `import.meta.env.PROD === true`. The throw message names the
missing variable and the deploy doc section.

## 7. `ErrorBoundary` (apps/shared/src/ui/composites/ErrorBoundary.tsx)

```ts
export interface ErrorBoundaryProps {
  /** Fallback receives the error and a reset callback. */
  fallback: (error: unknown, reset: () => void) => ReactNode;
  /** Called once per caught error (FR-017 logging hook). */
  onError?: (error: unknown) => void;
  children: ReactNode;
}
```

Kiosk composes it with a `KioskCrashRecovery` fallback implementing the
5/15/60 s reload watchdog (counters in `sessionStorage`, cleared after
5 min stable). Management composes a panel + reset fallback.

## 8. Resilience logging (FR-017)

```ts
export type ResilienceSubsystem = 'stream' | 'hub' | 'session' | 'crash';
export function logResilienceEvent(
  subsystem: ResilienceSubsystem,
  transition: string,          // e.g. 'live→reconnecting', 'degraded→connected'
  detail?: Record<string, unknown>,
): void; // structured console.info, stable '[resilience]' prefix
```

Stable prefix + shape is the observable contract (asserted in e2e and
usable in kiosk remote-debug/log capture).
