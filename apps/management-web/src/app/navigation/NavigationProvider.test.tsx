import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NavigationProvider, useNavigation } from './NavigationProvider.js';

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §4.3
 * / §6 test 3. `./NavigationProvider.ts(x)` does not exist yet (T011, US2).
 * Every case here fails on the missing module today, not on an assertion
 * mismatch against a working provider.
 *
 * <p>
 * <b>Judgment call (flagged, not silently picked):</b> neither spec.md nor
 * plan.md fixes `NavigationProvider`'s exact props or exported context
 * shape beyond "exposes composition state" and the context value
 * `{ status, entries, remoteFor(path) }` (plan.md §4.3). This file assumes:
 * (a) a `remotes` prop lets a caller override the registry `remotes.ts`
 * would otherwise read from `import.meta.env` at module scope — the same
 * reasoning `RemoteLoadFailure.ts`'s own doc comment gives for why
 * `RemoteSurface.test.tsx` cannot let a real static import chain read that
 * env var before `vi.stubEnv` runs; (b) a `manifestTimeoutMs` prop overrides
 * the fixed `MANIFEST_TIMEOUT_MS` (plan.md §4.3's 5 s bound) so the timeout
 * case below does not have to wait 5 real seconds; (c) a `useNavigation()`
 * hook exposes the context value plan.md §4.3 describes, for a consuming
 * test component to read. T011 may implement this differently; if it does,
 * this file's imports — not its assertions about *behaviour* — are what
 * change.
 * </p>
 */

const CAMERAS_BASE_URL = 'http://localhost:5176';
const LAYOUTS_BASE_URL = 'http://localhost:5177';

function manifestResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

const VALID_CAMERAS_MANIFEST = {
  schemaVersion: 1,
  remote: 'cameras',
  basePath: '/cameras',
  entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
};

/** Mirrors `RemoteSurface.test.tsx`'s own `resilienceLines` helper. */
function resilienceLines(calls: unknown[][], transition: string): Record<string, unknown>[] {
  const lines: Record<string, unknown>[] = [];
  for (const [prefix, payload] of calls) {
    if (prefix !== '[resilience]' || typeof payload !== 'object' || payload === null) continue;
    const line = payload as Record<string, unknown>;
    if (line.transition === transition) lines.push(line);
  }
  return lines;
}

/** Mirrors plan.md §2.1's NavEntry shape, for the Probe component below. */
interface ProbeNavEntry {
  readonly path: string;
  readonly label: string;
}

function Probe() {
  const { status, entries } = useNavigation();
  return (
    <ul data-testid="probe" data-status={status}>
      {(entries as readonly ProbeNavEntry[]).map((entry) => (
        <li key={entry.path}>{entry.label}</li>
      ))}
    </ul>
  );
}

describe('NavigationProvider — fail-closed manifest handling (spec 316 FR-015, plan.md §4.3/§6 test 3)', () => {
  let infoSpy: ReturnType<typeof vi.spyOn>;
  const fetchMock = vi.fn();

  beforeEach(() => {
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => undefined);
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('A manifest fetch that 404s contributes no entry and logs manifest-rejected exactly once', async () => {
    fetchMock.mockResolvedValue(manifestResponse({ error: 'not found' }, 404));

    render(
      <NavigationProvider remotes={[{ name: 'cameras', baseUrl: CAMERAS_BASE_URL }]}>
        <Probe />
      </NavigationProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveAttribute('data-status', 'ready'));

    expect(screen.queryByText('Cameras')).not.toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'manifest-rejected');
    expect(lines).toHaveLength(1);
    expect(lines[0]?.remote).toBe('cameras');
    expect(lines[0]?.reason).toBeTypeOf('string');
  });

  it('A manifest fetch that times out contributes no entry and logs manifest-rejected exactly once', async () => {
    // Never resolves — the exact shape plan.md §4.3 guards against with
    // AbortSignal.timeout(MANIFEST_TIMEOUT_MS): a down remote must not hold
    // the whole shell's navigation hostage.
    fetchMock.mockImplementation(() => new Promise<never>(() => {}));

    render(
      <NavigationProvider remotes={[{ name: 'cameras', baseUrl: CAMERAS_BASE_URL }]} manifestTimeoutMs={20}>
        <Probe />
      </NavigationProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveAttribute('data-status', 'ready'), {
      timeout: 5_000,
    });

    expect(screen.queryByText('Cameras')).not.toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'manifest-rejected');
    expect(lines).toHaveLength(1);
    expect(lines[0]?.remote).toBe('cameras');
  });

  it('A manifest that fails schema validation contributes no entry and logs manifest-rejected exactly once', async () => {
    fetchMock.mockResolvedValue(manifestResponse({ schemaVersion: 2, remote: 'cameras', basePath: '/cameras' }));

    render(
      <NavigationProvider remotes={[{ name: 'cameras', baseUrl: CAMERAS_BASE_URL }]}>
        <Probe />
      </NavigationProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveAttribute('data-status', 'ready'));

    expect(screen.queryByText('Cameras')).not.toBeInTheDocument();

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'manifest-rejected');
    expect(lines).toHaveLength(1);
    expect(lines[0]?.remote).toBe('cameras');
    expect(lines[0]?.reason).toBeTypeOf('string');
  });

  it('A remote manifest whose basePath collides with another accepted remote is rejected (FR-015)', async () => {
    fetchMock.mockImplementation((input: unknown) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.startsWith(CAMERAS_BASE_URL)) return Promise.resolve(manifestResponse(VALID_CAMERAS_MANIFEST));
      if (url.startsWith(LAYOUTS_BASE_URL)) {
        // Deliberately collides on "/cameras", the first remote's accepted
        // basePath, rather than on an in-shell path — this remote-vs-remote
        // half of FR-015's collision rule, not the remote-vs-in-shell half.
        return Promise.resolve(
          manifestResponse({
            schemaVersion: 1,
            remote: 'duplicate-cameras',
            basePath: '/cameras',
            entries: [{ path: '/cameras', label: 'Also cameras', order: 15, requiredScopes: ['sse.layouts.read'] }],
          }),
        );
      }
      throw new Error(`unexpected fetch: ${url}`);
    });

    render(
      <NavigationProvider
        remotes={[
          { name: 'cameras', baseUrl: CAMERAS_BASE_URL },
          { name: 'duplicate-cameras', baseUrl: LAYOUTS_BASE_URL },
        ]}
      >
        <Probe />
      </NavigationProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('probe')).toHaveAttribute('data-status', 'ready'));

    // Exactly one of the two colliding remotes may win the basePath — this
    // test does not pin which — but never both: a collision cannot leave two
    // entries claiming the same path.
    expect(screen.getAllByText(/cameras/i)).toHaveLength(1);

    const lines = resilienceLines(infoSpy.mock.calls as unknown[][], 'manifest-rejected');
    expect(lines).toHaveLength(1);
    expect(lines[0]?.reason).toMatch(/basePath|collis/i);
  });
});
