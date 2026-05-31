import { useEffect, useRef } from 'react';
import { useDispatch } from 'react-redux';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import {
  createLayoutHubClient,
  type LayoutRevisionArchivedMessage,
  type LayoutRevisionPublishedMessage,
  type OverlayRevisionArchivedMessage,
  type OverlayRevisionPublishedMessage,
  type ResolvedOverlayTextChangedMessage,
} from '@smart-sentinel-eye/shared/realtime/layoutHub';
import type { AppDispatch } from '../../app/store.js';

const HUB_PATH = '/hubs/layouts';

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
  /** Called after a successful SignalR reconnect. */
  onReconnected?: () => void;
  /**
   * Disable while the user isn't signed in yet. The hub requires
   * ``sse.management`` scope and the access-token factory will throw
   * if called before auth lands.
   */
  enabled?: boolean;
}

/**
 * Subscribes to the LayoutLifecycle SignalR hub for the lifetime of
 * the component. On reconnect, invalidates the Published list cache so
 * any missed events get reconciled (spec 003 FR-012).
 */
export function useLayoutLifecycle(options: UseLayoutLifecycleOptions): void {
  const dispatch = useDispatch<AppDispatch>();
  const enabled = options.enabled ?? true;

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
        hubUrl: HUB_PATH,
        accessTokenFactory: () => optionsRef.current.accessTokenFactory(),
      },
      {
        onPublished: (message) => optionsRef.current.onPublished?.(message),
        onArchived: (message) => optionsRef.current.onArchived?.(message),
        onOverlayPublished: (message) => optionsRef.current.onOverlayPublished?.(message),
        onOverlayArchived: (message) => optionsRef.current.onOverlayArchived?.(message),
        onResolvedOverlayTextChanged: (message) =>
          optionsRef.current.onResolvedOverlayTextChanged?.(message),
        onReconnected: () => {
          dispatch(layoutsApi.util.invalidateTags([{ type: 'LayoutList', id: 'ALL' }]));
          dispatch(overlaysApi.util.invalidateTags([{ type: 'OverlayList', id: 'ALL' }]));
          dispatch(systemVariablesApi.util.invalidateTags([{ type: 'OverlaySnapshot', id: 'ALL' }]));
          optionsRef.current.onReconnected?.();
        },
      },
    );

    void hub.start().catch(() => {
      // Initial connect failures recover on the next automatic reconnect.
    });

    return () => {
      void hub.stop().catch(() => undefined);
    };
  }, [enabled, dispatch]);
}
