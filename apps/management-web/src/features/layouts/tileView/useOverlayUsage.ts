import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { DesignerCell } from '../gridDesignerModel';

export interface OverlayUsageInput {
  overlayIdentifier: string;
  /** The layout open in the designer; `undefined` while it has not been saved. */
  layoutIdentifier: string | undefined;
  /** The designer's current cells, which stand in for the open layout's saved tiles. */
  cells: readonly DesignerCell[];
}

export interface OverlayUsage {
  /** A lower bound: only layouts the operator can list are counted. `null` until known. */
  count: number | null;
  failed: boolean;
}

/** Tiles using the overlay in Draft and Published revisions (not Archived), the open layout read from the form. */
export function useOverlayUsage({ overlayIdentifier, layoutIdentifier, cells }: OverlayUsageInput): OverlayUsage {
  const { data, isError } = useListLayoutsQuery(undefined);
  if (data === undefined) return { count: null, failed: isError };

  const inOpenLayout = cells.filter((cell) => cell.overlayIdentifier === overlayIdentifier).length;
  // A draft usually repeats the published revision's tiles, so a layout counts by its fullest open
  // revision rather than by both.
  const elsewhere = data.chains
    .filter((layout) => layout.layoutIdentifier !== layoutIdentifier)
    .map((layout) =>
      Math.max(
        0,
        ...layout.revisions
          .filter((revision) => revision.state !== 'Archived')
          .map((revision) => revision.tiles.filter((tile) => tile.overlayIdentifier === overlayIdentifier).length),
      ),
    )
    .reduce((sum, count) => sum + count, 0);

  return { count: inOpenLayout + elsewhere, failed: false };
}
