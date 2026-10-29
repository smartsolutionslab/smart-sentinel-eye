import type { CameraViewerOverlay } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

function percent(fraction: number): string {
  return `${Math.round(fraction * 100)} %`;
}

/**
 * The frame as text, so what the picture shows is also available without it:
 * each element's kind, its (resolved) text and where it sits, in percent of the tile.
 */
export function TileViewReadout({ elements }: { elements: readonly CameraViewerOverlay[] }) {
  return (
    <section aria-labelledby="tile-view-readout-heading" className="mt-4">
      <h3 id="tile-view-readout-heading" className="text-sm font-medium text-fg-muted">
        What this tile shows
      </h3>
      {elements.length === 0 ? (
        <p className="mt-1 text-sm text-fg-muted">No overlay elements, the picture alone.</p>
      ) : (
        <ul className="mt-1 list-disc pl-5 text-sm">
          {elements.map((element, ordinal) => (
            <li key={ordinal}>
              {element.kind}
              {element.kind === 'Text' && <> “{element.text}”</>}
              {' — '}
              left {percent(element.normalizedX)}, top {percent(element.normalizedY)},{' '}
              {percent(element.normalizedWidth)} wide, {percent(element.normalizedHeight)} high
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
