import { useEffect, useRef, useState } from 'react';
import { useDispatch } from 'react-redux';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import {
  createLayoutHubClient,
  type LayoutRevisionArchivedMessage,
  type LayoutRevisionPublishedMessage,
  type OverlayHighlightChangedMessage,
  type OverlayRevisionArchivedMessage,
  type OverlayRevisionPublishedMessage,
  type ResolvedOverlayTextChangedMessage,
} from '@smart-sentinel-eye/shared/realtime/layoutHub';
import type { AppDispatch } from '../../app/store.js';

export interface UseLayoutLifecycleOptions {
  /** OIDC access token factory; called on every reconnect. */
  accessTokenFactory: () => string | Promise<string>;
  /** Called when a Published revision lands. */
  onPublished?: (message: LayoutRevisionPublishedMessage) => void;
  /** Called when a Published revision is Archived. */
  onArchived?: (message: LayoutRevisionArchivedMessage) => void;
  /** Called when an overlay revision becomes Published (spec 004 US3). */
  onOverlayPublished?: (message: OverlayRevisionPublishedMessage) => void;
  /** Called when an overlay revision becomes Archived (spec 004 US3). */
  onOverlayArchived?: (message: OverlayRevisionArchivedMessage) => void;
  /** Called when an overlay's resolved text changes (spec 005 US2). */
  onResolvedOverlayTextChanged?: (message: ResolvedOverlayTextChangedMessage) => void;
  /** Called when an overlay highlight is requested (spec 010 US3). */
  onOverlayHighlightChanged?: (message: OverlayHighlightChangedMessage) => void;
  /** Called after a successful SignalR reconnect. */
  onReconnected?: () => void;
  /**
   * Disable while the user isn't signed in yet. The hub requires
   * ``sse.layouts.read`` (or the grandfathered ``sse.management``) and the
   * access-token factory will throw if called before auth lands.
   *
   * The hub also joins one group per fab in the token's ``groups`` claim, and
   * resolved-text and highlight pushes are addressed to those groups — so a
   * connection holding no fab joins nothing and receives none of them. Until
   * spec 041 the kiosk held no fab, which means this path had never run.
   */
  enabled?: boolean;
}

export interface UseLayoutLifecycleResult {
  /**
   * True while the hub is degraded (spec 011 FR-007) — the pages render
   * the discreet badge from this. The transient boot-time `connecting`
   * state is NOT degraded, so the badge never flashes on a healthy load.
   */
  degraded: boolean;
}

/**
 * Subscribes to the LayoutLifecycle SignalR hub for the lifetime of the
 * component. The hub handle owns unbounded (re)connect retries (spec 011
 * FR-006); on every reconnect this hook reconciles the full pushed state
 * (FR-008): the Published layout list (covers revocation), the overlay
 * list, every mounted per-overlay query, and the resolved-text snapshots.
 */
export function useLayoutLifecycle(options: UseLayoutLifecycleOptions): UseLayoutLifecycleResult {
  const dispatch = useDispatch<AppDispatch>();
  const enabled = options.enabled ?? true;
  const [degraded, setDegraded] = useState(false);

  // The hub connection is long-lived — rebuilt only when `enabled` flips,
  // not on every render. Its callbacks must therefore read the LATEST
  // options rather than the closures captured when the connection was
  // built: a callback closing over state that resolves after mount (e.g.
  // CellPage's overlayIdentifier, null until the layout query lands) would
  // otherwise capture the stale value and silently no-op forever. Keep the
  // latest options in a ref the handlers dereference on each event.
  const optionsRef = useRef(options);
  useEffect(() => {
    optionsRef.current = options;
  });

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const hub = createLayoutHubClient(
      {
        accessTokenFactory: () => optionsRef.current.accessTokenFactory(),
      },
      {
        onPublished: (message) => optionsRef.current.onPublished?.(message),
        onArchived: (message) => optionsRef.current.onArchived?.(message),
        onOverlayPublished: (message) => optionsRef.current.onOverlayPublished?.(message),
        onOverlayArchived: (message) => optionsRef.current.onOverlayArchived?.(message),
        onResolvedOverlayTextChanged: (message) => optionsRef.current.onResolvedOverlayTextChanged?.(message),
        onOverlayHighlightChanged: (message) => optionsRef.current.onOverlayHighlightChanged?.(message),
        onStateChange: (state) => setDegraded(state === 'degraded'),
        onReconnected: () => {
          logResilienceEvent('hub', 'reconnected-reconciliation');
          dispatch(layoutsApi.util.invalidateTags([{ type: 'LayoutList', id: 'ALL' }]));
          dispatch(overlaysApi.util.invalidateTags([{ type: 'OverlayList', id: 'ALL' }]));
          dispatch(overlaysApi.util.invalidateTags(['Overlay']));
          dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ALL' }]));
          optionsRef.current.onReconnected?.();
        },
      },
    );

    // Defer the connect by a tick so React 18 StrictMode's dev double-mount
    // (mount → cleanup → mount) cancels this start before it begins
    // negotiating; otherwise the cleanup's hub.stop() aborts the in-flight
    // start() and SignalR logs a spurious "stopped during negotiation" error.
    // The surviving mount's start fires on the next tick and connects normally.
    let started = false;
    const startTimer = setTimeout(() => {
      started = true;
      // Retry ownership lives in the hub handle (spec 011 FR-006): start()
      // resolves once the connection is started or a retry is scheduled.
      void hub.start();
    }, 0);

    return () => {
      clearTimeout(startTimer);
      if (started) {
        void hub.stop().catch(() => undefined);
      }
    };
  }, [enabled, dispatch]);

  return { degraded };
}
