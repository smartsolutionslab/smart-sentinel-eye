import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import { GridDesigner } from './GridDesigner.js';
import { buildCells, type GridDesignerValue } from './gridDesignerModel.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';

function camera(identifier: string, name: string): CameraSummary {
  return {
    cameraIdentifier: identifier,
    version: 1,
    fab: 'munich',
    name,
    rtspUrl: 'rtsp://10.0.5.1/h264',
    registeredAt: '2026-05-24T10:00:00Z',
    status: 'Registered',
  };
}

/** Mirrors GridDesignerRetainedOption.test.tsx's harness: one field, a 1×1 start. */
function Harness() {
  const form = useForm<GridDesignerValue>({
    defaultValues: { name: 'wall', grid: { rows: 1, cols: 1 }, cells: buildCells(1, 1) },
  });

  return (
    <GridDesigner
      form={form}
      cameras={[camera(CAMERA_A, 'Bay 4 Inlet')]}
      overlays={[]}
      camerasLoading={false}
      overlaysLoading={false}
    />
  );
}

/**
 * Spec 228 US1 (item 2, FR-001-FR-003). The grid-size picker must behave like
 * a real radio group: one tab stop in, arrow keys move the selection, click
 * still works, and every preset stays addressable by role and name.
 *
 * <p>
 * <b>Spec A4, checked rather than assumed.</b> user-event 14.6.6's roving
 * tab stop (`getTabDestination.js`) and arrow-key walking
 * (`event/behavior/keydown.js`) are both gated on
 * <c>isElementType(target, 'input', &#123; type: 'radio' &#125;)</c> — read
 * from the installed package, not the changelog. Neither fires for the
 * `button[role="radio"]` elements this repo ships today, so the keyboard
 * scenarios below exercise real jsdom behaviour and are expected to fail
 * against the current markup; they are not moved to Playwright.
 * </p>
 */
describe('GridDesigner — the grid-size picker as a keyboard radio group', () => {
  it('Is one tab stop into the group, and one tab stop out to the first Camera select', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.tab();
    expect(screen.getByRole('radio', { name: '1×1' })).toHaveFocus();

    await user.tab();
    expect(screen.getAllByLabelText('Camera')[0]).toHaveFocus();
  });

  it('Moves the selection with ArrowRight, and the tile grid follows', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.tab();
    await user.keyboard('{ArrowRight}');

    const next = screen.getByRole('radio', { name: '1×2' });
    expect(next).toHaveFocus();
    expect(next).toBeChecked();
    expect(screen.getAllByLabelText('Camera')).toHaveLength(2);
  });

  /** Regression: unrelated to the keyboard defect, kept green through the fix. */
  it('Still selects a preset by click', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole('radio', { name: '2×2' }));

    expect(screen.getByRole('radio', { name: '2×2' })).toBeChecked();
    expect(screen.getAllByLabelText('Camera')).toHaveLength(4);
  });

  /**
   * Regression: FR-002's stability guarantee for existing role/name queries.
   *
   * Spec 262 (#2607), ADR-0156: the cap rose from four cells to nine, so the
   * offered presets grow from four (`1×1`/`1×2`/`2×1`/`2×2`) to all nine
   * `rows,cols ∈ {1,2,3}` combinations (plan.md §6.1).
   */
  it('Is still addressable by role and name, grouped as "Grid size"', () => {
    render(<Harness />);

    expect(screen.getByRole('radio', { name: '1×1' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '1×2' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '1×3' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '2×1' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '2×2' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '2×3' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '3×1' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '3×2' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: '3×3' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Grid size' })).toBeInTheDocument();
  });
});
