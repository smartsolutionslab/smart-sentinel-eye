import type { OverlayElement } from '../../api/overlays.api.js';

export interface OverlayLayerListProps {
  elements: readonly OverlayElement[];
  rowIds: readonly string[];
  selectedId: string;
  onSelect: (id: string) => void;
  onMove: (ordinal: number, delta: 1 | -1) => void;
  onRemove: (ordinal: number) => void;
  onAddLabel: () => void;
  canAdd: boolean;
  canRemove: boolean;
}

/** Stub (spec 441, red phase): renders nothing until the implementation lands. */
export function OverlayLayerList(_props: OverlayLayerListProps) {
  return null;
}
