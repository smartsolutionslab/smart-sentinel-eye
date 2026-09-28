import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ShellLayout, SurfaceCrash } from './ShellLayout.js';

/**
 * Spec 287 (issue #2623) T002 — characterisation, captured GREEN on develop
 * before any source change (plan.md §5a): nothing today covers the crash
 * panel's own retry, only `App.test.tsx`'s router-level crash-containment
 * test (which never clicks "Try again"). A refactor with no covering test is
 * a rewrite, so this is written first and observed passing against today's
 * raw `<button>` before spec 287 moves it to the shared `Button` primitive.
 *
 * Mirrors `ShellLayout.test.tsx`'s own `createMemoryRouter` pattern rather
 * than mounting the whole `App`: the property under test is `SurfaceCrash`'s
 * retry wiring (re-navigate to the same path resets the router's error
 * boundary), not sign-in or route composition.
 */
afterEach(cleanup);

/**
 * Throws while `counter.loaderCalls <= 1`, i.e. on the route's initial entry.
 *
 * <p>
 * A flag flipped inside the throwing component's own render body does not
 * work here: React 19's `createRoot` (what Testing Library's `render` uses)
 * retries a failed render synchronously once before handing the error to the
 * router's `errorElement`, calling the component function a second time in
 * the same pass. A flag cleared on first throw already reads "recovered" by
 * that second call, so the panel never shows at all — the crash is silently
 * absorbed by React's own retry, not by this component's "try again". The
 * counter instead advances in the route's `loader`, which the data router
 * runs once per navigation (not once per React render attempt), so both the
 * interrupted and the synchronous-retry render see the same count.
 * </p>
 */
function CrashOnFirstEntry({ counter }: { counter: { loaderCalls: number } }) {
  if (counter.loaderCalls <= 1) {
    throw new Error('crash panel probe');
  }
  return <h1>Recovered surface</h1>;
}

function renderShellWithCrashingChild(counter: { loaderCalls: number }) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <ShellLayout />,
        children: [
          {
            path: 'crash-probe',
            loader: () => {
              counter.loaderCalls += 1;
              return null;
            },
            element: <CrashOnFirstEntry counter={counter} />,
            errorElement: <SurfaceCrash />,
          },
        ],
      },
    ],
    { initialEntries: ['/crash-probe'] },
  );
  render(<RouterProvider router={router} />);
}

describe('ShellLayout crash panel', () => {
  it('Shows "Try again", and activating it re-navigates and re-renders the recovered child', async () => {
    // React reports the caught render error via console.error; keep output clean
    // (mirrors App.test.tsx's own crash-containment test).
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const counter = { loaderCalls: 0 };
    try {
      renderShellWithCrashingChild(counter);

      expect(await screen.findByRole('heading', { name: /something went wrong/i })).toBeInTheDocument();
      expect(screen.getByText(/crash panel probe/i)).toBeInTheDocument();

      const retry = screen.getByRole('button', { name: /^try again$/i });
      fireEvent.click(retry);

      expect(await screen.findByText('Recovered surface')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: /something went wrong/i })).not.toBeInTheDocument();
    } finally {
      consoleError.mockRestore();
    }
  });
});
