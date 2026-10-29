import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import { GridDesigner } from './GridDesigner.js';
import { buildCells, type GridDesignerValue } from './gridDesignerModel.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';

const CAMERAS: CameraSummary[] = [
  {
    cameraIdentifier: CAMERA_A,
    version: 1,
    fab: 'munich',
    name: 'Line-1-Entrance',
    rtspUrl: 'rtsp://10.0.5.1/h264',
    registeredAt: '2026-05-24T10:00:00Z',
    status: 'Registered',
  },
];

function Harness({ onOpenTileView }: { onOpenTileView?: (cellIndex: number) => void }) {
  const form = useForm<GridDesignerValue>({
    defaultValues: {
      name: 'wall',
      grid: { rows: 1, cols: 2 },
      cells: buildCells(1, 2, [
        { cameraIdentifier: CAMERA_A, overlayIdentifier: '', row: 0, col: 0, rowSpan: 1, colSpan: 1 },
      ]),
    },
  });
  return (
    <GridDesigner
      form={form}
      cameras={CAMERAS}
      overlays={[]}
      camerasLoading={false}
      overlaysLoading={false}
      onOpenTileView={onOpenTileView}
    />
  );
}

afterEach(cleanup);

describe('GridDesigner — opening a tile view', () => {
  it('offers "Open tile view" on a populated tile only', () => {
    render(<Harness onOpenTileView={vi.fn()} />);
    // Two tiles, one with a camera.
    expect(screen.getAllByRole('combobox', { name: /camera/i })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: /open tile view/i })).toHaveLength(1);
  });

  it('names the tile it opens and reports its index', async () => {
    const onOpenTileView = vi.fn();
    const user = userEvent.setup();
    render(<Harness onOpenTileView={onOpenTileView} />);

    await user.click(screen.getByRole('button', { name: /open tile view.*1,1/i }));
    expect(onOpenTileView).toHaveBeenCalledWith(0);
  });

  it('offers nothing when the host cannot show a tile view', () => {
    render(<Harness />);
    expect(screen.queryByRole('button', { name: /open tile view/i })).not.toBeInTheDocument();
  });
});
