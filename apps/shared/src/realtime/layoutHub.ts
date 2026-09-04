import { HubConnectionBuilder, type HubConnection, HubConnectionState, type IRetryPolicy } from '@microsoft/signalr';
import { logResilienceEvent } from '../observability/resilienceLog.js';
import { layoutHubUrl } from './hubUrl.js';

/**
 * Lean lifecycle frame (spec 010, ADR-0112 §3): no tile set — the picker
 * re-queries `GET /layouts?state=published` on receipt. The published
 * tiles ride the `LayoutRevisionPublishedV2` integration event, not this
 * SignalR frame.
 */
export interface LayoutRevisionPublishedMessage {
  layout: string;
  revisionNumber: number;
  name: string;
  publishedAt: string;
}

export interface LayoutRevisionArchivedMessage {
  layout: string;
  revisionNumber: number;
  archivedAt: string;
}

/**
 * Wire shape for overlay-revision-published SignalR frames (spec 004
 * PR C broadcaster bridge). The backend reuses the LayoutLifecycle hub
 * so kiosks subscribe to overlay updates over the same connection.
 */
export interface OverlayRevisionPublishedMessage {
  overlay: string;
  revisionNumber: number;
  name: string;
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
  publishedAt: string;
}

export interface OverlayRevisionArchivedMessage {
  overlay: string;
  revisionNumber: number;
  archivedAt: string;
}

/**
 * Wire shape for resolved-overlay-text SignalR frames (spec 005
 * FR-013). Pushed when a referenced system variable changes value,
 * gets archived, or the overlay itself republishes. `version` is a
 * monotonic per-overlay counter so the kiosk can discard out-of-order
 * frames.
 *
 * `fab` names the plant the frame belongs to (spec 067). A connection holding
 * two fabs joins both groups and correctly receives both plants' frames; only
 * the client knows which wall it is showing, so only the client can refuse
 * what is not its own (ADR-0145).
 */
export interface ResolvedOverlayTextChangedMessage {
  overlay: string;
  fab: string;
  resolvedText: string;
  version: number;
}

/**
 * Wire shape for overlay-highlight SignalR frames (spec 007 / spec 010
 * US3). Pushed when an Automation rule's HighlightOverlay action fires.
 * The kiosk applies the `ssE-overlay-highlight` class to *every* tile
 * bound to `overlay` for `durationMs` ms, then auto-reverts (overlay
 * reuse → highlight-all-matching, ADR-0112 §5).
 *
 * `fab` names the plant whose rule fired (spec 067), for the reason above.
 */
export interface OverlayHighlightChangedMessage {
  overlay: string;
  fab: string;
  durationMs: number;
}

export type LayoutHubConnectionState = 'connecting' | 'connected' | 'degraded';

export interface LayoutHubCallbacks {
  onPublished?: (message: LayoutRevisionPublishedMessage) => void;
  onArchived?: (message: LayoutRevisionArchivedMessage) => void;
  onOverlayPublished?: (message: OverlayRevisionPublishedMessage) => void;
  onOverlayArchived?: (message: OverlayRevisionArchivedMessage) => void;
  onResolvedOverlayTextChanged?: (message: ResolvedOverlayTextChangedMessage) => void;
  onOverlayHighlightChanged?: (message: OverlayHighlightChangedMessage) => void;
  /** Fires on every recovery — SignalR auto-reconnect AND manual restart. */
  onReconnected?: () => void;
  /** Fires on every connection-state transition, incl. the initial connect. */
  onStateChange?: (state: LayoutHubConnectionState) => void;
}

export interface LayoutHubConfig {
  /** Override for tests; defaults to the deploy-time resolved hub endpoint. */
  hubUrl?: string;
  accessTokenFactory: () => string | Promise<string>;
}

// Unbounded reconnect ladder (spec 011 FR-006, research R4): 0/2/5/10/30 s,
// then every 30 s. Full ±20% jitter keeps 20 kiosks (and their restart loops)
// from synchronizing reconnects after a backend restart (SC-005).
const RETRY_LADDER_MS: readonly number[] = [0, 2_000, 5_000, 10_000, 30_000];

function jitteredRetryDelayMs(previousRetryCount: number): number {
  const base = RETRY_LADDER_MS[Math.min(previousRetryCount, RETRY_LADDER_MS.length - 1)] ?? 30_000;
  return Math.round(base * (0.8 + Math.random() * 0.4));
}

// Never returns null — a permanent give-up state is forbidden (FR-006).
const unboundedRetryPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (context) => jitteredRetryDelayMs(context.previousRetryCount),
};

/**
 * Thin SignalR client wrapper for the LayoutLifecycle hub
 * (spec 003 FR-009). Hides the @microsoft/signalr surface behind a
 * focused callback API so the kiosk pages only deal with typed events.
 *
 * Resilience (spec 011 US2): reconnects are unbounded via the jittered
 * ladder, `start()` owns an initial-connect retry loop on the same ladder,
 * and server-initiated closes reschedule a restart. `onReconnected` fires
 * after every recovery so the caller can reconcile missed events;
 * `onStateChange` surfaces connected/degraded for the UI badge.
 */
export function createLayoutHubClient(config: LayoutHubConfig, callbacks: LayoutHubCallbacks): LayoutHubHandle {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(config.hubUrl ?? layoutHubUrl, {
      accessTokenFactory: () => Promise.resolve(config.accessTokenFactory()),
    })
    .withAutomaticReconnect(unboundedRetryPolicy)
    .build();

  registerMessageHandlers(connection, callbacks);
  return createResilientHandle(connection, callbacks);
}

function registerMessageHandlers(connection: HubConnection, callbacks: LayoutHubCallbacks): void {
  if (callbacks.onPublished !== undefined) {
    connection.on('LayoutRevisionPublished', callbacks.onPublished);
  }
  if (callbacks.onArchived !== undefined) {
    connection.on('LayoutRevisionArchived', callbacks.onArchived);
  }
  if (callbacks.onOverlayPublished !== undefined) {
    connection.on('OverlayRevisionPublished', callbacks.onOverlayPublished);
  }
  if (callbacks.onOverlayArchived !== undefined) {
    connection.on('OverlayRevisionArchived', callbacks.onOverlayArchived);
  }
  if (callbacks.onResolvedOverlayTextChanged !== undefined) {
    connection.on('ResolvedOverlayTextChanged', callbacks.onResolvedOverlayTextChanged);
  }
  if (callbacks.onOverlayHighlightChanged !== undefined) {
    connection.on('OverlayHighlightChanged', callbacks.onOverlayHighlightChanged);
  }
}

/**
 * The recovery lifecycle around the raw connection. SignalR's automatic
 * reconnect only covers drops of an ESTABLISHED connection; the manual
 * restart loop here covers the two paths it does not — initial-connect
 * failures and server-initiated closes (`onclose`). `stop()` is the only
 * way out of the loop.
 */
function createResilientHandle(connection: HubConnection, callbacks: LayoutHubCallbacks): LayoutHubHandle {
  let lastState: LayoutHubConnectionState | undefined;
  let stopped = false;
  let restartAttempt = 0;
  let restartTimer: ReturnType<typeof setTimeout> | undefined;

  const emitState = (state: LayoutHubConnectionState): void => {
    if (state === lastState) {
      return;
    }
    logResilienceEvent('hub', `${lastState ?? 'idle'}→${state}`);
    lastState = state;
    callbacks.onStateChange?.(state);
  };

  const scheduleRestart = (): void => {
    if (stopped || restartTimer !== undefined) {
      return;
    }
    restartTimer = setTimeout(() => {
      restartTimer = undefined;
      void tryStart();
    }, jitteredRetryDelayMs(restartAttempt));
    restartAttempt += 1;
  };

  const tryStart = async (): Promise<void> => {
    if (stopped) {
      return;
    }
    try {
      await connection.start();
    } catch {
      emitState('degraded');
      scheduleRestart();
      return;
    }
    restartAttempt = 0;
    const recovering = lastState === 'degraded';
    emitState('connected');
    if (recovering) {
      callbacks.onReconnected?.();
    }
  };

  connection.onreconnecting(() => emitState('degraded'));
  connection.onreconnected(() => {
    restartAttempt = 0;
    emitState('connected');
    callbacks.onReconnected?.();
  });
  connection.onclose(() => {
    if (stopped) {
      return;
    }
    emitState('degraded');
    scheduleRestart();
  });

  return {
    start: async () => {
      stopped = false;
      emitState('connecting');
      await tryStart();
    },
    stop: async () => {
      stopped = true;
      if (restartTimer !== undefined) {
        clearTimeout(restartTimer);
        restartTimer = undefined;
      }
      if (connection.state !== HubConnectionState.Disconnected) {
        await connection.stop();
      }
    },
    state: () => connection.state,
  };
}

export interface LayoutHubHandle {
  /**
   * Resolves once the connection is started or a retry is scheduled; the
   * internal ladder owns initial-start failures and post-close restarts,
   * so a caller never needs its own retry loop (FR-006).
   */
  start: () => Promise<void>;
  /** Cancels all pending retries; no restart fires after this resolves. */
  stop: () => Promise<void>;
  state: () => HubConnectionState;
}
