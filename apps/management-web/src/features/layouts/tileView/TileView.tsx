import { skipToken } from '@reduxjs/toolkit/query';
import { useMemo, useState } from 'react';
import {
  useBranchDraftOverlayRevisionMutation,
  useGetOverlayQuery,
  useListOverlaysQuery,
  type OverlayElement,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { FrameGrabber } from '@smart-sentinel-eye/shared/ui/composites/FrameGrabber';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { OverlayDraftForm } from '../../overlays/OverlayDraftForm';
import { SELECT_CLASS, type DesignerCell, type GridDesignerValue } from '../gridDesignerModel';
import { DEFAULT_DISPLAY_SHAPE, DISPLAY_SHAPES, cellSizePx, fitWithinPx, type SizePx } from './displayShapes';
import { revisionToShow, useResolvedElements } from './shownOverlay';
import { TileFrame } from './TileFrame';
import { TileViewContext } from './TileViewContext';
import { TileViewReadout } from './TileViewReadout';
import { nearestFirst, useNeighbourStills } from './useNeighbourStills';
import { useOverlayUsage } from './useOverlayUsage';

/** The display the tile is sized on, before it is fitted into the dialog. */
const NOMINAL_DISPLAY: SizePx = { width: 1920, height: 1080 };
const PREVIEW_AREA: SizePx = { width: 800, height: 450 };

export interface TileViewProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The designer form's current grid and cells, saved or not. */
  grid: GridDesignerValue['grid'];
  cells: readonly DesignerCell[];
  cellIndex: number;
  /** The layout open in the designer; absent while it has not been saved. */
  layoutIdentifier?: string;
  onAssignOverlay: (cellIndex: number, overlayIdentifier: string) => void;
  getToken: () => Promise<string | null>;
  canEditOverlays: boolean;
}

/** Module scope: `FrameGrabber` takes the stream as a prop and must not remount with this view. */
function BackdropGrabber({
  cameraIdentifier,
  getToken,
  onCaptured,
  onFailed,
}: {
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  onCaptured: (dataUrl: string) => void;
  onFailed: () => void;
}) {
  const { currentData: stream } = useGetStreamQuery(cameraIdentifier);
  return (
    <FrameGrabber
      cameraIdentifier={cameraIdentifier}
      stream={stream}
      getToken={getToken}
      onCaptured={onCaptured}
      onFailed={onFailed}
    />
  );
}

type TileViewBodyProps = Omit<TileViewProps, 'open' | 'onOpenChange'> & {
  cell: DesignerCell;
  onClose: () => void;
};

function TileViewBody({
  grid,
  cells,
  cell,
  cellIndex,
  layoutIdentifier,
  onAssignOverlay,
  getToken,
  canEditOverlays,
  onClose,
}: TileViewBodyProps) {
  const [shapeKey, setShapeKey] = useState(DEFAULT_DISPLAY_SHAPE.key);
  const shape = DISPLAY_SHAPES.find((candidate) => candidate.key === shapeKey) ?? DEFAULT_DISPLAY_SHAPE;

  const { overlayIdentifier } = cell;
  const { data: overlay } = useGetOverlayQuery(overlayIdentifier === '' ? skipToken : overlayIdentifier);
  const { data: published } = useListOverlaysQuery('Published');
  const { revision, notice: overlayNotice } = revisionToShow(overlayIdentifier === '' ? undefined : overlay);

  const order = useMemo(() => nearestFirst(cells, cellIndex), [cells, cellIndex]);
  const queue = useNeighbourStills(order);
  const backdropFailed = queue.failed.has(cell.cameraIdentifier);

  const [branchDraft] = useBranchDraftOverlayRevisionMutation();
  const [editing, setEditing] = useState(false);
  const [making, setMaking] = useState(false);
  const [liveElements, setLiveElements] = useState<OverlayElement[] | null>(null);
  const [draftError, setDraftError] = useState<unknown>(undefined);
  const usage = useOverlayUsage({ overlayIdentifier, layoutIdentifier, cells });
  const draft = overlay?.revisions
    .filter((candidate) => candidate.state === 'Draft')
    .sort((a, b) => b.revisionNumber - a.revisionNumber)[0];

  // An existing draft is edited as it is; otherwise the published revision is branched into one,
  // so the published revision itself is never touched.
  async function startEditing() {
    if (making) return;
    setDraftError(undefined);
    if (draft === undefined && overlay !== undefined) {
      setMaking(true);
      const result = await branchDraft({ overlayIdentifier, version: overlay.version });
      setMaking(false);
      if ('error' in result) {
        setDraftError(result.error);
        return;
      }
    }
    setEditing(true);
  }
  function stopEditing() {
    setEditing(false);
    setLiveElements(null);
  }
  const editingDraft = editing ? draft : undefined;
  // Stable across the renders an edit causes: the form re-seeds itself whenever this changes.
  const editTarget = useMemo(
    () =>
      editingDraft === undefined
        ? undefined
        : {
            overlayIdentifier,
            revisionNumber: editingDraft.revisionNumber,
            name: overlay?.name ?? '',
            elements: editingDraft.elements,
          },
    [overlayIdentifier, editingDraft, overlay?.name],
  );

  const shownElements = useMemo<readonly OverlayElement[]>(
    () => (editingDraft !== undefined && liveElements !== null ? liveElements : (revision?.elements ?? [])),
    [editingDraft, liveElements, revision],
  );
  const { elements, resolvers } = useResolvedElements(shownElements);

  const tile = fitWithinPx(cellSizePx(shape, grid, cell, NOMINAL_DISPLAY), PREVIEW_AREA);
  const notices = [
    overlayNotice,
    overlayIdentifier !== '' && !canEditOverlays
      ? 'You are not allowed to change overlays, so the editor is not offered.'
      : null,
    backdropFailed ? 'The camera picture could not be captured. A neutral background is shown instead.' : null,
  ].filter((notice): notice is string => notice !== null);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-4">
        <Button type="button" variant="secondary" onClick={onClose}>
          Close
        </Button>
        <label className="flex flex-col gap-1 text-xs font-medium text-fg-muted">
          Display shape
          <select className={SELECT_CLASS} value={shape.key} onChange={(event) => setShapeKey(event.target.value)}>
            {DISPLAY_SHAPES.map((option) => (
              <option key={option.key} value={option.key}>
                {option.key}
              </option>
            ))}
          </select>
        </label>
        {overlayIdentifier === '' && (
          <label className="flex flex-col gap-1 text-xs font-medium text-fg-muted">
            Assign an overlay
            <select
              className={SELECT_CLASS}
              value=""
              onChange={(event) => {
                if (event.target.value !== '') onAssignOverlay(cellIndex, event.target.value);
              }}
            >
              <option value="">Choose an overlay</option>
              {(published?.published ?? []).map((option) => (
                <option key={option.overlayIdentifier} value={option.overlayIdentifier}>
                  {option.name}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      <div data-testid="tile-view-display" data-shape={shape.key} className="flex justify-center">
        <TileFrame
          widthPx={tile.width}
          heightPx={tile.height}
          backdropUrl={queue.stills[cell.cameraIdentifier] ?? null}
          elements={elements}
        />
      </div>

      <TileViewContext
        grid={grid}
        cells={cells}
        openIndex={cellIndex}
        shape={shape}
        stills={queue.stills}
        openElements={editingDraft !== undefined && liveElements !== null ? liveElements : undefined}
      />

      <div role="status" className="text-sm text-fg-muted">
        {notices.map((notice) => (
          <p key={notice}>{notice}</p>
        ))}
      </div>

      <TileViewReadout elements={elements} />

      {overlayIdentifier !== '' && (
        <div className="flex flex-col gap-2 text-sm">
          {usage.count !== null && (
            <p>{`Used on at least ${usage.count} tile${usage.count === 1 ? '' : 's'} in layouts you can see.`}</p>
          )}
          {usage.count !== null && usage.count > 1 && <p>This overlay is shared: changing it changes all of them.</p>}
          <a
            className="text-accent-primary underline"
            href={
              draft === undefined
                ? '/overlays'
                : `/overlays/${overlayIdentifier}/revisions/${draft.revisionNumber}/edit`
            }
            target="_blank"
            rel="noreferrer"
          >
            Open in the overlay editor
          </a>
          {canEditOverlays && !editing && overlay !== undefined && (
            <div>
              <Button type="button" variant="secondary" unavailable={making} onClick={() => void startEditing()}>
                Edit this overlay
              </Button>
            </div>
          )}
          {draftError !== undefined && (
            <p role="alert" className="text-accent-fault">
              {problemDetail(
                draftError,
                isStaleConflict(draftError) ? CONFLICT_FALLBACK : 'Could not make a draft of the overlay. Try again.',
              )}
            </p>
          )}
        </div>
      )}

      {editTarget !== undefined && (
        <OverlayDraftForm
          editTarget={editTarget}
          canvasWidthPx={tile.width}
          canvasHeightPx={(tile.width * 9) / 16}
          backdropCameraIdentifier={cell.cameraIdentifier}
          onElementsChange={setLiveElements}
          onDone={stopEditing}
          onCancel={stopEditing}
        />
      )}

      {resolvers}
      {queue.capturingCamera !== null && (
        <BackdropGrabber
          key={queue.capturingCamera}
          cameraIdentifier={queue.capturingCamera}
          getToken={getToken}
          onCaptured={queue.onCaptured}
          onFailed={queue.onFailed}
        />
      )}
    </div>
  );
}

/**
 * The body is mounted only while the dialog is open, so closing releases the capture and the
 * picture, and reopening starts a fresh one. The key restarts it if the tile's camera changes.
 */
export function TileView({ open, onOpenChange, cells, cellIndex, ...rest }: TileViewProps) {
  const cell = cells[cellIndex];
  if (cell === undefined) return null;
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Tile view"
      description="The tile as it appears on the wall."
      size="wide"
    >
      {open && (
        <TileViewBody
          key={cell.cameraIdentifier}
          cell={cell}
          cells={cells}
          cellIndex={cellIndex}
          onClose={() => onOpenChange(false)}
          {...rest}
        />
      )}
    </Dialog>
  );
}
