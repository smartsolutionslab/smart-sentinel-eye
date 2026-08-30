import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async (_body: unknown) => ({ data: 2 }));

// Set per test so the error banner can be exercised; the mutation hooks are
// module-level mocks and cannot take arguments.
let editError: unknown = undefined;
const refetchChainMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: editError, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [editDraftMock, { isLoading: false, error: editError, reset: vi.fn() }],
    // The dialog reads the chain back to learn its current version; the page
    // branched a draft just before opening, so the version it held is stale.
    useGetLayoutQuery: () => ({
      data: { layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', version: 7 },
      isLoading: false,
      refetch: refetchChainMock,
    }),
  };
});

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';

/**
 * Driven per test so the picker's states can be induced rather than waited for.
 * Spec 048's whole subject is what happens when the list is *not* everything,
 * and a fixture that is always complete cannot exercise it.
 */
interface CameraChoicesResult {
  data?: { items: unknown[]; count: number; complete: boolean };
  isLoading: boolean;
  isError: boolean;
}

const COMPLETE_CHOICES: CameraChoicesResult = {
  data: {
    items: [
      {
        cameraIdentifier: CAMERA_A,
        name: 'Line-1-Entrance',
        rtspUrl: 'rtsp://10.0.5.12/h264',
        registeredAt: '2026-05-25T10:00:00Z',
      },
      {
        cameraIdentifier: CAMERA_B,
        name: 'Line-2-Exit',
        rtspUrl: 'rtsp://10.0.5.13/h264',
        registeredAt: '2026-05-25T10:00:00Z',
      },
    ],
    count: 2,
    complete: true,
  },
  isLoading: false,
  isError: false,
};

let cameraChoices: CameraChoicesResult = COMPLETE_CHOICES;

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => cameraChoices,
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({
      data: {
        chains: [],
        published: [
          {
            overlayIdentifier: OVERLAY_X,
            name: 'Line-1 Title',
            revisionNumber: 1,
            text: 'Production Line 1',
            publishedAt: '2026-05-27T10:00:00Z',
          },
        ],
      },
      isLoading: false,
    }),
  };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function renderDialog(editTarget?: LayoutEditTarget) {
  return render(
    <Provider store={store}>
      <LayoutEditorDialog open={true} onOpenChange={() => {}} editTarget={editTarget} />
    </Provider>,
  );
}

describe('LayoutEditorDialog — create', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    editError = undefined;
  });

  it('Starts on a 1×1 grid with a name input and one camera picker', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getAllByRole('combobox', { name: /camera/i })).toHaveLength(1);
    expect(screen.getByRole('option', { name: 'Line-1-Entrance' })).toBeInTheDocument();
  });

  it('Submits a single-tile wall with the chosen overlay', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1');
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.selectOptions(screen.getByRole('combobox', { name: /overlay/i }), OVERLAY_X);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Line-1',
      grid: { rows: 1, cols: 1 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 }],
    });
  });

  it('Submits overlayIdentifier=null when "(none)" is selected', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-2');
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Line-2',
      grid: { rows: 1, cols: 1 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 }],
    });
  });

  it('Resizing to 1×2 and filling both cells submits two tiles', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Wall');
    await user.click(screen.getByRole('radio', { name: '1×2' }));

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    expect(cameraSelects).toHaveLength(2);
    await user.selectOptions(cameraSelects[0]!, CAMERA_A);
    await user.selectOptions(cameraSelects[1]!, CAMERA_B);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Wall',
      grid: { rows: 1, cols: 2 },
      tiles: [
        { cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 },
        { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
      ],
    });
  });

  it('Drops an empty cell so a sparse 1×2 submits one tile', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Sparse');
    await user.click(screen.getByRole('radio', { name: '1×2' }));

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    await user.selectOptions(cameraSelects[0]!, CAMERA_A);
    // Leave cell 2 empty.
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Sparse',
      grid: { rows: 1, cols: 2 },
      tiles: [{ cameraIdentifier: CAMERA_A, overlayIdentifier: null, row: 0, col: 0 }],
    });
  });

  it('Rejects a wall with no populated cell (at least one tile required)', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Empty');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(await screen.findByText(/at least one tile/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('Surfaces a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});

describe('LayoutEditorDialog — edit', () => {
  const editTarget: LayoutEditTarget = {
    layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    revisionNumber: 2,
    name: 'Rolling Mill',
    grid: { rows: 1, cols: 2 },
    tiles: [
      { cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 },
      { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
    ],
  };

  beforeEach(() => {
    createDraftMock.mockClear();
    editDraftMock.mockClear();
    refetchChainMock.mockClear();
    editError = undefined;
  });

  it('Pre-loads the branched draft grid + tiles and PATCHes on save', async () => {
    const user = userEvent.setup();
    renderDialog(editTarget);

    // No name input in edit mode (the chain name is kept).
    expect(screen.queryByLabelText(/name/i)).not.toBeInTheDocument();
    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    expect(cameraSelects).toHaveLength(2);
    expect(cameraSelects[0]!).toHaveValue(CAMERA_A);
    expect(cameraSelects[1]!).toHaveValue(CAMERA_B);

    await user.click(screen.getByRole('button', { name: /save draft/i }));

    expect(editDraftMock).toHaveBeenCalledWith({
      layoutIdentifier: editTarget.layoutIdentifier,
      revisionNumber: 2,
      version: 7,
      grid: { rows: 1, cols: 2 },
      tiles: [
        { cameraIdentifier: CAMERA_A, overlayIdentifier: OVERLAY_X, row: 0, col: 0 },
        { cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 1 },
      ],
    });
    expect(createDraftMock).not.toHaveBeenCalled();
  });

  it('Lets the operator swap a tile camera before saving', async () => {
    const user = userEvent.setup();
    renderDialog(editTarget);

    const cameraSelects = screen.getAllByRole('combobox', { name: /camera/i });
    await user.selectOptions(cameraSelects[1]!, CAMERA_A);
    await user.click(screen.getByRole('button', { name: /save draft/i }));

    const body = editDraftMock.mock.calls[0]![0] as { tiles: Array<{ cameraIdentifier: string }> };
    expect(body.tiles[1]!.cameraIdentifier).toBe(CAMERA_A);
  });
});

/**
 * Spec 012 T050. The dialog previously rendered "Could not save the layout.
 * Try again." for every failure — including a 409, where retrying resubmits
 * the same stale intent over whoever wrote in between. That is the overwrite
 * ADR-0113 exists to prevent, recommended by the UI itself.
 */
describe('Conflict copy (spec 012 T050)', () => {
  const editTarget: LayoutEditTarget = {
    layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    revisionNumber: 2,
    name: 'Cnc-Hall',
    grid: { rows: 1, cols: 1 },
    tiles: [{ cameraIdentifier: CAMERA_B, overlayIdentifier: null, row: 0, col: 0 }],
  };

  beforeEach(() => {
    refetchChainMock.mockClear();
    editError = undefined;
  });

  function staleConflict() {
    return {
      status: 409,
      data: {
        title: 'LAYOUT_REVISION_STALE',
        detail: 'Layout has changed since version 7 (now 8). Re-read it and reapply the change.',
      },
    };
  }

  it('Tells the operator to reload rather than retry when the revision moved', async () => {
    editError = staleConflict();
    renderDialog(editTarget);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Re-read');
    expect(alert.textContent).not.toContain('Try again');
  });

  it('Refetches the chain when the operator reloads, so the resubmitted version is current', async () => {
    editError = staleConflict();
    const user = userEvent.setup();
    renderDialog(editTarget);

    await user.click(screen.getByRole('button', { name: /reload/i }));

    expect(refetchChainMock).toHaveBeenCalledTimes(1);
  });

  // A name collision is the other 409 this dialog can produce, and there
  // retrying *is* the right advice — with a different name. Keying the copy on
  // the status alone would have handed the operator a reload prompt instead.
  it('Keeps retry wording, and offers no reload, for a name collision', async () => {
    editError = {
      status: 409,
      data: { title: 'LAYOUT_NAME_TAKEN', detail: "A layout named 'Cnc-Hall' already exists." },
    };
    renderDialog();

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('already exists');
    expect(screen.queryByRole('button', { name: /reload/i })).toBeNull();
  });
});

/**
 * Spec 048 US1 — the picker stops being silent.
 *
 * <p>
 * <b>Every case here induces an incomplete list before asserting anything.</b>
 * A fixture whose list is always complete passes with the notice deleted, and
 * one whose list is always truncated passes with the notice hard-coded. Both
 * directions are asserted for that reason.
 * </p>
 */
describe('The camera picker says what it is not showing (spec 048 US1)', () => {
  afterEach(() => {
    cameraChoices = COMPLETE_CHOICES;
  });

  function truncated(shown: number, total: number): CameraChoicesResult {
    return {
      data: {
        items: Array.from({ length: shown }, (_unused, index) => ({
          cameraIdentifier: `cam-${String(index).padStart(4, '0')}`,
          name: `Camera ${String(index).padStart(4, '0')}`,
          rtspUrl: 'rtsp://10.0.5.1/h264',
          registeredAt: '2026-05-25T10:00:00Z',
        })),
        count: total,
        complete: false,
      },
      isLoading: false,
      isError: false,
    };
  }

  /**
   * **The core claim.** Both numbers, because "some cameras may not be shown"
   * tells an operator nothing they can act on.
   */
  it('States how many cameras are shown and how many exist', async () => {
    cameraChoices = truncated(1_000, 1_200);
    renderDialog();

    const notice = await screen.findByText(/Showing 1000 of 1200 cameras/i);
    expect(notice).toBeInTheDocument();
  });

  /**
   * **The assertion that gives the notice meaning.** One that is always present
   * carries no information, and operators learn to ignore it.
   */
  it('Says nothing at all when every camera is offered', async () => {
    cameraChoices = COMPLETE_CHOICES;
    renderDialog();

    await screen.findByLabelText(/^Camera$/i);
    expect(screen.queryByText(/Showing \d+ of \d+ cameras/i)).not.toBeInTheDocument();
  });

  /**
   * Painted is not announced. Without the association a screen-reader user
   * tabbing into the select hears the label and nothing about the list being
   * incomplete — which is the population most harmed by a silently short list.
   */
  it('Announces the notice on the camera control, not merely beside it', async () => {
    cameraChoices = truncated(1_000, 1_200);
    renderDialog();

    const select = await screen.findByLabelText(/^Camera$/i);
    const describedBy = select.getAttribute('aria-describedby');
    expect(describedBy, 'the select must point at the notice').not.toBeNull();
    expect(document.getElementById(describedBy as string)).toHaveTextContent(/Showing 1000 of 1200/i);
  });

  it('Leaves the camera control undescribed when there is nothing to say', async () => {
    cameraChoices = COMPLETE_CHOICES;
    renderDialog();

    const select = await screen.findByLabelText(/^Camera$/i);
    expect(select.getAttribute('aria-describedby')).toBeNull();
  });
});

/**
 * Spec 048 FR-003 — three states that used to render identically.
 *
 * <p>
 * An operator who cannot tell "this fab has no cameras" from "the request
 * failed" goes looking for the wrong problem. It is the same class of defect as
 * the silent truncation: a state rendered as a different, more innocent one.
 * </p>
 */
describe('An empty camera picker says why it is empty (spec 048 FR-003)', () => {
  afterEach(() => {
    cameraChoices = COMPLETE_CHOICES;
  });

  it('Distinguishes a fab with no cameras from a list that could not be retrieved', async () => {
    cameraChoices = { data: { items: [], count: 0, complete: true }, isLoading: false, isError: false };
    renderDialog();
    expect(await screen.findByText(/No cameras in this fab/i)).toBeInTheDocument();

    cleanup();
    cameraChoices = { data: undefined, isLoading: false, isError: true };
    renderDialog();
    expect(await screen.findByText(/Camera list unavailable/i)).toBeInTheDocument();
  });

  it('Says it is still loading rather than claiming the fab is empty', async () => {
    cameraChoices = { data: undefined, isLoading: true, isError: false };
    renderDialog();

    expect(await screen.findByText(/Loading cameras/i)).toBeInTheDocument();
    expect(screen.queryByText(/No cameras in this fab/i)).not.toBeInTheDocument();
  });
});

/**
 * Spec 048 US2 — every camera is reachable.
 *
 * <p>
 * Complements the paging tests rather than repeating them. Those prove which
 * camera survives a page boundary; these prove the dialog offers what it was
 * handed and does not lose a selection when the list grows underneath.
 * </p>
 */
describe('Every camera in the fab can be put on a tile (spec 048 US2)', () => {
  afterEach(() => {
    cameraChoices = COMPLETE_CHOICES;
  });

  function fullFab(total: number): CameraChoicesResult {
    return {
      data: {
        items: Array.from({ length: total }, (_unused, index) => ({
          cameraIdentifier: `cam-${String(index).padStart(4, '0')}`,
          name: `Camera ${String(index).padStart(4, '0')}`,
          rtspUrl: 'rtsp://10.0.5.1/h264',
          registeredAt: '2026-05-25T10:00:00Z',
        })),
        count: total,
        complete: true,
      },
      isLoading: false,
      isError: false,
    };
  }

  /**
   * 250 is the production target and the number the picker failed at. The
   * alphabetically last camera is the one a single fifty-row request could
   * never reach, so asserting only the option count would pass against the
   * defect.
   */
  it('Offers all 250 cameras of a full fab, including the last', async () => {
    cameraChoices = fullFab(250);
    renderDialog();

    const select = await screen.findByLabelText(/^Camera$/i);
    const options = within(select).getAllByRole('option');
    // 250 cameras plus the '(empty cell)' placeholder.
    expect(options).toHaveLength(251);
    expect(within(select).getByRole('option', { name: 'Camera 0249' })).toBeInTheDocument();
  });

  /**
   * FR-011. The list arriving or growing must not cost an operator a choice
   * they already made — which is why the option list keeps the shape it had
   * rather than being restructured around paging.
   */
  it('Keeps a selection already made when the list grows underneath it', async () => {
    cameraChoices = fullFab(250);
    const view = renderDialog();

    const select = await screen.findByLabelText(/^Camera$/i);
    await userEvent.selectOptions(select, 'cam-0100');
    expect(select).toHaveValue('cam-0100');

    // The list grows under the open dialog, as a concurrent registration does.
    cameraChoices = fullFab(400);
    view.rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} />
      </Provider>,
    );

    const grown = await screen.findByLabelText(/^Camera$/i);
    expect(grown, 'the choice survives the list being extended').toHaveValue('cam-0100');
  });
});
