import { useEffect } from 'react';
import { useDispatch } from 'react-redux';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  createLayoutHubClient,
  type LayoutRevisionArchivedMessage,
  type LayoutRevisionPublishedMessage,
  type OverlayRevisionArchivedMessage,
  type OverlayRevisionPublishedMessage,
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

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const hub = createLayoutHubClient(
      { hubUrl: HUB_PATH, accessTokenFactory: options.accessTokenFactory },
      {
        onPublished: options.onPublished,
        onArchived: options.onArchived,
        onOverlayPublished: options.onOverlayPublished,
        onOverlayArchived: options.onOverlayArchived,
        onReconnected: () => {
          dispatch(layoutsApi.util.invalidateTags([{ type: 'LayoutList', id: 'ALL' }]));
          dispatch(overlaysApi.util.invalidateTags([{ type: 'OverlayList', id: 'ALL' }]));
          options.onReconnected?.();
        },
      },
    );

    void hub.start().catch(() => {
      // Initial connect failures recover on the next automatic reconnect.
    });

    return () => {
      void hub.stop().catch(() => undefined);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]);
}
