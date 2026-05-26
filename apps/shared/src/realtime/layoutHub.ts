import { HubConnectionBuilder, type HubConnection, HubConnectionState } from '@microsoft/signalr';

export interface LayoutRevisionPublishedMessage {
  layout: string;
  revisionNumber: number;
  name: string;
  camera: string;
  publishedAt: string;
}

export interface LayoutRevisionArchivedMessage {
  layout: string;
  revisionNumber: number;
  archivedAt: string;
}

export interface LayoutHubCallbacks {
  onPublished?: (message: LayoutRevisionPublishedMessage) => void;
  onArchived?: (message: LayoutRevisionArchivedMessage) => void;
  onReconnected?: () => void;
}

export interface LayoutHubConfig {
  hubUrl: string;
  accessTokenFactory: () => string | Promise<string>;
}

/**
 * Thin SignalR client wrapper for the LayoutLifecycle hub
 * (spec 003 FR-009). Hides the @microsoft/signalr surface behind a
 * focused callback API so the kiosk pages only deal with typed events.
 *
 * The native client handles exponential reconnect; `onReconnected`
 * fires after a successful reconnect so the caller can re-fetch
 * `GET /layouts?state=published` and reconcile any missed events
 * (FR-012).
 */
export function createLayoutHubClient(config: LayoutHubConfig, callbacks: LayoutHubCallbacks): LayoutHubHandle {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(config.hubUrl, {
      accessTokenFactory: () => Promise.resolve(config.accessTokenFactory()),
    })
    .withAutomaticReconnect()
    .build();

  if (callbacks.onPublished !== undefined) {
    connection.on('LayoutRevisionPublished', callbacks.onPublished);
  }
  if (callbacks.onArchived !== undefined) {
    connection.on('LayoutRevisionArchived', callbacks.onArchived);
  }
  if (callbacks.onReconnected !== undefined) {
    connection.onreconnected(() => {
      callbacks.onReconnected?.();
    });
  }

  return {
    start: () => connection.start(),
    stop: async () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        await connection.stop();
      }
    },
    state: () => connection.state,
  };
}

export interface LayoutHubHandle {
  start: () => Promise<void>;
  stop: () => Promise<void>;
  state: () => HubConnectionState;
}
