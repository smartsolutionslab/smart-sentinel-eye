import clsx from 'clsx';
import type { StreamHealth, StreamState } from '@smart-sentinel-eye/shared/api/streams.api';
import { Tooltip } from '@smart-sentinel-eye/shared/ui/primitives/Tooltip';

export interface StreamHealthBadgeProps {
  stream: StreamHealth | undefined;
}

const TONES: Record<StreamState | 'unknown', string> = {
  Healthy: 'bg-accent-active/20 text-accent-active border-accent-active/40',
  Degraded: 'bg-accent-warn/20 text-accent-warn border-accent-warn/40',
  Offline: 'bg-accent-fault/20 text-accent-fault border-accent-fault/40',
  Provisioning: 'bg-fg-muted/20 text-fg-muted border-fg-muted/40',
  unknown: 'bg-fg-muted/10 text-fg-muted border-fg-muted/30',
};

const PILL = 'inline-flex items-center rounded border px-2 py-0.5 text-xs';

/**
 * Pill rendering the current StreamState for a single camera, with a
 * Radix tooltip carrying `lastSuccessAt` and (for non-Healthy states) the
 * error string. The page polls `useListStreamsQuery` once for the visible
 * rows and hands each badge its slice — avoids N independent polls.
 */
export function StreamHealthBadge({ stream }: StreamHealthBadgeProps) {
  if (stream === undefined) {
    return (
      <span className={clsx(PILL, TONES.unknown)} aria-label="Stream state unknown">
        Unknown
      </span>
    );
  }

  return (
    <Tooltip
      trigger={<span className={clsx(PILL, TONES[stream.state] ?? TONES.unknown)}>{stream.state}</span>}
      content={buildTooltip(stream.state, stream.lastSuccessAt, stream.error)}
    />
  );
}

function buildTooltip(state: StreamState, lastSuccessAt: string | null, error: string | null): string {
  const lines: string[] = [`State: ${state}`];
  if (lastSuccessAt !== null) {
    lines.push(`Last frame: ${new Date(lastSuccessAt).toLocaleString()}`);
  }
  if (state !== 'Healthy' && error !== null) {
    lines.push(`Error: ${error}`);
  }
  return lines.join('\n');
}
