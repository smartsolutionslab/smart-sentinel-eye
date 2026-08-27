import { useListCamerasQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import {
  useCreateLayoutDraftMutation,
  useEditDraftRevisionMutation,
  useGetLayoutQuery,
  type LayoutTile,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { useListOverlaysQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { useEffect, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { GridDesigner } from './GridDesigner.js';
import {
  buildCells,
  cellsFromTiles,
  createGridDesignerResolver,
  tilesFromCells,
  type GridDesignerValue,
} from './gridDesignerModel.js';

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

const EMPTY_CREATE: GridDesignerValue = {
  name: '',
  grid: { rows: 1, cols: 1 },
  cells: buildCells(1, 1),
};

export function LayoutEditorDialog({ open, onOpenChange, editTarget }: LayoutEditorDialogProps) {
  const isEdit = editTarget !== undefined;
  const [createLayoutDraft, createState] = useCreateLayoutDraftMutation();
  const [editDraftRevision, editState] = useEditDraftRevisionMutation();

  // The If-Match version has to be the chain's *current* one (ADR-0113).
  // The page branches a new draft before opening this dialog, and that branch
  // is itself a write, so the version the page held is already one behind.
  // Reading it back rather than inferring "+1" keeps the client from doing
  // arithmetic on server state -- and still fails correctly if another
  // operator moves the chain while the dialog is open.
  const { data: currentChain, refetch: refetchChain } = useGetLayoutQuery(editTarget?.layoutIdentifier ?? skipToken);
  const { isLoading, error, reset: resetMutationState } = isEdit ? editState : createState;

  // Drop any prior backend error when the dialog closes so a stale banner
  // doesn't greet the operator on the next open.
  useEffect(() => {
    if (!open) resetMutationState();
  }, [open, resetMutationState]);

  const { data: cameras, isLoading: camerasLoading } = useListCamerasQuery({ limit: 50 });
  const { data: overlays, isLoading: overlaysLoading } = useListOverlaysQuery('Published');

  const defaultValues = useMemo<GridDesignerValue>(() => {
    if (editTarget === undefined) return EMPTY_CREATE;
    return {
      name: editTarget.name,
      grid: editTarget.grid,
      cells: cellsFromTiles(editTarget.grid.rows, editTarget.grid.cols, editTarget.tiles),
    };
  }, [editTarget]);

  const form = useForm<GridDesignerValue>({
    resolver: createGridDesignerResolver(isEdit ? 'edit' : 'create'),
    defaultValues,
  });
  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = form;

  // Re-seed when the target (or create/edit mode) changes between opens.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);

  const onSubmit = handleSubmit(async (value) => {
    const tiles = tilesFromCells(value.cells);
    if (editTarget !== undefined) {
      if (currentChain === undefined) return;
      const result = await editDraftRevision({
        layoutIdentifier: editTarget.layoutIdentifier,
        revisionNumber: editTarget.revisionNumber,
        version: currentChain.version,
        grid: value.grid,
        tiles,
      });
      if (!('error' in result)) {
        reset(defaultValues);
        onOpenChange(false);
      }
      return;
    }
    const result = await createLayoutDraft({ name: value.name, grid: value.grid, tiles });
    if (!('error' in result)) {
      reset(EMPTY_CREATE);
      onOpenChange(false);
    }
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
  const cameraItems = cameras?.items ?? [];
  const overlayItems = overlays?.published ?? [];

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
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        {!isEdit && (
          <FormField label="Name" htmlFor="layout-name" error={errors.name?.message}>
            <Input id="layout-name" autoFocus {...register('name')} />
          </FormField>
        )}
        <GridDesigner
          form={form}
          cameras={cameraItems}
          overlays={overlayItems}
          camerasLoading={camerasLoading}
          overlaysLoading={overlaysLoading}
        />
        {backendError !== null && (
          <p role="alert" className="text-sm text-accent-fault">
            {backendError}{' '}
            {staleConflict && (
              // Reload, never retry. Refetching the chain replaces the version
              // the dialog would resubmit with the one the other writer left,
              // so the operator reapplies against what is actually stored.
              <button type="button" className="underline" onClick={() => void refetchChain()}>
                Reload
              </button>
            )}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" disabled={isLoading || cameraItems.length === 0}>
            {isLoading ? 'Saving…' : isEdit ? 'Save draft' : 'Save as draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
