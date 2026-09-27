import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
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

vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useGetWallQuery: (...args: unknown[]) => getWallMock(...args),
    useSwitchWallSceneMutation: () => [switchMock, switchState],
  };
});

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

/**
 * Spec 273 (#2632) / ADR-0151 — W1, new behaviour, RED. `WallDetailPage`
 * still passes native `disabled={switchState.isLoading}` to Next (`:126`).
 */
describe('WallDetailPage — Next keeps focus while a scene switch is in flight (spec 273 W1)', () => {
  beforeEach(() => {
    switchMock.mockClear();
    getWallMock.mockReset();
    getWallMock.mockReturnValue({ data: wall(), isLoading: false, error: undefined, refetch: refetchMock });
  });

  it('Announces Next as unavailable with aria-disabled, not native disabled, while a switch is in flight', () => {
    switchState = { isLoading: true };
    renderPage();

    const next = screen.getByRole('button', { name: /^next$/i });
    expect(next).toHaveAttribute('aria-disabled', 'true');
    expect(next).not.toHaveAttribute('disabled');
  });

  it('Sends no second switch from Next while one is already in flight', () => {
    switchState = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));

    expect(switchMock).not.toHaveBeenCalled();
  });
});

/**
 * Spec 273 (#2632) / ADR-0151 — W2, new behaviour, RED. `WallDetailPage`
 * still passes native `disabled={switchState.isLoading || scene ===
 * wall.showing}` to each scene's Show (`:157`) — direct while a switch is in
 * flight, and terminal (spec.md §3) for the scene already showing.
 */
describe('WallDetailPage — Show keeps focus while unavailable (spec 273 W2)', () => {
  beforeEach(() => {
    switchMock.mockClear();
    getWallMock.mockReset();
    getWallMock.mockReturnValue({ data: wall(), isLoading: false, error: undefined, refetch: refetchMock });
  });

  it('Announces Show as unavailable with aria-disabled, not native disabled, while a switch is in flight', () => {
    switchState = { isLoading: true };
    renderPage();

    // Scene 'b' is not the one showing (wall().showing === 'a'); its own Show
    // is unavailable here purely because a switch is in flight (direct).
    const show = screen.getAllByRole('button', { name: /^show$/i })[1]!;
    expect(show).toHaveAttribute('aria-disabled', 'true');
    expect(show).not.toHaveAttribute('disabled');
  });

  it('Sends no second switch from Show while one is already in flight', () => {
    switchState = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getAllByRole('button', { name: /^show$/i })[1]!);

    expect(switchMock).not.toHaveBeenCalled();
  });

  it('Announces the currently-showing scene as unavailable with aria-disabled (terminal)', () => {
    switchState = { isLoading: false };
    renderPage();

    // Scene 'a' is `wall().showing`, so its own Show is unavailable — the
    // terminal mechanism, independent of `switchState.isLoading`.
    const show = screen.getAllByRole('button', { name: /^show$/i })[0]!;
    expect(show).toHaveAttribute('aria-disabled', 'true');
    expect(show).not.toHaveAttribute('disabled');
  });

  it('Sends no switch for the already-showing scene', () => {
    switchState = { isLoading: false };
    renderPage();

    fireEvent.click(screen.getAllByRole('button', { name: /^show$/i })[0]!);

    expect(switchMock).not.toHaveBeenCalled();
  });
});
