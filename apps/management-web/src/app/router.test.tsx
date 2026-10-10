import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from './store.js';
import { createAppRouter } from './router.js';

/**
 * Every child route is wrapped in `<Gated>`, which reads
 * `useVisibleEntries()` — a `NavigationProvider`/`useAuth` context this
 * file's own render never supplies. The property under test here is route
 * registration, not navigation gating, so the hook is mocked to make
 * `/overlays` visible.
 */
vi.mock('./navigation/useVisibleEntries.js', () => ({
  useVisibleEntries: () => [{ path: '/overlays', label: 'Overlays', order: 10, requiredScopes: ['sse.overlays.read'] }],
}));

/**
 * Spec 305 (#2350) FR-001/FR-008, new behaviour, RED (ADR-0139/0144). Today's
 * `createAppRouter` registers no child for `overlays/new` or
 * `overlays/:overlayIdentifier/revisions/:revisionNumber/edit` — navigating
 * there matches no route in the tree at all, so neither `ShellLayout`'s nav
 * nor any page content renders. `window.history.pushState` + a freshly
 * created `createBrowserRouter` is the same pattern `App.affordance.test.tsx`
 * uses to exercise the app's real browser router under jsdom.
 */
function renderAt(path: string) {
  window.history.pushState({}, '', path);
  const router = createAppRouter();
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return router;
}

describe('createAppRouter — the overlay editor routes (spec 305, #2350)', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/');
  });

  afterEach(() => {
    window.history.pushState({}, '', '/');
  });

  it('Registers /overlays/new under ShellLayout, with Overlays marked current', () => {
    renderAt('/overlays/new');

    // Today neither this path nor any child of it exists in the route tree,
    // so the whole tree fails to match and the shell — including its nav —
    // never mounts at all (react-router's own default "no match" error page
    // renders instead). The assertion is the DESIRED behaviour (FR-001/
    // FR-008), so it fails now for that reason, not because the query times
    // out — a synchronous `queryByRole` rather than `findByRole`, so the red
    // run does not pay `findBy`'s default wait.
    const overlaysLink = screen.queryByRole('link', { name: /^overlays$/i });
    expect(overlaysLink).not.toBeNull();
    expect(overlaysLink).toHaveAttribute('aria-current', 'page');
  });

  it('Registers overlays/:overlayIdentifier/revisions/:revisionNumber/edit under ShellLayout, with Overlays marked current', () => {
    renderAt('/overlays/11111111-1111-1111-1111-111111111111/revisions/2/edit');

    const overlaysLink = screen.queryByRole('link', { name: /^overlays$/i });
    expect(overlaysLink).not.toBeNull();
    expect(overlaysLink).toHaveAttribute('aria-current', 'page');
  });
});
