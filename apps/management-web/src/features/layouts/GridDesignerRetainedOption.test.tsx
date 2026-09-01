import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { useForm } from 'react-hook-form';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import { GridDesigner } from './GridDesigner.js';
import { buildCells, type GridDesignerValue } from './gridDesignerModel.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';

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

/**
 * A filter must not blank a tile that is already assigned (spec 055).
 *
 * <para>
 * <b>This is the failure the spec did not name.</b> Options come from the
 * server's matches, so a fragment excluding a camera some tile already holds
 * leaves a select whose value has no matching option. It paints blank while the
 * form still carries the value — so the operator sees a tile they filled become
 * empty, fills it again, and the filter has quietly edited their layout.
 * </para>
 *
 * <para>
 * It is tested at <c>GridDesigner</c> rather than through the dialog because
 * that is where the option list is built, and because the dialog would need a
 * mocked query mid-filter to reach the same state.
 * </para>
 */
describe('GridDesigner — a filtered-out camera stays on its own tile', () => {
  function Harness({
    cameras,
    knownCameras,
    selected,
  }: {
    cameras: ReadonlyArray<CameraSummary>;
    knownCameras?: ReadonlyMap<string, CameraSummary>;
    selected: string;
  }) {
    const cells = buildCells(1, 1);
    cells[0]!.cameraIdentifier = selected;

    const form = useForm<GridDesignerValue>({
      defaultValues: { name: 'wall', grid: { rows: 1, cols: 1 }, cells },
    });

    return (
      <GridDesigner
        form={form}
        cameras={cameras}
        knownCameras={knownCameras}
        overlays={[]}
        camerasLoading={false}
        overlaysLoading={false}
      />
    );
  }

  it('Keeps the assigned camera selectable when the filter excludes it', () => {
    const assigned = camera(CAMERA_A, 'Line 2 Furnace');
    const matched = camera(CAMERA_B, 'Bay 4 Inlet');

    render(
      <Harness
        cameras={[matched]}
        knownCameras={new Map([[CAMERA_A, assigned]])}
        selected={CAMERA_A}
      />,
    );

    const select = screen.getByLabelText(/^Camera$/i);

    expect(select).toHaveValue(CAMERA_A);
    expect(within(select).getByRole('option', { name: /Line 2 Furnace/i })).toBeInTheDocument();
  });

  /**
   * The control's *value* is the part that matters. An option present but not
   * selected would still show the operator an empty tile.
   */
  it('Still shows the assigned camera as the chosen one', () => {
    const assigned = camera(CAMERA_A, 'Line 2 Furnace');

    render(
      <Harness
        cameras={[camera(CAMERA_B, 'Bay 4 Inlet')]}
        knownCameras={new Map([[CAMERA_A, assigned]])}
        selected={CAMERA_A}
      />,
    );

    const select = screen.getByLabelText(/^Camera$/i);

    expect(select).toHaveDisplayValue(/Line 2 Furnace/);
  });

  /**
   * Without the retained entry there is nothing to fall back to, and the tile
   * does go blank. Asserted so the guard above is known to be doing the work —
   * a test that passes with and without the mechanism proves nothing.
   */
  it('Goes blank without the retained camera, which is what the retention prevents', () => {
    render(
      <Harness
        cameras={[camera(CAMERA_B, 'Bay 4 Inlet')]}
        knownCameras={new Map()}
        selected={CAMERA_A}
      />,
    );

    const select = screen.getByLabelText(/^Camera$/i);

    expect(select).not.toHaveValue(CAMERA_A);
  });

  it('Leaves an unfiltered tile exactly as it was', () => {
    const assigned = camera(CAMERA_A, 'Line 2 Furnace');

    render(
      <Harness
        cameras={[assigned, camera(CAMERA_B, 'Bay 4 Inlet')]}
        knownCameras={new Map([[CAMERA_A, assigned]])}
        selected={CAMERA_A}
      />,
    );

    const select = screen.getByLabelText(/^Camera$/i);

    expect(select).toHaveValue(CAMERA_A);
    // Two cameras plus the empty placeholder — the retained one is not appended
    // a second time when it is already among the matches.
    //
    // Scoped to this select: the tile also carries an overlay chooser with a
    // placeholder of its own, so counting every option on the page counts that
    // too. The first version of this assertion did, and failed for a reason
    // that had nothing to do with the behaviour under test.
    expect(within(select).getAllByRole('option')).toHaveLength(3);
  });
});
