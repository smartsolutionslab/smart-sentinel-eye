import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShellLayout, SurfaceCrash } from '../ShellLayout.js';

/**
 * Spec 316 (issue superseding #1008) T005 — RED (ADR-0139/0144), plan.md §6
 * test 6, read in light of the two decisions appended to plan.md on
 * 2026-10-08 after T001's live findings:
 *
 * <p>
 * <b>FR-008 (mismatch containment).</b> T001 found that the realistic
 * remote-stricter version-mismatch case never rejects `loadRemote`'s
 * promise at all — only the runtime's own `errorLoadRemote` lifecycle hook
 * (`RemoteHandler.hooks.lifecycle`) fires. The first case below drives that
 * hook directly, with `loadRemote` deliberately left permanently pending,
 * so the only way the failure panel can appear is through the hook — a
 * `RemoteSurface` that only wrapped `loadRemote` in try/catch (as plan.md
 * §4.4 originally sketched, before T001) would leave this case red forever.
 * </p>
 *
 * <p>
 * <b>FR-007 (retry).</b> T001 found `registerRemotes(..., { force: true })`
 * necessary but not sufficient: a browser's own dynamic-`import()` cache
 * permanently fails a literal URL for the life of the page, so a retry that
 * does not vary the registered entry URL can never succeed. The second case
 * asserts the registered URL actually differs across attempts — the
 * stronger assertion the plan decision calls for, since "`loadRemote` was
 * called again" alone would stay green even if the cache-busting were
 * silently dropped.
 * </p>
 *
 * <p>
 * `RemoteSurface`, and the whole `apps/management-web/src/app/navigation/`
 * directory, do not exist yet — T009's job. This file is expected to fail
 * at the `./RemoteSurface.js` import (module not found), not at an
 * assertion. Do not add any implementation to make it pass early.
 * </p>
 */

const BASE_URL = 'http://localhost:5176';
vi.stubEnv('VITE_CAMERAS_REMOTE_URL', BASE_URL);

const registerRemotesMock = vi.fn();
const loadRemoteMock = vi.fn();

type ErrorLoadRemoteHandler = (payload: { id: string; error: unknown }) => void;
const errorLoadRemoteHandlers: ErrorLoadRemoteHandler[] = [];

/**
 * The mocked shape of `@module-federation/runtime` 2.9.2's default global
 * instance — `getInstance().hooks.lifecycle.errorLoadRemote.on(handler)` —
 * per plan.md's own citation of `RemoteHandler.hooks.lifecycle` (T001
 * finding item 3). The package itself is not installed on this branch yet
 * (T007 adds the pin), so this mock is not checked against the real
 * package's type declarations; T009 reconciles the shape with the real one
 * when it writes `RemoteSurface.tsx`, adjusting this mock if it is wrong
 * rather than the behaviour this file pins down.
 */
vi.mock('@module-federation/runtime', () => ({
  registerRemotes: (...args: unknown[]) => registerRemotesMock(...args),
  loadRemote: (...args: unknown[]) => loadRemoteMock(...args),
  getInstance: () => ({
    hooks: {
      lifecycle: {
        errorLoadRemote: {
          on: (handler: ErrorLoadRemoteHandler) => {
            errorLoadRemoteHandlers.push(handler);
          },
        },
      },
    },
  }),
}));

const { RemoteSurface } = await import('./RemoteSurface.js');

/**
 * Mirrors `CellPage.test.tsx`'s own helpers: filtering `[resilience]` lines
 * by `transition` keeps the assertion independent of every other line the
 * page emits, rather than counting raw `console.info` calls.
 */
function resilienceLines(calls: unknown[][], transition: string): Record<string, unknown>[] {
  const lines: Record<string, unknown>[] = [];
  for (const [prefix, payload] of calls) {
    if (prefix !== '[resilience]' || typeof payload !== 'object' || payload === null) continue;
    const line = payload as Record<string, unknown>;
    if (line.transition === transition) lines.push(line);
  }
  return lines;
}

function renderRemoteSurfaceInShell() {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <ShellLayout />,
        children: [
          {
            path: 'cameras',
            element: <RemoteSurface remote="cameras" />,
            errorElement: <SurfaceCrash />,
          },
        ],
      },
    ],
    { initialEntries: ['/cameras'] },
  );
  render(<RouterProvider router={router} />);
}

describe('RemoteSurface — load failure, retry, and bad module shape (spec 316, plan.md §6 test 6)', () => {
  let infoSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    registerRemotesMock.mockReset();
    loadRemoteMock.mockReset();
    errorLoadRemoteHandlers.length = 0;
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("A strict-version mismatch surfaces through the runtime's errorLoadRemote hook, not a loadRemote rejection", async () => {
    // Deliberately never resolves or rejects — exactly T001's live finding
    // for the remote-stricter mismatch case. If the failure panel below
    // appears, it can only be because RemoteSurface observed the hook, not
    // a caught promise rejection.
    loadRemoteMock.mockImplementation(() => new Promise<never>(() => {}));

    renderRemoteSurfaceInShell();

    await vi.waitFor(() => expect(errorLoadRemoteHandlers.length).toBeGreaterThan(0));

    act(() => {
      errorLoadRemoteHandlers[0]?.({ id: 'cameras', error: new Error('version mismatch') });
    });

    expect(await screen.findByText(/this surface could not be loaded/i)).toBeInTheDocument();
    // The nav survives — the layout route stays mounted, only the child
    // route's content changes (mirrors SurfaceCrash's own existing doc
    // comment on why it is a child-route errorElement, not a boundary
    // around the whole Outlet).
    expect(screen.getByRole('link', { name: /^cameras$/i })).toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'remote-load-failed');
    expect(lines).toHaveLength(1);
    expect(lines[0]?.remote).toBe('cameras');
  });

  it('Retry re-registers the remote under a varying entry URL, not the lazy cache', async () => {
    loadRemoteMock.mockRejectedValue(new Error('RUNTIME-008: Failed to fetch dynamically imported module'));

    renderRemoteSurfaceInShell();

    expect(await screen.findByText(/this surface could not be loaded/i)).toBeInTheDocument();
    await vi.waitFor(() => expect(registerRemotesMock).toHaveBeenCalledTimes(1));

    const firstCallRemotes = registerRemotesMock.mock.calls[0]?.[0] as Array<{ entry: string }>;
    const firstEntry = firstCallRemotes[0]?.entry;
    expect(firstEntry).toContain(BASE_URL);

    fireEvent.click(screen.getByRole('button', { name: /^try again$/i }));

    await vi.waitFor(() => expect(registerRemotesMock).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(loadRemoteMock).toHaveBeenCalledTimes(2));

    const secondCallRemotes = registerRemotesMock.mock.calls[1]?.[0] as Array<{ entry: string }>;
    const secondEntry = secondCallRemotes[0]?.entry;

    // The stronger assertion plan.md's 2026-10-08 decision calls for
    // (T001 finding item 4): "loadRemote called again" alone would stay
    // green even if the cache-busting query parameter were silently
    // dropped, because the browser's own dynamic-import() cache for the
    // literal entry URL would still shadow every retry.
    expect(secondEntry).not.toBe(firstEntry);
    expect(secondEntry).toMatch(/\?retry=\d+$/);
  });

  it('A loaded module whose default export is missing or not a function is a load failure, not a crash', async () => {
    loadRemoteMock.mockResolvedValue({ default: undefined });

    renderRemoteSurfaceInShell();

    expect(await screen.findByText(/this surface could not be loaded/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /^cameras$/i })).toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'remote-load-failed');
    expect(lines).toHaveLength(1);
  });
});
