import { useParams } from 'react-router-dom';
import { LayoutGrid } from './LayoutGrid.js';

/**
 * Route wrapper for `/layouts/:layoutIdentifier` (spec 003 US2 + US3). All
 * rendering lives in `LayoutGrid` (spec 258 T063) — extracted so `WallPage`
 * can reuse it for whichever scene a wall is currently showing. This file
 * only turns the route param into `LayoutGrid`'s prop.
 */
export function CellPage() {
  const { layoutIdentifier = '' } = useParams<{ layoutIdentifier: string }>();
  return <LayoutGrid layoutIdentifier={layoutIdentifier} />;
}
