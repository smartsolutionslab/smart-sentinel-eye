import clsx from 'clsx';
import { useEffect, useRef, useState } from 'react';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { WhepClient } from '@smart-sentinel-eye/shared/streaming/WhepClient';

export type CameraViewerStatus =
  | 'idle'
  | 'connecting'
  | 'live'
  | 'reconnecting'
  | 'error'
  | 'offline';

export interface CameraViewerProps {
  cameraIdentifier: string;
  /** Resolves the bearer token for the current operator (Keycloak access token). */
  getToken: () => Promise<string | null>;
  className?: string;
}

/**
 * Generic WebRTC viewer composite (spec 002 FR-016). Accepts a
 * cameraIdentifier and renders the live stream. Designed to be embedded
 * unchanged by spec 003 (Layout Composition) — no layout concerns leak in.
 */
export function CameraViewer({ cameraIdentifier, getToken, className }: CameraViewerProps) {
  const { data: stream, error: queryError } = useGetStreamQuery(cameraIdentifier, {
    pollingInterval: 5000,
  });
  const videoRef = useRef<HTMLVideoElement>(null);
  const [status, setStatus] = useState<CameraViewerStatus>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const whepUrl = stream?.whepUrl;
  const isOffline = stream?.state === 'Offline';
  const offlineError = stream?.error ?? null;

  useEffect(() => {
    const videoEl = videoRef.current;
    if (!whepUrl || !videoEl) return undefined;
    if (isOffline) {
      setStatus('offline');
      setErrorMessage(offlineError ?? 'Stream is offline.');
      return undefined;
    }

    const controller = new AbortController();
    const client = new WhepClient({ whepUrl, getToken });
    setStatus('connecting');
    setErrorMessage(null);

    client
      .connect(videoEl, controller.signal)
      .then(() => {
        setStatus('live');
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) return;
        const message = cause instanceof Error ? cause.message : String(cause);
        setStatus('error');
        setErrorMessage(message);
      });

    return () => {
      controller.abort();
      client.close();
    };
  }, [whepUrl, isOffline, offlineError, getToken]);

  const streamState = stream?.state;
  const streamError = stream?.error ?? null;

  useEffect(() => {
    if (streamState === 'Degraded' && status === 'live') {
      setStatus('reconnecting');
      setErrorMessage(streamError ?? 'Source unreachable. Reconnecting…');
    }
    if (streamState === 'Healthy' && status === 'reconnecting') {
      setStatus('live');
      setErrorMessage(null);
    }
  }, [streamState, streamError, status]);

  return (
    <div className={clsx('relative aspect-video w-full overflow-hidden rounded-md bg-black', className)}>
      <video ref={videoRef} autoPlay playsInline muted className="h-full w-full object-contain" />
      {status !== 'live' && (
        <ViewerOverlay status={status} message={errorMessage} stream={stream} queryError={queryError} />
      )}
    </div>
  );
}

function ViewerOverlay({
  status,
  message,
  stream,
  queryError,
}: {
  status: CameraViewerStatus;
  message: string | null;
  stream: StreamHealth | undefined;
  queryError: unknown;
}) {
  const label = labelFor(status, stream);
  const tone =
    status === 'error' || status === 'offline'
      ? 'text-accent-fault'
      : status === 'reconnecting'
        ? 'text-accent-warn'
        : 'text-fg-muted';

  const hint =
    message ??
    (queryError !== undefined ? 'Could not reach the streaming service.' : null);

  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/60 text-center text-sm">
      <span className={clsx('font-medium', tone)}>{label}</span>
      {hint !== null && <span className="px-4 text-xs text-fg-muted">{hint}</span>}
    </div>
  );
}

function labelFor(status: CameraViewerStatus, stream: StreamHealth | undefined): string {
  if (status === 'live') return 'Live';
  if (status === 'connecting') return 'Connecting…';
  if (status === 'reconnecting') return 'Reconnecting…';
  if (status === 'offline') return 'Stream is offline';
  if (status === 'error') return 'Viewer error';
  if (stream?.state === 'Provisioning') return 'Provisioning stream…';
  return 'Idle';
}
