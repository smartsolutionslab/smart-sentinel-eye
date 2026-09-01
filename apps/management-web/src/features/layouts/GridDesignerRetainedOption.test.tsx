import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { useForm } from 'react-hook-form';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import { GridDesigner } from './GridDesigner.js';
import { buildCells, type GridDesignerValue } from './gridDesignerModel.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';

function camera(identifier: string, name: string, fab = 'munich'): CameraSummary {
  return {
    cameraIdentifier: identifier,
    version: 1,
    fab,
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
 * Tested at <c>GridDesigner</c> because that is where the option list is built,
 * with the retained map supplied directly.
 * </para>
 *
 * <para>
 * <b>That is only half of it, and the half that worked.</b> Handing the map in
 * proves the designer consults one and says nothing about the dialog filling it
 * — which is exactly where the shipped defect was.
 * <c>LayoutEditorDialogRetention.test.tsx</c> drives the dialog for that reason.
 * </para>
 */
describe('GridDesigner — a filtered-out camera stays on its own tile', () => {
  function Harness({
    cameras,
    knownCameras,
    selected,
    filtering = false,
  }: {
    cameras: ReadonlyArray<CameraSummary>;
    knownCameras?: ReadonlyMap<string, CameraSummary>;
    selected: string | ReadonlyArray<string>;
    filtering?: boolean;
  }) {
    const held = typeof selected === 'string' ? [selected] : selected;
    const cells = buildCells(1, held.length);
    held.forEach((identifier, index) => {
      cells[index]!.cameraIdentifier = identifier;
    });

    const form = useForm<GridDesignerValue>({
      defaultValues: { name: 'wall', grid: { rows: 1, cols: held.length }, cells },
    });

    return (
      <GridDesigner
        form={form}
        cameras={cameras}
        knownCameras={knownCameras}
        overlays={[]}
        camerasLoading={false}
        overlaysLoading={false}
        cameraFilterActive={filtering}
      />
    );
  }

  it('Keeps the assigned camera selectable when the filter excludes it', () => {
    const assigned = camera(CAMERA_A, 'Line 2 Furnace');
    const matched = camera(CAMERA_B, 'Bay 4 Inlet');

    render(<Harness cameras={[matched]} knownCameras={new Map([[CAMERA_A, assigned]])} selected={CAMERA_A} />);

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
    render(<Harness cameras={[camera(CAMERA_B, 'Bay 4 Inlet')]} knownCameras={new Map()} selected={CAMERA_A} />);

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

  /**
   * **A retained option is qualified by the same rule as any other.**
   *
   * <para>
   * Ambiguity was computed over the matches alone, and the retained camera is
   * appended after that — so an operator holding two fabs, each with a
   * <c>Line-1-Entrance</c>, one on each of two tiles, who then types a fragment
   * matching neither, saw two identical unqualified options. That is exactly the
   * confusion spec 048's fab suffix exists to remove, let back in through the
   * retention path added here.
   * </para>
   */
  it('Qualifies a retained camera by fab when another retained camera shares its name', () => {
    const munich = camera(CAMERA_A, 'Line-1-Entrance', 'munich');
    const berlin = camera(CAMERA_B, 'Line-1-Entrance', 'berlin');

    render(
      <Harness
        cameras={[camera('33333333-3333-3333-3333-333333333333', 'Bay 4 Inlet')]}
        knownCameras={
          new Map([
            [CAMERA_A, munich],
            [CAMERA_B, berlin],
          ])
        }
        selected={[CAMERA_A, CAMERA_B]}
        filtering
      />,
    );

    const [first, second] = screen.getAllByLabelText(/^Camera$/i);

    expect(within(first!).getByRole('option', { name: 'Line-1-Entrance (munich)' })).toBeInTheDocument();
    expect(within(second!).getByRole('option', { name: 'Line-1-Entrance (berlin)' })).toBeInTheDocument();
  });

  /**
   * A camera not on any tile must not cause qualification. The retained map
   * holds everything seen, so qualifying from it wholesale would put a fab
   * suffix on options for a reason no longer on screen.
   */
  it('Leaves a name unqualified when the twin is only in the retained map', () => {
    const munich = camera(CAMERA_A, 'Line-1-Entrance', 'munich');
    const berlin = camera(CAMERA_B, 'Line-1-Entrance', 'berlin');

    render(<Harness cameras={[munich]} knownCameras={new Map([[CAMERA_B, berlin]])} selected={CAMERA_A} filtering />);

    const select = screen.getByLabelText(/^Camera$/i);

    expect(within(select).getByRole('option', { name: 'Line-1-Entrance' })).toBeInTheDocument();
  });
});

/**
 * The placeholder is what a screen reader announces when the picker takes
 * focus, which makes it the one place an operator is told why the list is empty
 * (spec 055).
 */
describe('GridDesigner — an empty list under a filter is not an empty fab', () => {
  function EmptyHarness({ filtering }: { filtering: boolean }) {
    const form = useForm<GridDesignerValue>({
      defaultValues: { name: 'wall', grid: { rows: 1, cols: 1 }, cells: buildCells(1, 1) },
    });

    return (
      <GridDesigner
        form={form}
        cameras={[]}
        overlays={[]}
        camerasLoading={false}
        overlaysLoading={false}
        cameraFilterActive={filtering}
      />
    );
  }

  it('Says the search matched nothing while a fragment is in force', () => {
    render(<EmptyHarness filtering={true} />);

    const select = screen.getByLabelText(/^Camera$/i);

    expect(within(select).getByRole('option', { name: /no camera matches your search/i })).toBeInTheDocument();
    expect(within(select).queryByRole('option', { name: /no cameras in this fab/i })).not.toBeInTheDocument();
  });

  /**
   * And still says the fab is empty when it is, which is the fact the filtered
   * message must not displace — an operator here should register a camera.
   */
  it('Still says the fab is empty when nothing is filtered', () => {
    render(<EmptyHarness filtering={false} />);

    const select = screen.getByLabelText(/^Camera$/i);

    expect(within(select).getByRole('option', { name: /no cameras in this fab/i })).toBeInTheDocument();
  });
});
