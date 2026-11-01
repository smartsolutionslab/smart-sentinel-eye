import type { LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';

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
