import type { StreamHealth, StreamState } from '@smart-sentinel-eye/shared/api/streams.api';
import { Badge, type BadgeTone } from '@smart-sentinel-eye/shared/ui/composites/Badge';
import { Popover } from '@smart-sentinel-eye/shared/ui/primitives/Popover';

export interface StreamHealthBadgeProps {
  stream: StreamHealth | undefined;
}

const TONE: Record<StreamState, BadgeTone> = {
  Healthy: 'active',
  Degraded: 'warning',
  Offline: 'fault',
  Provisioning: 'neutral',
};

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
      <Badge tone="neutral" aria-label="Stream state unknown">
        Unknown
      </Badge>
    );
  }

  return (
    <Popover
      trigger={
        <Badge
          tone={TONE[stream.state] ?? 'neutral'}
          asChild
          className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"
        >
          <button type="button">{stream.state}</button>
        </Badge>
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
