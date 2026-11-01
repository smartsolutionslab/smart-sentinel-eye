import {
  useCreateLayoutDraftMutation,
  useEditDraftRevisionMutation,
  type LayoutTile,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import { useListOverlaysQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { ChainRecoveryNotice } from '@smart-sentinel-eye/shared/ui/composites/ChainRecoveryNotice';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { GridDesigner } from './GridDesigner.js';
import { TileView } from './tileView/TileView.js';
import { useCanEditOverlays } from './tileView/useCanEditOverlays.js';
import { CameraFilterField } from './layoutEditor/CameraFilterField.js';
import { useLayoutChain } from './layoutEditor/useLayoutChain.js';
import { useLayoutEditorForm } from './layoutEditor/useLayoutEditorForm.js';
import { useSaveGate } from './layoutEditor/useSaveGate.js';
import { useTileView } from './layoutEditor/useTileView.js';
import { useCameraPicker } from './layoutEditor/useCameraPicker.js';
import { useLatestAccessToken } from './layoutEditor/useLatestAccessToken.js';
import { useResetMutationsOnClose } from './layoutEditor/useResetMutationsOnClose.js';

/**
 * The revision the dialog edits in edit-after-publish (US4). The page branches
 * a new draft off the Published chain first, then hands the new draft's
 * revision number plus the baseline grid+tiles (branch copies them verbatim)
 * so the designer opens pre-loaded.
 */
export interface LayoutEditTarget {
  layoutIdentifier: string;
  revisionNumber: number;
  name: string;
  grid: { rows: number; cols: number };
  tiles: LayoutTile[];
}

export interface LayoutEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** When set the dialog edits an existing draft (US4); otherwise it creates. */
  editTarget?: LayoutEditTarget;
}

export function LayoutEditorDialog({ open, onOpenChange, editTarget }: LayoutEditorDialogProps) {
  const isEdit = editTarget !== undefined;
  const [createLayoutDraft, createState] = useCreateLayoutDraftMutation();
  const [editDraftRevision, editState] = useEditDraftRevisionMutation();

  const { currentChain, chainFailed, chainFetching, refetchChain } = useLayoutChain(editTarget?.layoutIdentifier);
  const { isLoading, error } = isEdit ? editState : createState;

  useResetMutationsOnClose(open, createState, editState);

  const {
    cameraFilter,
    setCameraFilter,
    cameras,
    cameraItems,
    camerasLoading,
    camerasFailed,
    camerasTruncated,
    knownCameras,
    filtering,
    cameraFilterStatus,
    cameraNoticeId,
  } = useCameraPicker(open);
  const { data: overlays, isLoading: overlaysLoading } = useListOverlaysQuery('Published');

  const { form, register, errors, reset, defaultValues, onSubmit } = useLayoutEditorForm({
    editTarget,
    currentChain,
    createLayoutDraft,
    editDraftRevision,
    onOpenChange,
  });

  // "Try again" is the wrong advice on a stale conflict — resubmitting replays
  // the same stale intent over whoever wrote in between, which is the overwrite
  // this whole mechanism exists to prevent. A name collision keeps it, because
  // there retrying with a different name is exactly what the operator should do.
  const staleConflict = isStaleConflict(error);
  const backendError = problemDetail(
    error,
    staleConflict ? CONFLICT_FALLBACK : 'Could not save the layout. Try again.',
  );

  const { saveRef, saveBlocked, handleFormSubmit } = useSaveGate({
    isLoading,
    knownCameras,
    isEdit,
    currentChain,
    chainFetching,
    chainFailed,
    onSubmit,
  });

  const overlayItems = overlays?.published ?? [];

  const { tileViewIndex, setTileViewIndex, watchedGrid, watchedCells, assignOverlay } = useTileView(form);
  const canEditOverlays = useCanEditOverlays();
  const getToken = useLatestAccessToken();

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset(defaultValues);
        onOpenChange(next);
      }}
      title={isEdit ? 'Edit layout' : 'New layout'}
      description={
        isEdit
          ? 'Adjust the grid and tiles, then save the draft.'
          : 'Name the wall, pick a grid size, and assign a camera to each tile. It starts as a draft.'
      }
    >
      <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
        {!isEdit && (
          <FormField label="Name" htmlFor="layout-name" error={errors.name?.message}>
            <Input id="layout-name" autoFocus {...register('name')} />
          </FormField>
        )}
        <CameraFilterField
          cameraFilter={cameraFilter}
          setCameraFilter={setCameraFilter}
          camerasTruncated={camerasTruncated}
          shownCount={cameraItems.length}
          totalCount={cameras?.count}
          cameraNoticeId={cameraNoticeId}
          cameraFilterStatus={cameraFilterStatus}
        />
        <GridDesigner
          form={form}
          cameras={cameraItems}
          knownCameras={knownCameras}
          overlays={overlayItems}
          camerasLoading={camerasLoading}
          overlaysLoading={overlaysLoading}
          camerasFailed={camerasFailed}
          cameraFilterActive={filtering}
          cameraNoticeId={cameraNoticeId}
          onOpenTileView={setTileViewIndex}
        />
        <ChainRecoveryNotice
          noun="layout"
          readFailed={chainFailed}
          reReading={chainFetching}
          onReRead={() => void refetchChain()}
          backendError={backendError}
          offerReload={staleConflict}
          onReadRecovered={() => saveRef.current?.focus()}
        />
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          {/*
            `unavailable` (ADR-0151), not the native `disabled` attribute —
            it emits `aria-disabled` and the dimming classes without blurring
            the focused element, the same finding this repo already shipped
            twice: `ChainRecoveryNotice.tsx:31-33` (spec 156) and
            `OverlayEditor.tsx:649-657` (spec 154). `saveBlocked` above is
            what defines the gate; `handleFormSubmit` on the form is what
            actually enforces it now that the button stays clickable.
          */}
          <Button ref={saveRef} type="submit" unavailable={saveBlocked} className="aria-disabled:cursor-progress">
            {isLoading ? 'Saving…' : isEdit ? 'Save draft' : 'Save as draft'}
          </Button>
        </div>
      </form>
      {/* After the form, not inside it: a portal keeps React's event tree, and the view's buttons must not submit it. */}
      <TileView
        open={tileViewIndex !== null}
        layoutIdentifier={editTarget?.layoutIdentifier}
        onOpenChange={(next) => {
          if (!next) setTileViewIndex(null);
        }}
        grid={watchedGrid}
        cells={watchedCells}
        cellIndex={tileViewIndex ?? 0}
        onAssignOverlay={assignOverlay}
        getToken={getToken}
        canEditOverlays={canEditOverlays}
      />
    </Dialog>
  );
}
