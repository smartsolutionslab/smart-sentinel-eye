import type { OverlayElement } from '../../api/overlays.api.js';

/** Stub (spec 441, red phase): behaviourless until the implementation lands. */
export function layerName(_element: OverlayElement, _ordinal: number, _elements: readonly OverlayElement[]): string {
  return '';
}

/** Stub (spec 441, red phase). */
export function listPositionOf(_ordinal: number, _count: number): number {
  return 0;
}

/** Stub (spec 441, red phase). */
export function ordinalAt(_listPosition: number, _count: number): number {
  return 0;
}

/** Stub (spec 441, red phase). */
export function textOverlaps(_elements: readonly OverlayElement[]): Array<[number, number]> {
  return [];
}

/** Stub (spec 441, red phase). */
export function selectionAfterRemove(_removedListPosition: number, _newCount: number): number {
  return 0;
}
