import {
  OverlayLabel,
  OverlayShape,
  type CameraViewerOverlay,
} from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import { viewerBoxPx } from './displayShapes';

interface TileFrameProps {
  widthPx: number;
  heightPx: number;
  /** The captured picture of the tile's camera; `null` shows a neutral backdrop. */
  backdropUrl: string | null;
  elements: readonly CameraViewerOverlay[] | undefined;
}

/**
 * One wall tile at a pixel size: the cell clips, and the 16:9 viewer box inside it is as wide
 * as the cell and vertically centred, as on the wall. The elements are the wall's own overlay
 * rendering, so placement and `cqw` text scaling match by construction.
 */
export function TileFrame({ widthPx, heightPx, backdropUrl, elements }: TileFrameProps) {
  const { height, top } = viewerBoxPx({ width: widthPx, height: heightPx });
  return (
    <div
      data-testid="tile-frame"
      className="relative overflow-hidden"
      style={{ width: `${widthPx}px`, height: `${heightPx}px` }}
    >
      <div
        data-testid="tile-frame-viewer"
        className="absolute left-0 aspect-video w-full overflow-hidden @container"
        style={{ top: `${top}px`, height: `${height}px` }}
      >
        {backdropUrl === null ? (
          <div data-testid="tile-frame-backdrop-neutral" className="absolute inset-0 bg-bg-video" />
        ) : (
          <img
            data-testid="tile-frame-backdrop"
            src={backdropUrl}
            alt=""
            className="absolute inset-0 h-full w-full object-contain"
          />
        )}
        {elements?.map((element, ordinal) =>
          element.kind === 'Text' ? (
            <OverlayLabel key={ordinal} overlay={element} />
          ) : (
            <OverlayShape key={ordinal} overlay={element} />
          ),
        )}
      </div>
    </div>
  );
}
