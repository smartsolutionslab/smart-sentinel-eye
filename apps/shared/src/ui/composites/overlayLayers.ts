import type { OverlayElement } from '../../api/overlays.api.js';
import { DEFAULT_OVERLAY_COLOR } from '../../api/overlays.schema.js';

/**
 * Spec 441. Pure layer logic for the overlay editor's layer list. Ordinals are
 * 0-based paint order (the array order, highest paints last and so in front);
 * list positions are 1-based and front-most first.
 */

export function layerName(element: OverlayElement, ordinal: number, elements: readonly OverlayElement[]): string {
  if (element.kind === 'Text') {
    const trimmed = element.text.trim();
    return trimmed === '' ? 'Empty label' : trimmed;
  }
  let count = 0;
  for (let index = 0; index <= ordinal; index++) {
    if (elements[index]?.kind === element.kind) count++;
  }
  return `${element.kind} ${count}`;
}

export function listPositionOf(ordinal: number, count: number): number {
  return count - ordinal;
}

export function ordinalAt(listPosition: number, count: number): number {
  return count - listPosition;
}

// Geometry is binary floating point: 0.1 + 0.2 exceeds 0.3, so two labels that touch at an edge
// would read as overlapping by one ulp without a tolerance far below any visible size.
const EPSILON = 1e-9;

/** Pairs `[i, j]`, `i < j`, of Text elements whose rectangles intersect with positive area. */
export function textOverlaps(elements: readonly OverlayElement[]): Array<[number, number]> {
  const pairs: Array<[number, number]> = [];
  for (let i = 0; i < elements.length; i++) {
    const a = elements[i]!;
    if (a.kind !== 'Text') continue;
    for (let j = i + 1; j < elements.length; j++) {
      const b = elements[j]!;
      if (b.kind !== 'Text') continue;
      if (
        a.normalizedX + EPSILON < b.normalizedX + b.normalizedWidth &&
        b.normalizedX + EPSILON < a.normalizedX + a.normalizedWidth &&
        a.normalizedY + EPSILON < b.normalizedY + b.normalizedHeight &&
        b.normalizedY + EPSILON < a.normalizedY + a.normalizedHeight
      ) {
        pairs.push([i, j]);
      }
    }
  }
  return pairs;
}

/** The 1-based list position to select once the row at `removedListPosition` is gone and `newCount` remain. */
export function selectionAfterRemove(removedListPosition: number, newCount: number): number {
  return Math.min(removedListPosition, newCount);
}

/** Spec 457. The element a menu choice adds; geometry is a starting point the operator moves. */
export function newElement(kind: OverlayElement['kind']): OverlayElement {
  if (kind === 'Text') {
    return {
      kind,
      color: DEFAULT_OVERLAY_COLOR,
      text: 'Overlay text',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
    };
  }
  return {
    kind,
    color: DEFAULT_OVERLAY_COLOR,
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.3,
  };
}

export function addedAnnouncement(kind: OverlayElement['kind'], count: number): string {
  return `${kind === 'Text' ? 'Label' : kind} added, position 1 of ${count}`;
}
