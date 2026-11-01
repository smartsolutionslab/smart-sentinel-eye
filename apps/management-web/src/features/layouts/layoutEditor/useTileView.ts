import { useState } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import type { GridDesignerValue } from '../gridDesignerModel.js';

export function useTileView(form: UseFormReturn<GridDesignerValue>) {
  // The tile view shows the form's own cells, saved or not, and writes an assigned overlay back into
  // them: it is the same unsaved state the designer's overlay select changes, and saves nothing.
  const [tileViewIndex, setTileViewIndex] = useState<number | null>(null);
  const watchedGrid = useWatch({ control: form.control, name: 'grid' });
  const watchedCells = useWatch({ control: form.control, name: 'cells' });
  const assignOverlay = (cellIndex: number, overlayIdentifier: string) =>
    form.setValue(`cells.${cellIndex}.overlayIdentifier`, overlayIdentifier, { shouldDirty: true });
  return { tileViewIndex, setTileViewIndex, watchedGrid, watchedCells, assignOverlay };
}
