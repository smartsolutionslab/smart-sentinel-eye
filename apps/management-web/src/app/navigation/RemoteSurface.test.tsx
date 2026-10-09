import type { ComponentType } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShellLayout, SurfaceCrash } from '../ShellLayout.js';
import { default as mismatchRuntimePlugin } from './mismatchRuntimePlugin.js';

/**
 * Spec 316 (issue superseding #1008) T005 — RED (ADR-0139/0144), plan.md §6
 * test 6, read in light of the two decisions appended to plan.md on
 * 2026-10-08 after T001's live findings, and B3/B4 (phase-6 review) after
 * that.
 *
 * <p>
 * <b>FR-008 (mismatch containment).</b> T001 found that the realistic
 * remote-stricter version-mismatch case never rejects `loadRemote`'s
 * promise at all. The first case below drives the real `resolveShare`
 * runtime-plugin hook (`mismatchRuntimePlugin.ts`) directly — the signal
 * confirmed, live, against the installed `@module-federation/runtime-core
 * @2.9.2`, to actually fire for this case — with `loadRemote` deliberately
 * left permanently pending, so the only way the failure panel can appear is
 * through that hook. An earlier version of this test drove an invented
 * `errorLoadRemote` shape instead; B3's review found that hook's payload
 * never carries the bare remote name this component filtered on, so it
 * could never have fired for this case even though the test was green.
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
 * <b>B4 (no re-fetch on a plain remount).</b> The fourth case asserts a
 * remount after a prior success — the index route and `cameras/*` are
 * separate route objects, so every navigation between them remounts this
 * component — calls `registerRemotes` exactly once, not once per mount.
 * Each `it` below re-imports `./RemoteSurface.js` under a fresh,
 * test-unique query string: its module-scope B4 caches
 * (`resolvedComponents`/`failedRemotes`) must survive a remount *within* one
 * running app (that's the point of them), so they must NOT leak *between*
 * these otherwise-independent test cases. `mismatchRuntimePlugin.js` is
 * imported normally (no cache-busting): its listener set is emptied by
 * each test's own effect cleanup (`cleanup()` in `afterEach`), so nothing
 * leaks there either.
 * </p>
 */

const BASE_URL = 'http://localhost:5176';
vi.stubEnv('VITE_CAMERAS_REMOTE_URL', BASE_URL);

const registerRemotesMock = vi.fn();
const loadRemoteMock = vi.fn();

/**
 * Mocks only `registerRemotes`/`loadRemote`; `satisfy` (and everything
 * else) passes through to the real, installed `@module-federation/runtime`
 * package — B3's counterfactual below exercises the real version-range
 * check, not a reinvented one.
 */
vi.mock('@module-federation/runtime', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@module-federation/runtime')>();
  return {
    ...actual,
    registerRemotes: (...args: unknown[]) => registerRemotesMock(...args),
    loadRemote: (...args: unknown[]) => loadRemoteMock(...args),
  };
});

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

function renderRemoteSurfaceInShell(RemoteSurfaceComponent: ComponentType<{ remote: string }>) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <ShellLayout />,
        children: [
          {
            path: 'cameras',
            element: <RemoteSurfaceComponent remote="cameras" />,
            errorElement: <SurfaceCrash />,
          },
        ],
      },
    ],
    { initialEntries: ['/cameras'] },
  );
  return render(<RouterProvider router={router} />);
}

describe('RemoteSurface — load failure, retry, bad module shape, and no-refetch-on-remount (spec 316, plan.md §6 test 6)', () => {
  let infoSpy: ReturnType<typeof vi.spyOn>;
  let RemoteSurface: ComponentType<{ remote: string }>;
  let testModuleInstance = 0;

  beforeEach(async () => {
    registerRemotesMock.mockReset();
    loadRemoteMock.mockReset();
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => undefined);

    // A fresh `./RemoteSurface.js` instance per test, via a test-unique
    // query string: its B4 caches are module scope by design (surviving a
    // remount is the fix), so without this they would leak across these
    // independent test cases instead of just within one running app.
    testModuleInstance += 1;
    const module = (await import(
      /* @vite-ignore */ `./RemoteSurface.js?test-instance=${testModuleInstance}`
    )) as typeof import('./RemoteSurface.js');
    RemoteSurface = module.RemoteSurface;
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('A shared-dependency version mismatch surfaces through the real resolveShare runtime hook, not a loadRemote rejection', async () => {
    // Deliberately never resolves or rejects — exactly T001's live finding
    // for the remote-stricter mismatch case. If the failure panel below
    // appears, it can only be because RemoteSurface observed the plugin's
    // mismatch report, not a caught promise rejection.
    loadRemoteMock.mockImplementation(() => new Promise<never>(() => {}));

    renderRemoteSurfaceInShell(RemoteSurface);

    await vi.waitFor(() => expect(registerRemotesMock).toHaveBeenCalledTimes(1));

    // The exact function @module-federation/vite's generated runtime-init
    // code would call as the `runtimePlugins` factory (vite.config.ts), and
    // the exact hook the real `SharedHandler` would invoke on it — driven
    // directly, with the real `satisfy` underneath (19.2.0 does not satisfy
    // a pinned-exact '19.3.0': confirmed live against the installed
    // @module-federation/runtime-core@2.9.2 before writing this test).
    const plugin = mismatchRuntimePlugin();
    act(() => {
      plugin.resolveShare?.({
        shareScopeMap: {},
        scope: 'default',
        pkgName: 'react',
        version: '19.2.0',
        shareInfo: { shareConfig: { requiredVersion: '19.3.0' } },
        GlobalFederation: {},
        resolver: () => undefined,
      } as Parameters<NonNullable<ReturnType<typeof mismatchRuntimePlugin>['resolveShare']>>[0]);
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
    expect(lines[0]?.message).toMatch(/react/);
  });

  it('Retry re-registers the remote under a varying entry URL, not the lazy cache', async () => {
    loadRemoteMock.mockRejectedValue(new Error('RUNTIME-008: Failed to fetch dynamically imported module'));

    renderRemoteSurfaceInShell(RemoteSurface);

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

    // B4: this second registration is a genuine retry (the first attempt
    // failed), so it is the one case still allowed to force.
    const secondCallOptions = registerRemotesMock.mock.calls[1]?.[1] as { force?: boolean } | undefined;
    expect(secondCallOptions?.force).toBe(true);
  });

  it('A loaded module whose default export is missing or not a function is a load failure, not a crash', async () => {
    loadRemoteMock.mockResolvedValue({ default: undefined });

    renderRemoteSurfaceInShell(RemoteSurface);

    expect(await screen.findByText(/this surface could not be loaded/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /^cameras$/i })).toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'remote-load-failed');
    expect(lines).toHaveLength(1);
  });

  it('B4: a remount after a prior success re-registers exactly once, not once per mount', async () => {
    function CamerasSurfaceStub() {
      return <p>cameras surface</p>;
    }
    loadRemoteMock.mockResolvedValue({ default: CamerasSurfaceStub });

    const first = renderRemoteSurfaceInShell(RemoteSurface);
    expect(await screen.findByText('cameras surface')).toBeInTheDocument();
    await vi.waitFor(() => expect(registerRemotesMock).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(loadRemoteMock).toHaveBeenCalledTimes(1));

    first.unmount();

    renderRemoteSurfaceInShell(RemoteSurface);
    expect(await screen.findByText('cameras surface')).toBeInTheDocument();

    // The remount resolved from the module-scope cache: no second
    // registration, no second fetch — plan §5.3's "incurs no fetch" for a
    // repeat visit, not just the first one.
    expect(registerRemotesMock).toHaveBeenCalledTimes(1);
    expect(loadRemoteMock).toHaveBeenCalledTimes(1);
  });
});
