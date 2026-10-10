import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthProvider } from 'react-oidc-context';
import { UserManager, User, WebStorageStateStore } from 'oidc-client-ts';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NavigationProvider } from './navigation/NavigationProvider.js';
import { ShellLayout, SurfaceCrash } from './ShellLayout.js';

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §4.3
 * / §6 test 4. `./navigation/NavigationProvider.ts(x)` does not exist yet,
 * and `ShellLayout` still renders its unconditional `DESTINATIONS` list
 * (T011, US2, replaces it with `useVisibleEntries()`). This file fails on
 * the missing module today; once T011 lands, the fallback failure mode is
 * the nav staying unconditional — the seven links/items appearing regardless
 * of the stubbed scope.
 *
 * <p>
 * <b>Auth stub, mirrors `staleBearerRetry.test.tsx`.</b> A real
 * `AuthProvider` + `UserManager`, never a mocked `react-oidc-context` — a
 * mocked `useAuth` never carries a real `User.scope`, which is exactly the
 * input FR-009 says navigation must read.
 * </p>
 *
 * <p>
 * <b>Judgment call (flagged):</b> plan.md does not name the hook that
 * combines `NavigationProvider`'s entries with the session's granted scopes.
 * This file assumes `NavigationProvider` wraps the auth-aware tree (as
 * plan.md §4.3 says it is "mounted in RoutedApp, i.e. after
 * authentication") and that `ShellLayout` itself reads both contexts, since
 * nothing in `router.tsx`'s shape names a separate composition point. If
 * T011 places the combining hook elsewhere, only this file's provider
 * nesting needs adjusting, not its assertions.
 * </p>
 */

const nowEpoch = () => Math.floor(Date.now() / 1000);

const userWithScope = (scope: string): User =>
  new User({
    access_token: 'ACCESS-TOKEN',
    token_type: 'Bearer',
    scope,
    profile: {
      sub: 'operator',
      iss: 'https://keycloak.test/realms/smart-sentinel-eye',
      aud: 'management-web',
      exp: nowEpoch() + 3600,
      iat: nowEpoch(),
    },
    expires_at: nowEpoch() + 3600,
  });

const ALL_21_SCOPES = [
  'sse.variables.write',
  'sse.identity.kiosks.read',
  'sse.identity.devices.read',
  'sse.audit.read',
  'sse.cameras.read',
  'sse.overlays.read',
  'sse.identity.devices.write',
  'sse.rules.read',
  'sse.overlays.write',
  'sse.events.read',
  'sse.streams.write',
  'sse.layouts.write',
  'sse.variables.read',
  'sse.events.write',
  'sse.identity.kiosks.write',
  'sse.rules.write',
  'sse.webhooks.write',
  'sse.cameras.write',
  'sse.streams.read',
  'sse.events.types.write',
  'sse.layouts.read',
].join(' ');

const WITHOUT_CAMERAS_READ = ALL_21_SCOPES.split(' ')
  .filter((scope) => scope !== 'sse.cameras.read')
  .join(' ');

const CAMERAS_BASE_URL = 'http://localhost:5176';

const VALID_CAMERAS_MANIFEST = {
  schemaVersion: 1,
  remote: 'cameras',
  basePath: '/cameras',
  entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
};

const DESTINATIONS: ReadonlyArray<{ path: string; label: string }> = [
  { path: 'layouts', label: 'Layouts' },
  { path: 'walls', label: 'Walls' },
  { path: 'overlays', label: 'Overlays' },
  { path: 'rules', label: 'Rules' },
  { path: 'system-variables', label: 'System variables' },
  { path: 'audit', label: 'Audit' },
];

function StubPage({ label }: { label: string }) {
  return <h1>{label}</h1>;
}

async function renderShellFor(manager: UserManager) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <ShellLayout />,
        children: [
          { index: true, element: <StubPage label="Layouts" /> },
          { path: 'cameras', element: <StubPage label="Cameras" />, errorElement: <SurfaceCrash /> },
          ...DESTINATIONS.map((destination) => ({
            path: destination.path,
            element: <StubPage label={destination.label} />,
          })),
        ],
      },
    ],
    { initialEntries: ['/'] },
  );

  const result = render(
    <AuthProvider userManager={manager}>
      <NavigationProvider remotes={[{ name: 'cameras', baseUrl: CAMERAS_BASE_URL }]}>
        <RouterProvider router={router} />
      </NavigationProvider>
    </AuthProvider>,
  );

  // Settle both the auth provider's initial getUser() resolution and the
  // navigation provider's manifest fetch (ADR-0150: never a fixed yield
  // count).
  await waitFor(() => expect(screen.queryByRole('navigation')).not.toBeNull());

  return result;
}

function managerWith(user: User): UserManager {
  const manager = new UserManager({
    authority: 'https://keycloak.test/realms/smart-sentinel-eye',
    client_id: 'management-web',
    redirect_uri: 'http://localhost/',
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    automaticSilentRenew: false,
  });
  void manager.storeUser(user);
  return manager;
}

describe('ShellLayout — navigation gated by the session’s granted scopes (spec 316 FR-009/FR-010/FR-011/FR-014)', () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(VALID_CAMERAS_MANIFEST), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.sessionStorage.clear();
  });

  it('A full-scope session sees all seven surfaces in today’s order, in the nav and in the palette', async () => {
    await renderShellFor(managerWith(userWithScope(ALL_21_SCOPES)));

    const expectedOrder = ['Cameras', 'Layouts', 'Walls', 'Overlays', 'Rules', 'System variables', 'Audit'];

    await waitFor(() => {
      for (const label of expectedOrder) {
        expect(screen.getByRole('link', { name: new RegExp(`^${label}$`, 'i') })).toBeInTheDocument();
      }
    });

    const navLinks = within(screen.getByRole('navigation')).getAllByRole('link');
    expect(navLinks.map((link) => link.textContent)).toEqual(expectedOrder);

    const user = userEvent.setup();
    await user.keyboard('{Control>}k{/Control}');
    const dialog = await screen.findByRole('dialog', { name: 'Go to' });
    for (const label of expectedOrder) {
      expect(within(dialog).getByText(label)).toBeInTheDocument();
    }
  });

  it('A session missing sse.cameras.read sees six surfaces, with no Cameras in the nav or the palette', async () => {
    await renderShellFor(managerWith(userWithScope(WITHOUT_CAMERAS_READ)));

    await waitFor(() => {
      expect(screen.getByRole('link', { name: /^layouts$/i })).toBeInTheDocument();
    });

    expect(screen.queryByRole('link', { name: /^cameras$/i })).not.toBeInTheDocument();

    const navLinks = within(screen.getByRole('navigation')).getAllByRole('link');
    expect(navLinks).toHaveLength(6);

    const user = userEvent.setup();
    await user.keyboard('{Control>}k{/Control}');
    const dialog = await screen.findByRole('dialog', { name: 'Go to' });
    expect(within(dialog).queryByText('Cameras')).not.toBeInTheDocument();
  });

  it('A silently renewed, narrower scope recomputes the visible set without remounting the nav (FR-014)', async () => {
    const user = userWithScope(ALL_21_SCOPES);
    const manager = managerWith(user);

    await renderShellFor(manager);
    await waitFor(() => expect(screen.getByRole('link', { name: /^cameras$/i })).toBeInTheDocument());

    const navBefore = screen.getByRole('navigation');

    const narrowedUser = userWithScope(WITHOUT_CAMERAS_READ);
    await manager.storeUser(narrowedUser);
    await manager.events.load(narrowedUser);

    await waitFor(() => expect(screen.queryByRole('link', { name: /^cameras$/i })).not.toBeInTheDocument());

    // The same <nav> DOM node, not a freshly mounted one: a remount would
    // have produced a new element, which `toBe` (reference equality) would
    // catch.
    expect(screen.getByRole('navigation')).toBe(navBefore);
  });
});
