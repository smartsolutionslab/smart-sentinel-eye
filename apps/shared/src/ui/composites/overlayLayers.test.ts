import { describe, expect, it } from 'vitest';
import type { OverlayElement } from '../../api/overlays.api.js';
import { layerName, listPositionOf, ordinalAt, selectionAfterRemove, textOverlaps } from './overlayLayers.js';

/**
 * Spec 441 (issue #3072) C1. New behaviour, RED (ADR-0139): the pure layer
 * logic. List positions are 1-based and front-most first, so the element with
 * the highest ordinal is position 1.
 */

function text(
  value: string,
  geometry: { x?: number; y?: number; width?: number; height?: number } = {},
): OverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: value,
    normalizedX: geometry.x ?? 0.1,
    normalizedY: geometry.y ?? 0.1,
    normalizedWidth: geometry.width ?? 0.3,
    normalizedHeight: geometry.height ?? 0.1,
    fontSizePx: 32,
  };
}

function shape(
  kind: 'Box' | 'Ellipse',
  geometry: { x?: number; y?: number; width?: number; height?: number } = {},
): OverlayElement {
  return {
    kind,
    color: '#FF0000FF',
    normalizedX: geometry.x ?? 0.1,
    normalizedY: geometry.y ?? 0.1,
    normalizedWidth: geometry.width ?? 0.3,
    normalizedHeight: geometry.height ?? 0.1,
  };
}

describe('layerName', () => {
  it('Names a Text element by its trimmed text', () => {
    const elements = [text('  Line 1  ')];
    expect(layerName(elements[0]!, 0, elements)).toBe('Line 1');
  });

  it('Names a Text element with blank text "Empty label"', () => {
    const elements = [text('   ')];
    expect(layerName(elements[0]!, 0, elements)).toBe('Empty label');
  });

  it('Numbers each Box by its 1-based count among Boxes in ordinal order', () => {
    const elements = [shape('Box'), text('A'), shape('Box')];
    expect(layerName(elements[0]!, 0, elements)).toBe('Box 1');
    expect(layerName(elements[2]!, 2, elements)).toBe('Box 2');
  });

  it('Numbers Ellipses independently of Boxes', () => {
    const elements = [shape('Box'), shape('Ellipse'), shape('Ellipse')];
    expect(layerName(elements[1]!, 1, elements)).toBe('Ellipse 1');
    expect(layerName(elements[2]!, 2, elements)).toBe('Ellipse 2');
  });
});

describe('list position and ordinal', () => {
  it('Puts the highest ordinal at the top of the list (position 1)', () => {
    expect(listPositionOf(2, 3)).toBe(1);
  });

  it('Puts ordinal 0 at the bottom of the list', () => {
    expect(listPositionOf(0, 3)).toBe(3);
  });

  it('Maps a list position back to its ordinal', () => {
    expect(ordinalAt(1, 3)).toBe(2);
    expect(ordinalAt(3, 3)).toBe(0);
  });

  it('Round-trips every ordinal of a set', () => {
    for (let ordinal = 0; ordinal < 8; ordinal++) {
      expect(ordinalAt(listPositionOf(ordinal, 8), 8)).toBe(ordinal);
    }
  });
});

describe('textOverlaps', () => {
  it('Reports two Text elements whose rectangles intersect with positive area, as [i, j] with i < j', () => {
    const elements = [text('A', { x: 0.1, y: 0.1, width: 0.3, height: 0.1 }), text('B', { x: 0.2, y: 0.15 })];
    expect(textOverlaps(elements)).toEqual([[0, 1]]);
  });

  it('Does not count rectangles that only touch at an edge', () => {
    const elements = [text('A', { x: 0.1, width: 0.3 }), text('B', { x: 0.4, width: 0.3 })];
    expect(textOverlaps(elements)).toEqual([]);
  });

  it('Does not count rectangles that only touch at a corner', () => {
    const elements = [text('A', { x: 0.1, y: 0.1, width: 0.2, height: 0.2 }), text('B', { x: 0.3, y: 0.3 })];
    expect(textOverlaps(elements)).toEqual([]);
  });

  it('Does not count disjoint rectangles', () => {
    const elements = [text('A', { x: 0.0, y: 0.0, width: 0.2 }), text('B', { x: 0.6, y: 0.6 })];
    expect(textOverlaps(elements)).toEqual([]);
  });

  it('Excludes Box and Ellipse elements even when they overlap a Text element', () => {
    const elements = [text('A'), shape('Box', { x: 0, y: 0, width: 1, height: 1 }), shape('Ellipse')];
    expect(textOverlaps(elements)).toEqual([]);
  });

  it('Reports every overlapping pair among three Text elements, in (i, j) order', () => {
    const elements = [text('A'), text('B'), text('C')];
    expect(textOverlaps(elements)).toEqual([
      [0, 1],
      [0, 2],
      [1, 2],
    ]);
  });

  it('Ignores a Box standing between two overlapping Text elements, keeping their ordinals', () => {
    const elements = [text('A'), shape('Box'), text('B')];
    expect(textOverlaps(elements)).toEqual([[0, 2]]);
  });
});

describe('selectionAfterRemove', () => {
  it('Selects the row now at the same list position', () => {
    expect(selectionAfterRemove(1, 2)).toBe(1);
    expect(selectionAfterRemove(2, 3)).toBe(2);
  });

  it('Selects the row above when the removed row was last in the list', () => {
    expect(selectionAfterRemove(3, 2)).toBe(2);
  });

  it('Selects the only remaining row', () => {
    expect(selectionAfterRemove(2, 1)).toBe(1);
  });
});
