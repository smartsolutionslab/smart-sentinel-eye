import { useCallback, useEffect, useRef, useState } from 'react';
import type { RefObject } from 'react';
import type { StreamState } from '@smart-sentinel-eye/shared/api/streams.api';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { WhepClient } from '@smart-sentinel-eye/shared/streaming/WhepClient';

export type CameraViewerStatus = 'idle' | 'connecting' | 'live' | 'reconnecting' | 'error' | 'offline';

export interface WhepSessionOptions {
  cameraIdentifier: string;
  whepUrl: string | undefined;
  streamState: StreamState | undefined;
  streamError: string | null;
  getToken: () => Promise<string | null>;
}

export interface WhepSessionResult {
  videoRef: RefObject<HTMLVideoElement | null>;
  status: CameraViewerStatus;
  errorMessage: string | null;
  /**
   * The session's receiver statistics, or null when there is no connection
   * (spec 040).
   *
   * <p>
   * One leg of the latency budget can be observed nowhere else — `inbound-rtp`
   * is where the kiosk's decode timing lives. <b>Read-only:</b> it hands back a
   * report from a connection this hook already owns, and nothing about the
   * session, its retries or its teardown changes (FR-011).
   * </p>
   */
  stats: () => Promise<RTCStatsReport> | null;
}

const RETRY_BASE_MS = 1_000;
const RETRY_CAP_MS = 15_000;
const DISCONNECT_GRACE_MS = 5_000;

function jitteredRetryDelay(attempt: number): number {
  const base = Math.min(RETRY_BASE_MS * 2 ** attempt, RETRY_CAP_MS);
  // Full ±20% jitter keeps 16+ tiles from synchronizing their reconnect
  // attempts after a shared outage (spec 011 SC-005).
  return base * (0.8 + Math.random() * 0.4);
}

/**
 * Owns the per-tile stream session state machine (spec 011 data-model §1).
 * "Live" is derived from the RTCPeerConnection state — never from the WHEP
 * POST succeeding — and failed sessions are retried indefinitely with
 * jittered exponential backoff (FR-001…FR-005).
 */
export function useWhepSession(options: WhepSessionOptions): WhepSessionResult {
  const { cameraIdentifier, whepUrl, streamState, streamError, getToken } = options;
  const videoRef = useRef<HTMLVideoElement>(null);
  // Spec 040: the live client, so a caller can read receiver statistics.
  // Read-only access to an object this hook already owns.
  const clientRef = useRef<WhepClient | null>(null);
  const [status, setStatus] = useState<CameraViewerStatus>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [retryNonce, setRetryNonce] = useState(0);
  const statusRef = useRef<CameraViewerStatus>('idle');
  const attemptRef = useRef(0);
  const previousStreamStateRef = useRef<StreamState | undefined>(undefined);

  // Callers commonly pass getToken as a fresh inline closure
  // (e.g. () => Promise.resolve(auth.user?.access_token)), so its identity
  // changes on every parent render. Hold the latest reference and read it
  // at connect time, so the effect below doesn't tear down and renegotiate
  // the RTCPeerConnection on every render — only when the stream changes.
  const getTokenRef = useRef(getToken);
  useEffect(() => {
    getTokenRef.current = getToken;
  });

  const transitionTo = useCallback(
    (next: CameraViewerStatus, message: string | null = null) => {
      setErrorMessage(message);
      if (statusRef.current === next) return;
      logResilienceEvent('stream', `${statusRef.current}→${next}`, { cameraIdentifier });
      statusRef.current = next;
      setStatus(next);
    },
    [cameraIdentifier],
  );

  const offlineMessage = streamState === 'Offline' ? (streamError ?? 'Stream is offline.') : null;

  useEffect(() => {
    void retryNonce; // dep only: each bump forces a fresh connection attempt
    const videoEl = videoRef.current;
    if (!whepUrl || !videoEl) return undefined;
    if (offlineMessage !== null) {
      transitionTo('offline', offlineMessage);
      return undefined;
    }

    const controller = new AbortController();
    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    let graceTimer: ReturnType<typeof setTimeout> | null = null;

    const scheduleRetry = (message: string | null) => {
      if (disposed || retryTimer !== null) return;
      transitionTo('reconnecting', message);
      const delay = jitteredRetryDelay(attemptRef.current);
      attemptRef.current += 1;
      retryTimer = setTimeout(() => setRetryNonce((nonce) => nonce + 1), delay);
    };

    const onConnectionStateChange = (state: RTCPeerConnectionState) => {
      if (disposed) return;
      if (state === 'connected') {
        if (graceTimer !== null) {
          clearTimeout(graceTimer);
          graceTimer = null;
        }
        attemptRef.current = 0;
        transitionTo('live');
        return;
      }
      if (state === 'failed') {
        scheduleRetry('Connection failed. Reconnecting…');
        return;
      }
      if (state === 'disconnected' && graceTimer === null && retryTimer === null) {
        // Grace window: ICE consent checks can self-heal a micro-blip, so
        // retrying instantly would churn sessions (spec 011 research R1).
        graceTimer = setTimeout(() => {
          graceTimer = null;
          scheduleRetry('Connection lost. Reconnecting…');
        }, DISCONNECT_GRACE_MS);
      }
    };

    // Spec 040: held so the caller can read receiver statistics. Read-only —
    // nothing about the session lifecycle changes (FR-011).
    clientRef.current = null;
    const client = new WhepClient({
      whepUrl,
      getToken: () => getTokenRef.current(),
      onConnectionStateChange,
    });
    clientRef.current = client;
    transitionTo('connecting');
    client.connect(videoEl, controller.signal).catch((cause: unknown) => {
      if (disposed || controller.signal.aborted) return;
      scheduleRetry(cause instanceof Error ? cause.message : String(cause));
    });

    return () => {
      disposed = true;
      if (retryTimer !== null) clearTimeout(retryTimer);
      if (graceTimer !== null) clearTimeout(graceTimer);
      controller.abort();
      client.close();
      clientRef.current = null;
    };
  }, [whepUrl, offlineMessage, retryNonce, transitionTo]);

  useEffect(() => {
    const previous = previousStreamStateRef.current;
    previousStreamStateRef.current = streamState;
    if (streamState === 'Degraded' && statusRef.current === 'live') {
      transitionTo('reconnecting', streamError ?? 'Source unreachable. Reconnecting…');
      return;
    }
    if (previous === 'Degraded' && streamState === 'Healthy') {
      // FR-005: a health recovery re-establishes a real session — the label
      // only returns to Live once the new peer connection reports connected.
      setRetryNonce((nonce) => nonce + 1);
    }
  }, [streamState, streamError, transitionTo]);

  // Stable across renders, deliberately. Callers put this in effect dependency
  // arrays, and a fresh identity each render tears their effect down and rebuilds
  // it — which silently killed the decode sampler: its 5 s interval was cleared
  // before it could fire twice, and its "previous sample" was reset every time,
  // so it reported nothing at all against real video (issue 1889).
  const stats = useCallback(() => clientRef.current?.stats() ?? null, []);

  return { videoRef, status, errorMessage, stats };
}
