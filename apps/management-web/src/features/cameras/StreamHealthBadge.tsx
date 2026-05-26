import clsx from 'clsx';
import { useGetStreamQuery, type StreamState } from '@smart-sentinel-eye/shared/api/streams.api';

export interface StreamHealthBadgeProps {
  cameraIdentifier: string;
}

const TONES: Record<StreamState | 'unknown', string> = {
  Healthy: 'bg-accent-active/20 text-accent-active border-accent-active/40',
  Degraded: 'bg-accent-warn/20 text-accent-warn border-accent-warn/40',
  Offline: 'bg-accent-fault/20 text-accent-fault border-accent-fault/40',
  Provisioning: 'bg-fg-muted/20 text-fg-muted border-fg-muted/40',
  unknown: 'bg-fg-muted/10 text-fg-muted border-fg-muted/30',
};

/**
 * Tiny pill displaying the camera's current stream state. Polls the
 * stream-distribution API every 5 seconds (replaced by push transport in
 * spec 002 v2 per ADR-0076).
 */
export function StreamHealthBadge({ cameraIdentifier }: StreamHealthBadgeProps) {
  const { data, error, isLoading } = useGetStreamQuery(cameraIdentifier, {
    pollingInterval: 5000,
  });

  if (isLoading) {
    return <span className={clsx('rounded border px-2 py-0.5 text-xs', TONES.unknown)}>…</span>;
  }
  if (error !== undefined || !data) {
    return (
      <span
        title="Stream metadata unavailable"
        className={clsx('rounded border px-2 py-0.5 text-xs', TONES.unknown)}
      >
        Unknown
      </span>
    );
  }

  const tone = TONES[data.state] ?? TONES.unknown;
  const tooltip = buildTooltip(data.state, data.lastSuccessAt, data.error);

  return (
    <span title={tooltip} className={clsx('rounded border px-2 py-0.5 text-xs', tone)}>
      {data.state}
    </span>
  );
}

function buildTooltip(state: StreamState, lastSuccessAt: string | null, error: string | null): string {
  const parts: string[] = [];
  if (lastSuccessAt !== null) {
    parts.push(`Last frame: ${new Date(lastSuccessAt).toLocaleString()}`);
  }
  if (state !== 'Healthy' && error !== null) {
    parts.push(`Error: ${error}`);
  }
  return parts.length > 0 ? parts.join('\n') : state;
}
