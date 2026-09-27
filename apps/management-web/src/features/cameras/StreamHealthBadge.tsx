import clsx from 'clsx';
import type { StreamHealth, StreamState } from '@smart-sentinel-eye/shared/api/streams.api';
import { Popover } from '@smart-sentinel-eye/shared/ui/primitives/Popover';

export interface StreamHealthBadgeProps {
  stream: StreamHealth | undefined;
}

const TONES: Record<StreamState | 'unknown', string> = {
  Healthy: 'bg-accent-active/20 text-accent-active border-accent-active/40',
  Degraded: 'bg-accent-warning/20 text-accent-warning border-accent-warning/40',
  Offline: 'bg-accent-fault/20 text-accent-fault border-accent-fault/40',
  Provisioning: 'bg-fg-muted/20 text-fg-muted border-fg-muted/40',
  unknown: 'bg-fg-muted/10 text-fg-muted border-fg-muted/30',
};

const PILL = 'inline-flex items-center rounded border px-2 py-0.5 text-xs';

/**
 * Pill rendering the current StreamState for a single camera, with a Radix
 * popover carrying `lastSuccessAt` and (for non-Healthy states) the error
 * string — a focusable trigger, not a hover-only tooltip, so the detail is
 * reachable from the keyboard (spec 266 §1 finding 4). The page polls
 * `useListStreamsQuery` once for the visible rows and hands each badge its
 * slice — avoids N independent polls.
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
    <Popover
      trigger={
        <button
          type="button"
          className={clsx(
            PILL,
            TONES[stream.state] ?? TONES.unknown,
            'focus-visible:ring-2 focus-visible:ring-focus-ring',
          )}
        >
          {stream.state}
        </button>
      }
      label={`Stream ${stream.state}`}
    >
      {detailLines(stream.state, stream.lastSuccessAt, stream.error).map((line) => (
        <p key={line}>{line}</p>
      ))}
    </Popover>
  );
}

function detailLines(state: StreamState, lastSuccessAt: string | null, error: string | null): readonly string[] {
  const lines: string[] = [`State: ${state}`];
  if (lastSuccessAt !== null) {
    lines.push(`Last frame: ${new Date(lastSuccessAt).toLocaleString()}`);
  }
  if (state !== 'Healthy' && error !== null) {
    lines.push(`Error: ${error}`);
  }
  return lines;
}
