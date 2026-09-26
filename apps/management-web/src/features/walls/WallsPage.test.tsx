import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';

/**
 * Spec 258 US1, T014 (tasks.md). `features/walls/` (and `walls.api.ts`'s
 * management-web-facing hooks) do not exist yet — this file is RED on import
 * failure alone (T016's "compile/module-resolution failure for missing
 * types" is the acceptable red here), mirroring
 * `LayoutsPage.test.tsx`/`LayoutEditorDialogSaveGate.test.tsx`'s conventions.
 *
 * Naming assumptions, stated explicitly because `features/walls/` doesn't
 * exist yet for this test to check itself against:
 * - a `WallsPage` default-ish named export in `./WallsPage.js`, listing
 *   walls with a "New wall" action, mirroring `LayoutsPage`'s shape;
 * - `wallsApi` exports `useListWallsQuery`, `useCreateWallMutation`,
 *   `useSwitchWallSceneMutation`, `useEditWallScenesMutation`, mirroring
 *   `layoutsApi`'s naming;
 * - the create/edit form is reachable from `WallsPage` via a "New wall"
 *   button opening a dialog with a `name` field and a scene multi-select
 *   whose options are Published layouts, mirroring `LayoutEditorDialog`'s
 *   "open from the page" shape.
 * If the implementing engineer's actual component tree differs, only the
 * render/interaction plumbing below needs adjusting — the assertions
 * (validation boundaries, stale-toast behaviour) are the pinned behaviour.
 */

const listWallsMock = vi.fn();
const createWallMock = vi.fn(async () => ({ data: 'w-1' }));
const switchWallSceneMock = vi.fn(async () => ({ data: {} }));

let createWallState: { isLoading: boolean; error?: unknown } = { isLoading: false };
let switchWallSceneState: { isLoading: boolean; error?: unknown } = { isLoading: false };

/** An RTK Query error in the shape the gateway's RFC-7807 body arrives in (mirrors LayoutsPage.test.tsx). */
function refusal(status: number, title: string, detail?: string) {
  return { status, data: { title, status, ...(detail === undefined ? {} : { detail }) } };
}

const PUBLISHED_LAYOUTS = [
  { layoutIdentifier: '11111111-1111-1111-1111-111111111111', name: 'Layout A' },
  { layoutIdentifier: '22222222-2222-2222-2222-222222222222', name: 'Layout B' },
  { layoutIdentifier: '33333333-3333-3333-3333-333333333333', name: 'Layout C' },
];

vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useListWallsQuery: (...args: unknown[]) => listWallsMock(...args),
    useCreateWallMutation: () => [createWallMock, createWallState],
    useSwitchWallSceneMutation: () => [switchWallSceneMock, switchWallSceneState],
    useEditWallScenesMutation: () => [vi.fn(async () => ({ data: {} })), { isLoading: false }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => ({
      data: { chains: [], published: PUBLISHED_LAYOUTS },
      isLoading: false,
    }),
  };
});

const { WallsPage } = await import('./WallsPage.js');

function renderPage() {
  return render(
    <Provider store={store}>
      <WallsPage />
    </Provider>,
  );
}

describe('WallsPage — create-form scene-count validation (PD-5)', () => {
  beforeEach(() => {
    listWallsMock.mockReset();
    listWallsMock.mockReturnValue({ data: [], isLoading: false });
    createWallMock.mockClear();
    createWallState = { isLoading: false };
  });

  async function openNewWallDialog() {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /new wall/i }));
    return user;
  }

  it('Accepts a name and two distinct scenes', async () => {
    const user = await openNewWallDialog();
    const dialog = screen.getByRole('dialog');

    await user.type(within(dialog).getByLabelText(/name/i), 'Line 3 rotation');
    await user.click(within(dialog).getByRole('checkbox', { name: /layout a/i }));
    await user.click(within(dialog).getByRole('checkbox', { name: /layout b/i }));
    await user.click(within(dialog).getByRole('button', { name: /save/i }));

    expect(createWallMock).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Line 3 rotation',
        scenes: [PUBLISHED_LAYOUTS[0]!.layoutIdentifier, PUBLISHED_LAYOUTS[1]!.layoutIdentifier],
      }),
    );
  });

  it('Rejects a single selected scene (MinScenes = 2)', async () => {
    const user = await openNewWallDialog();
    const dialog = screen.getByRole('dialog');

    await user.type(within(dialog).getByLabelText(/name/i), 'Line 3 rotation');
    await user.click(within(dialog).getByRole('checkbox', { name: /layout a/i }));
    await user.click(within(dialog).getByRole('button', { name: /save/i }));

    expect(createWallMock).not.toHaveBeenCalled();
    expect(within(dialog).getByText(/at least 2/i)).toBeInTheDocument();
  });

  it('Rejects more than eight selected scenes (MaxScenes = 8)', async () => {
    const nineLayouts = Array.from({ length: 9 }, (_, index) => ({
      layoutIdentifier: `${index}1111111-1111-1111-1111-111111111111`,
      name: `Layout ${index}`,
    }));
    listWallsMock.mockReturnValue({ data: [], isLoading: false });
    const user = userEvent.setup();
    render(
      <Provider store={store}>
        <WallsPage />
      </Provider>,
    );
    await user.click(screen.getByRole('button', { name: /new wall/i }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText(/name/i), 'Nine scenes');
    for (const layout of nineLayouts.slice(0, 9)) {
      const checkbox = within(dialog).queryByRole('checkbox', { name: new RegExp(layout.name, 'i') });
      if (checkbox !== null) {
        await user.click(checkbox);
      }
    }
    await user.click(within(dialog).getByRole('button', { name: /save/i }));

    expect(createWallMock).not.toHaveBeenCalled();
  });
});

describe('WallsPage — the stale-conflict toast on a 409 WALL_STALE switch (US1-7)', () => {
  beforeEach(() => {
    listWallsMock.mockReset();
    listWallsMock.mockReturnValue({
      data: [
        {
          wall: 'w-1',
          version: 3,
          fab: 'munich',
          name: 'Line 3 rotation',
          scenes: [PUBLISHED_LAYOUTS[0]!.layoutIdentifier, PUBLISHED_LAYOUTS[1]!.layoutIdentifier],
          showing: PUBLISHED_LAYOUTS[0]!.layoutIdentifier,
          sceneVersion: 5,
          showingSince: '2026-09-26T10:00:00Z',
        },
      ],
      isLoading: false,
    });
    switchWallSceneMock.mockClear();
    switchWallSceneState = { isLoading: false };
  });

  it("Shows a 'wall changed, refreshed' toast and re-fetches on WALL_STALE, without auto-retrying", async () => {
    switchWallSceneState = { isLoading: false, error: refusal(409, 'WALL_STALE', 'Wall has changed since version 3 (now 4).') };
    renderPage();

    expect(await screen.findByText(/wall changed/i)).toBeInTheDocument();
    expect(listWallsMock).toHaveBeenCalled();
    expect(switchWallSceneMock).not.toHaveBeenCalled();
  });

  it('Shows the generic conflict fallback for a non-stale 409 without a detail', async () => {
    switchWallSceneState = { isLoading: false, error: refusal(409, 'WALL_NAME_TAKEN') };
    renderPage();

    expect(await screen.findByText(CONFLICT_FALLBACK)).toBeInTheDocument();
  });
});
