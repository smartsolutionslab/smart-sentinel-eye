import { skipToken } from '@reduxjs/toolkit/query';
import { useGetOverlayQuery, type OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import type { DesignerCell, GridDesignerValue } from '../gridDesignerModel';
import { cellSizePx, displaySizePx, type DisplayShape, type SizePx } from './displayShapes';
import { revisionToShow, useResolvedElements } from './shownOverlay';
import { TileFrame } from './TileFrame';

/** The context is a small picture of the whole display, not a second full-size one. */
const CONTEXT_AREA: SizePx = { width: 480, height: 270 };

interface ContextTileProps {
  cell: DesignerCell;
  index: number;
  open: boolean;
  grid: GridDesignerValue['grid'];
  shape: DisplayShape;
  still: string | undefined;
  /** The open tile's elements while they are being edited; absent shows the saved overlay. */
  elementsOverride?: readonly OverlayElement[];
}

function ContextTile({ cell, index, open, grid, shape, still, elementsOverride }: ContextTileProps) {
  const { overlayIdentifier } = cell;
  const { data: overlay } = useGetOverlayQuery(overlayIdentifier === '' ? skipToken : overlayIdentifier);
  const { revision } = revisionToShow(overlayIdentifier === '' ? undefined : overlay);
  const { elements, resolvers } = useResolvedElements(elementsOverride ?? revision?.elements ?? []);
  const size = cellSizePx(shape, grid, cell, CONTEXT_AREA);

  return (
    <div
      data-testid="context-tile"
      data-cell-index={index}
      data-open={open}
      aria-current={open ? 'true' : undefined}
      style={{
        gridRow: `${cell.row + 1} / span ${cell.rowSpan}`,
        gridColumn: `${cell.col + 1} / span ${cell.colSpan}`,
      }}
      className={open ? 'rounded-sm ring-2 ring-accent-primary' : 'opacity-90'}
    >
      <TileFrame widthPx={size.width} heightPx={size.height} backdropUrl={still ?? null} elements={elements} />
      {resolvers}
    </div>
  );
}

export interface TileViewContextProps {
  grid: GridDesignerValue['grid'];
  cells: readonly DesignerCell[];
  openIndex: number;
  shape: DisplayShape;
  stills: Readonly<Record<string, string>>;
  openElements?: readonly OverlayElement[];
}

/**
 * Every tile of the layout where it sits, each with its own still and overlay, the open one marked.
 * Read-only: nothing here takes focus or a click. A layout of one tile has no context to show.
 */
export function TileViewContext({ grid, cells, openIndex, shape, stills, openElements }: TileViewContextProps) {
  if (cells.length < 2) return null;
  const display = displaySizePx(shape, CONTEXT_AREA);
  return (
    <section aria-label="The tile among the layout’s other tiles" className="flex flex-col gap-2">
      <h3 className="text-sm font-medium text-fg-muted">In the layout</h3>
      <div
        className="grid gap-1 self-center bg-bg-video p-1"
        style={{
          width: display.width,
          height: display.height,
          gridTemplateColumns: `repeat(${grid.cols}, minmax(0, 1fr))`,
          gridTemplateRows: `repeat(${grid.rows}, minmax(0, 1fr))`,
        }}
      >
        {cells.map((cell, index) => (
          <ContextTile
            key={index}
            cell={cell}
            index={index}
            open={index === openIndex}
            grid={grid}
            shape={shape}
            still={stills[cell.cameraIdentifier]}
            elementsOverride={index === openIndex ? openElements : undefined}
          />
        ))}
      </div>
    </section>
  );
}
