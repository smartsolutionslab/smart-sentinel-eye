import { useEffect, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import type {
  Layout,
  useCreateLayoutDraftMutation,
  useEditDraftRevisionMutation,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import {
  buildCells,
  cellsFromTiles,
  createGridDesignerResolver,
  tilesFromCells,
  type GridDesignerValue,
} from '../gridDesignerModel.js';
import type { LayoutEditTarget } from './layoutEditTarget.js';

const EMPTY_CREATE: GridDesignerValue = {
  name: '',
  grid: { rows: 1, cols: 1 },
  cells: buildCells(1, 1),
};

interface LayoutEditorFormInput {
  editTarget: LayoutEditTarget | undefined;
  currentChain: Layout | undefined;
  createLayoutDraft: ReturnType<typeof useCreateLayoutDraftMutation>[0];
  editDraftRevision: ReturnType<typeof useEditDraftRevisionMutation>[0];
  onOpenChange: (open: boolean) => void;
}

export function useLayoutEditorForm({
  editTarget,
  currentChain,
  createLayoutDraft,
  editDraftRevision,
  onOpenChange,
}: LayoutEditorFormInput) {
  const isEdit = editTarget !== undefined;
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
      // FR-002: kept as defence-in-depth alongside `saveBlocked` in useSaveGate (spec
      // 160 FR-002). It narrows `currentChain` for the `.version` read next,
      // and — now that the button is `aria-disabled` rather than natively
      // disabled — the form's submit guard, not this check, is what actually
      // keeps this unreached through the UI.
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

  return { form, register, errors, reset, defaultValues, onSubmit };
}
