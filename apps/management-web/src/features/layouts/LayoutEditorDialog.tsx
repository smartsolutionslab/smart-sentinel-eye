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
import { useRef, useState, type ComponentRef, type FormEvent } from 'react';
import { useWatch } from 'react-hook-form';
import { GridDesigner } from './GridDesigner.js';
import { TileView } from './tileView/TileView.js';
import { useCanEditOverlays } from './tileView/useCanEditOverlays.js';
import { CameraFilterField } from './layoutEditor/CameraFilterField.js';
import { useLayoutChain } from './layoutEditor/useLayoutChain.js';
import { useLayoutEditorForm } from './layoutEditor/useLayoutEditorForm.js';
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

  // Spec 156 (issue #2372). The chain read is the only thing blocking Save
  // (FR-002 above), so an operator clicks Retry *in order to* Save — focus
  // lands there, not restored to wherever it was, when the operator's own
  // re-read succeeds.
  // `ComponentRef<'button'>`, not `HTMLButtonElement` (spec 154's own
  // `e2e/overlays.spec.ts` fix, bc30486f) — this app's eslint config has no
  // per-tag DOM lib globals, and naming the type literally trips `no-undef`;
  // widening the config would be the gate-weakening ADR-0144 rules out.
  const saveRef = useRef<ComponentRef<'button'>>(null);

  // Spec 160 (issue #2387) FR-002/FR-007/spec §11 A1. Computed once,
  // consumed by both the button's `aria-disabled` and the form's submit
  // guard below — one gate, one mechanism, rather than the two overlapping
  // conditions this used to state separately (here and in `onSubmit`'s own
  // `currentChain === undefined` check above).
  //
  // `knownCameras.size === 0`, not `cameraItems.length === 0` — that is
  // "nothing to assign" and not the same question as "nothing matched".
  // Reading `cameraItems` here disabled Save with no explanation on a form
  // whose tiles may all already be filled, and flickered on every keystroke
  // while each new filter fragment was in flight. `knownCameras` only ever
  // grows, so it answers the question actually being asked: has this dialog
  // ever seen a camera.
  //
  // `chainFetching` alongside `currentChain === undefined` (FR-002): a
  // version that has not been read, or is being re-read, must never be the
  // one Save submits. Its most common trigger is not the Reload button — it
  // is RTK Query applying `invalidatesTags` on a REJECTED mutation too: a
  // stale-version 409 from `editDraftRevision` starts a background chain
  // refetch while the dialog stays subscribed, exactly the moment an
  // operator is about to click Save again. Verified in phase 6 against a
  // real `LAYOUT_REVISION_STALE` 409: GET count 1 -> 2 immediately,
  // `currentData` still the old version, `isFetching` true, Save unavailable,
  // exactly one PATCH ever issued. Reload (`refetchChain()`) hits the same
  // gate but is the rarer path — both are cases where `currentData`
  // genuinely stays stale while a fetch for the same argument is in flight.
  // The Reload path is pinned by `LayoutEditorDialogChainRecovery.test.tsx`'s
  // FR-005 case; the REJECTED-mutation path is not pinned by a test in this
  // repo, so that half of the outcome is recorded here rather than asserted.
  //
  // `chainFailed` (FR-007): a refused re-read still LEAVES `currentData` at
  // the pre-re-read version — `queryThunk.rejected` writes only
  // `status`/`error` (`@reduxjs/toolkit` 2.12.0,
  // `dist/query/rtk-query.modern.mjs:1443-1455`) and `currentData` is that
  // raw substate `data` (`dist/query/react/rtk-query-react.modern.mjs:155`).
  // Without this term the gate reopens on a version already known stale, and
  // a click resubmits it for an identical second 409. For a Retry-originated
  // refusal, Retry stays the way out (`ChainRecoveryNotice`'s chain arm,
  // `chainArmActive` on `readFailed`) — but not for a Reload-originated one:
  // `chainArmActive` excludes `origin === 'reload'` precisely so a refused
  // Reload keeps its OWN arm mounted instead, and Reload itself is the way
  // out there (`ChainRecoveryNotice.tsx`'s `chainArmActive` definition).
  const saveBlocked =
    isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching || chainFailed));

  // FR-003: `aria-disabled` restores implicit form submission (a natively
  // disabled default button suppresses Enter-to-submit; `aria-disabled` does
  // not), so this guard — run on the form's submit event, before
  // `handleSubmit` — is the only thing left stopping a submit while
  // `saveBlocked` is true, on both routes: a click on Save, and Enter in a
  // text field.
  function handleFormSubmit(event: FormEvent) {
    if (saveBlocked) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  }

  const overlayItems = overlays?.published ?? [];

  // The tile view shows the form's own cells, saved or not, and writes an assigned overlay back into
  // them: it is the same unsaved state the designer's overlay select changes, and saves nothing.
  const [tileViewIndex, setTileViewIndex] = useState<number | null>(null);
  const watchedGrid = useWatch({ control: form.control, name: 'grid' });
  const watchedCells = useWatch({ control: form.control, name: 'cells' });
  const assignOverlay = (cellIndex: number, overlayIdentifier: string) =>
    form.setValue(`cells.${cellIndex}.overlayIdentifier`, overlayIdentifier, { shouldDirty: true });
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
