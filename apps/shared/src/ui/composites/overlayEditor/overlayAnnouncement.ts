import { formatPercent } from '../normalizedPercent.js';
import type { RunKey } from '../useOverlayEditHistory.js';

export type AnnounceAxis = 'x' | 'y' | 'width' | 'height';

const AXIS_ANNOUNCE_LABEL: Record<AnnounceAxis, string> = {
  x: 'Left',
  y: 'Top',
  width: 'Width',
  height: 'Height',
};

// FR-016: which edge a refused press hit. Position axes are refused at the
// canvas edge on either side; size axes are refused at FR-008's floor on
// the low side and at the canvas edge (the reachable-region bound) on the
// high side.
export function edgeName(axis: AnnounceAxis, delta: number): string {
  if (axis === 'x') return delta < 0 ? 'at the left edge' : 'at the right edge';
  if (axis === 'y') return delta < 0 ? 'at the top edge' : 'at the bottom edge';
  if (axis === 'width') return delta < 0 ? 'at the minimum' : 'at the right edge';
  return delta < 0 ? 'at the minimum' : 'at the bottom edge';
}

export function buildAnnouncement(axis: AnnounceAxis, value: number, edge: string | null): string {
  const base = `${AXIS_ANNOUNCE_LABEL[axis]} ${formatPercent(value)}`;
  return edge === null ? base : `${base}, ${edge}`;
}

// Spec 154 Decision 2, site 6: the run key includes both the axis and the
// mode, so switching between moving and resizing mid-hold (Ctrl) starts a
// new run even though the axis label alone (`width` vs `x`) would already
// differ; holding Shift changes only the step size, never this key.
export function arrowRunKey(resizing: boolean, axis: AnnounceAxis): RunKey {
  return `arrow:${resizing ? 'resize' : 'move'}:${axis}`;
}
