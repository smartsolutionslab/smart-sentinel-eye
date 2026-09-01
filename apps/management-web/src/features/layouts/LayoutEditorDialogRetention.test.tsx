import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';
const CAMERA_B = '22222222-2222-2222-2222-222222222222';
const OVERLAY_X = '55555555-5555-5555-5555-555555555555';

const ALL_ITEMS = [
  {
    cameraIdentifier: CAMERA_A,
    name: 'Line 2 Furnace',
    rtspUrl: 'rtsp://10.0.5.12/h264',
    registeredAt: '2026-05-25T10:00:00Z',
  },
  {
    cameraIdentifier: CAMERA_B,
    name: 'Bay 4 Inlet',
    rtspUrl: 'rtsp://10.0.5.13/h264',
    registeredAt: '2026-05-25T10:00:00Z',
  },
];

// Only the second camera. Held as a module constant so the array identity is
// stable per filter, exactly as an RTK Query cache entry is.
const INLET_ONLY = [ALL_ITEMS[1]!];

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false, reset: vi.fn() }],
    useGetLayoutQuery: () => ({ data: undefined, isLoading: false, refetch: vi.fn() }),
  };
});

/**
 * Keyed on the fragment.
 *
 * <para>
 * The two populated results are <b>reference-stable</b> per key, like a real
 * cache entry — the close/reopen test below turns on that, and a fresh array per
 * render would hide the defect it exists to catch.
 * </para>
 *
 * <para>
 * The empty result is deliberately <b>a fresh array each render</b>, and should
 * stay that way. It is what proves the dialog's accumulation converges on
 * contents rather than on array identity: against the reference-based version
 * this crashed the component with "too many re-renders".
 * </para>
 */
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (arg: { name?: string } | void) => {
      const name = (arg ?? {}).name;
      const items = name === undefined ? ALL_ITEMS : name.toLowerCase().includes('inlet') ? INLET_ONLY : [];
      return {
        data: { items, count: items.length, complete: true },
        isLoading: false,
        isFetching: false,
        isError: false,
      };
    },
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

/**
 * The retention has to survive the dialog closing (spec 055).
 *
 * <para>
 * <b>The existing retention tests hand <c>GridDesigner</c> a map that already
 * holds the camera</b>, so they prove the designer uses one and prove nothing
 * about the dialog filling it. This drives the dialog itself, through the close
 * and reopen that is the ordinary way an operator reaches the editor twice.
 * </para>
 *
 * <para>
 * It fails when the retained map is not reaching the designer at all, and it
 * failed originally against the combination that shipped: a map cleared on close
 * plus a merge keyed on the response array's identity, so a reopen re-read the
 * same cache entry, nothing refilled the map, and it was empty at exactly the
 * moment a fragment excluded the camera a tile held.
 * </para>
 */
describe('LayoutEditorDialog — the retained camera survives a close and reopen', () => {
  it('Keeps an assigned camera on its tile after the dialog is reopened and then filtered', async () => {
    const user = userEvent.setup();

    const { rerender } = render(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} />
      </Provider>,
    );

    // Assign the camera the filter will later exclude.
    const picker = screen.getByRole('combobox', { name: /camera/i });
    await user.selectOptions(picker, CAMERA_A);
    expect(picker).toHaveValue(CAMERA_A);

    // Close and reopen — no fragment typed, which is the case that breaks.
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={false} onOpenChange={() => {}} />
      </Provider>,
    );
    rerender(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} />
      </Provider>,
    );

    const reopened = screen.getByRole('combobox', { name: /camera/i });
    await user.selectOptions(reopened, CAMERA_A);

    // Now filter to something that excludes it.
    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('inlet');

    // **Wait for the filter to actually be in force before asserting.** The
    // fragment is debounced, so for a moment after typing the picker still holds
    // the unfiltered list — and an assertion made then passes no matter what the
    // retention does. This test did exactly that until unwiring the retained map
    // failed to break it.
    await vi.waitFor(() => expect(screen.getByText(/1 of 1 cameras match/i)).toBeInTheDocument());

    // The tile must still hold — and still show — the camera it was given.
    const filtered = screen.getByRole('combobox', { name: /camera/i });
    expect(filtered).toHaveValue(CAMERA_A);
    expect(filtered).toHaveDisplayValue(/Line 2 Furnace/);
  });

  /**
   * **A search that matched nothing must not disable Save.**
   *
   * <para>
   * The button read the current matches, which meant "nothing matched" and
   * "there are no cameras" disabled it alike — on a form whose tiles may all
   * already be filled. The operator gets no reason and has to clear the box to
   * find out there was never a problem.
   * </para>
   */
  it('Leaves Save enabled when a fragment matches nothing', async () => {
    const user = userEvent.setup();

    render(
      <Provider store={store}>
        <LayoutEditorDialog open={true} onOpenChange={() => {}} />
      </Provider>,
    );

    await user.type(screen.getByLabelText(/^Name$/i), 'wall');
    await user.selectOptions(screen.getByRole('combobox', { name: /camera/i }), CAMERA_A);

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('nothingmatchesthis');

    // The status paragraph specifically: the picker's placeholder now says
    // something similar, which is the point of the other fix.
    await vi.waitFor(() => expect(screen.getByText(/no camera matches “nothingmatchesthis”/i)).toBeInTheDocument());

    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });
});
