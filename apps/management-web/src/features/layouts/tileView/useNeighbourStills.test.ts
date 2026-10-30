import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import type { DesignerCell } from '../gridDesignerModel';
import { nearestFirst, useNeighbourStills } from './useNeighbourStills';

function cell(row: number, col: number, camera: string, rowSpan = 1, colSpan = 1): DesignerCell {
  return { cameraIdentifier: camera, overlayIdentifier: '', row, col, rowSpan, colSpan };
}

describe('nearestFirst — the order the stills are taken in', () => {
  it('puts the open tile first, then the others by distance, ties by row then column', () => {
    // 2×2: the open tile is bottom-left (index 2). Top-left and bottom-right are one step away,
    // top-right is two. Of the two equal ones the upper (row 0) comes first.
    const cells = [cell(0, 0, 'a'), cell(0, 1, 'b'), cell(1, 0, 'c'), cell(1, 1, 'd')];
    expect(nearestFirst(cells, 2)).toEqual(['c', 'a', 'd', 'b']);
  });

  it('measures a spanning tile from its middle, not its corner', () => {
    // The open tile spans the left column; the tall tile's middle is level with the lower neighbour.
    const cells = [cell(0, 0, 'tall', 2, 1), cell(0, 1, 'top'), cell(1, 1, 'bottom')];
    expect(nearestFirst(cells, 0)).toEqual(['tall', 'top', 'bottom']);
  });

  it('names a camera once however many tiles show it', () => {
    const cells = [cell(0, 0, 'a'), cell(0, 1, 'a'), cell(0, 2, 'b')];
    expect(nearestFirst(cells, 0)).toEqual(['a', 'b']);
  });
});

describe('useNeighbourStills — one capture queue for the whole view', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('starts with the first camera only, and moves on after each result, never two at once', () => {
    const { result } = renderHook(() => useNeighbourStills(['a', 'b', 'c']));
    expect(result.current.capturingCamera).toBe('a');

    act(() => result.current.onCaptured('data:a'));
    expect(result.current.capturingCamera).toBe('b');
    expect(result.current.stills).toEqual({ a: 'data:a' });

    act(() => result.current.onFailed());
    expect(result.current.capturingCamera).toBe('c');
    expect(result.current.failed.has('b')).toBe(true);

    act(() => result.current.onCaptured('data:c'));
    expect(result.current.capturingCamera).toBeNull();
    expect(result.current.stills).toEqual({ a: 'data:a', c: 'data:c' });
  });

  it('gives a camera that does not answer ten seconds, then marks it failed and goes on', () => {
    const { result } = renderHook(() => useNeighbourStills(['a', 'b']));

    act(() => vi.advanceTimersByTime(9_999));
    expect(result.current.capturingCamera).toBe('a');

    act(() => vi.advanceTimersByTime(1));
    expect(result.current.failed.has('a')).toBe(true);
    expect(result.current.capturingCamera).toBe('b');
  });

  it('keeps what it has when the list is re-rendered, and takes only the new cameras', () => {
    const { result, rerender } = renderHook(({ order }) => useNeighbourStills(order), {
      initialProps: { order: ['a'] },
    });
    act(() => result.current.onCaptured('data:a'));
    expect(result.current.capturingCamera).toBeNull();

    rerender({ order: ['a', 'b'] });

    expect(result.current.stills).toEqual({ a: 'data:a' });
    expect(result.current.capturingCamera).toBe('b');
  });
});
