import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { layerName, textOverlaps } from '@smart-sentinel-eye/shared/ui/composites/overlayLayers';

/** Names each pair of overlapping labels. Always mounted, so a screen reader is already watching it when a line appears. */
export function OverlapStatus({
  elements,
  rows,
}: {
  elements: readonly OverlayElement[];
  rows: readonly { id: string }[];
}) {
  const overlaps = textOverlaps(elements);
  return (
    <div role="status" data-testid="overlay-editor-overlap-status">
      {overlaps.map(([first, second]) => (
        <p
          key={`${rows[first]!.id}:${rows[second]!.id}`}
          data-testid="overlay-editor-overlap-warning"
          className="text-sm text-fg-muted"
        >
          {`"${layerName(elements[first]!, first, elements)}" and "${layerName(elements[second]!, second, elements)}" overlap, so one may hide part of the other on the wall.`}
        </p>
      ))}
    </div>
  );
}
