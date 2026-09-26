import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { store } from '../../app/store.js';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';

/**
 * Spec 258 US1, T014 (tasks.md). `WallDetailPage.tsx` and
 * `apps/shared/src/api/walls.api.ts` do not exist yet — this file is RED on
 * import failure, the acceptable "red for missing types" form (T016).
 *
 * Covers plan.md §6.2: the detail page's Next/Show switch mutation shows a
 * "wall changed, refreshed" toast on 409 WALL_STALE and re-fetches, without
 * auto-retrying — mirroring `LayoutsPage.test.tsx`'s stale-conflict fallback
 * convention (`refusal(status, title, detail?)`, `role="alert"`, and the
 * shared `CONFLICT_FALLBACK` string).
 */

const getWallMock = vi.fn();
const switchMock = vi.fn(async () => ({ data: {} }));
const refetchMock = vi.fn();

let switchState: { isLoading: boolean; error?: unknown } = { isLoading: false };

/** An RTK Query error in the shape the gateway's RFC-7807 body arrives in. */
function refusal(status: number, title: string, detail?: string) {
  return { status, data: { title, status, ...(detail === undefined ? {} : { detail }) } };
}

vi.mock('@smart-sentinel-eye/shared/api/walls.api', () => ({
  useGetWallQuery: (...args: unknown[]) => getWallMock(...args),
  useSwitchWallSceneMutation: () => [switchMock, switchState],
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useParams: () => ({ wallIdentifier: 'wall-1' }),
  };
});

const { WallDetailPage } = await import('./WallDetailPage.js');

function wall(overrides: Record<string, unknown> = {}) {
  return {
    wallIdentifier: 'wall-1',
    version: 0,
    fab: 'munich',
    name: 'Line 3 rotation',
    scenes: ['a', 'b'],
    showing: 'a',
    sceneVersion: 0,
    ...overrides,
  };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <WallDetailPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe('WallDetailPage — the stale-conflict toast (spec 258 US1, WALL_STALE)', () => {
  beforeEach(() => {
    switchState = { isLoading: false };
    switchMock.mockClear();
    getWallMock.mockReset();
    getWallMock.mockReturnValue({ data: wall(), isLoading: false, error: undefined, refetch: refetchMock });
    refetchMock.mockClear();
  });

  it('Shows the shared conflict fallback and re-fetches on a 409 WALL_STALE refusal', () => {
    switchState = { isLoading: false, error: refusal(409, 'WALL_STALE') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(CONFLICT_FALLBACK);
    expect(refetchMock).toHaveBeenCalled();
  });

  it('Does not automatically retry the switch after a WALL_STALE refusal', () => {
    switchState = { isLoading: false, error: refusal(409, 'WALL_STALE') };
    renderPage();

    // Only the mutation call(s) the test itself triggers should ever reach
    // switchMock; the page must not fire a second attempt on its own.
    expect(switchMock).not.toHaveBeenCalled();
  });

  it('Keeps the generic fallback for a non-stale 409', () => {
    switchState = { isLoading: false, error: refusal(409, 'WALL_SCENE_NOT_PUBLISHED') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).not.toHaveTextContent(CONFLICT_FALLBACK);
  });
});
