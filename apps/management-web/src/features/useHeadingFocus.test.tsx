import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useHeadingFocus } from './useHeadingFocus.js';

describe('useHeadingFocus', () => {
  it('Focuses the element the heading ref is attached to', () => {
    const { result } = renderHook(() => useHeadingFocus());
    const heading = document.createElement('h1');
    heading.tabIndex = -1;
    document.body.append(heading);
    result.current.headingRef.current = heading;

    result.current.focusHeading();

    expect(document.activeElement).toBe(heading);
    heading.remove();
  });

  it('Does nothing, and does not throw, before the heading is mounted', () => {
    const { result } = renderHook(() => useHeadingFocus());

    expect(() => result.current.focusHeading()).not.toThrow();
    expect(document.activeElement).toBe(document.body);
  });

  it('Returns the same callback on every render', () => {
    const { result, rerender } = renderHook(() => useHeadingFocus());
    const first = result.current.focusHeading;

    rerender();

    expect(result.current.focusHeading).toBe(first);
  });
});
