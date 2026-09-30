import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TileAlignmentBadge } from './TileAlignmentBadge.js';

/**
 * Spec 297 (issue #2635) T003. Characterisation, written and observed green on
 * the untouched tree (`ab030e5a`) before any source edit — no test file named
 * `tile-out-of-alignment` or `TileAlignmentBadge` existed before this one
 * (spec §1). Pins what US2's conversion to a shared `Badge` must keep: role,
 * test id, `data-camera` and the text (spec §2 US2, FR-005). Must pass
 * unmodified after the conversion (ADR-0139/0144 §6).
 */
describe('TileAlignmentBadge', () => {
  it('Renders role status, test id tile-out-of-alignment, data-camera and the out-of-sync text', () => {
    render(<TileAlignmentBadge camera="cam-7" />);

    const badge = screen.getByRole('status');
    expect(badge).toHaveAttribute('data-testid', 'tile-out-of-alignment');
    expect(badge).toHaveAttribute('data-camera', 'cam-7');
    expect(badge).toHaveTextContent('Not in sync with the wall');
  });

  /**
   * Spec 297 (issue #2635) T009, US2, FR-005/FR-010. RED: today's className
   * (`TileAlignmentBadge.tsx:20`) is `bg-accent-warning/30` — a translucent
   * call-site alpha fill over live video (ADR-0146 violation, spec §1). After
   * the conversion it must be the shared Badge's warning tone at the wall
   * size (`md`), with no alpha modifier.
   */
  it('Uses the Badge warning tone at the wall size, with no alpha modifier', () => {
    render(<TileAlignmentBadge camera="cam-7" />);

    const badge = screen.getByRole('status');
    expect(badge.className).toContain('bg-accent-warning-subtle');
    expect(badge.className).toContain('px-3');
    expect(badge.className).toContain('py-1');
    const offending = badge.className.split(/\s+/).filter((token) => /\/\d+$/.test(token));
    expect(offending).toEqual([]);
  });
});
