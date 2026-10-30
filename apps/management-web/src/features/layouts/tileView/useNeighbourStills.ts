import { useCallback, useEffect, useRef, useState } from 'react';
import { useFrameCapture } from '@smart-sentinel-eye/shared/ui/composites/useFrameCapture';
import type { DesignerCell } from '../gridDesignerModel';

/**
 * The cameras to take a still of: the open tile's first, then the others nearest first by the
 * distance between tile middles (ties by row, then column). A camera is named once.
 */
export function nearestFirst(cells: readonly DesignerCell[], openIndex: number): string[] {
  const open = cells[openIndex];
  if (open === undefined) return [];
  const middle = (cell: DesignerCell) => ({ row: cell.row + cell.rowSpan / 2, col: cell.col + cell.colSpan / 2 });
  const from = middle(open);
  const others = cells
    .filter((_, index) => index !== openIndex)
    .map((cell) => {
      const at = middle(cell);
      return { cell, distance: (at.row - from.row) ** 2 + (at.col - from.col) ** 2 };
    })
    .sort((a, b) => a.distance - b.distance || a.cell.row - b.cell.row || a.cell.col - b.cell.col);
  return [...new Set([open, ...others.map((entry) => entry.cell)].map((cell) => cell.cameraIdentifier))];
}

/**
 * One capture queue for the whole view. `useFrameCapture` only guards its own instance, so a
 * capture per tile would open them all at once; one instance walks the cameras in order, one at a
 * time, each bounded by that hook's own timeout. Results are kept per camera while the view lives.
 */
export function useNeighbourStills(order: readonly string[]) {
  const { state, activeCamera, capture, cancel, fail } = useFrameCapture();
  const [stills, setStills] = useState<Record<string, string>>({});
  const [failed, setFailed] = useState<ReadonlySet<string>>(new Set());
  const current = useRef<string | null>(null);

  const markFailed = useCallback((camera: string) => setFailed((previous) => new Set(previous).add(camera)), []);

  useEffect(() => {
    if (state === 'failed') {
      if (current.current !== null) markFailed(current.current);
      cancel();
      return;
    }
    if (state !== 'idle') return;
    const next = order.find((camera) => !(camera in stills) && !failed.has(camera));
    if (next === undefined) return;
    current.current = next;
    capture(next);
  }, [state, order, stills, failed, capture, cancel, markFailed]);

  const onCaptured = useCallback(
    (dataUrl: string) => {
      const camera = current.current;
      if (camera !== null) setStills((previous) => ({ ...previous, [camera]: dataUrl }));
      cancel();
    },
    [cancel],
  );

  return { stills, failed, capturingCamera: activeCamera, onCaptured, onFailed: fail };
}
