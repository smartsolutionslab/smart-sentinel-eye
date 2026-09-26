import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import { ShellLayout } from './ShellLayout.js';

/**
 * Spec 266 (issue #2335) US6 — new behaviour, RED. `ShellLayout` gains a
 * shared `CommandPalette` (⌘K / Ctrl+K, or the visible "Go to…" button),
 * built on `@radix-ui/react-dialog` with a hand-written filtered listbox
 * (plan.md §3.5, §4.4). Every case here fails on content against today's
 * shell, which has no "Go to…" button and ignores Control+K/Meta+K
 * entirely — `findByRole('dialog', { name: 'Go to' })` times out.
 *
 * Renders under a real `createMemoryRouter` (mirroring
 * `CameraDetailPageNavigation.test.tsx`) with the seven current paths as stub
 * child routes, each a heading, so navigation and `aria-current` are
 * observed through the real router rather than asserted on a mock.
 */
afterEach(cleanup);

const DESTINATIONS: ReadonlyArray<{ path: string; label: string }> = [
  { path: 'cameras', label: 'Cameras' },
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

function renderShell(initialEntry = '/cameras') {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <ShellLayout />,
        children: [
          { index: true, element: <StubPage label="Cameras" /> },
          ...DESTINATIONS.map((destination) => ({
            path: destination.path,
            element: <StubPage label={destination.label} />,
          })),
        ],
      },
    ],
    { initialEntries: [initialEntry] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

describe('ShellLayout', () => {
  /**
   * Declared green in advance (spec 266 §6): true of today's shell. Its
   * green result is not phase-4a evidence — it pins that extracting the
   * DESTINATIONS constant must not change the nav's markup or link set.
   */
  it('Renders the nav with its current seven links', () => {
    renderShell();

    for (const destination of DESTINATIONS) {
      expect(screen.getByRole('link', { name: new RegExp(`^${destination.label}$`, 'i') })).toBeInTheDocument();
    }
  });

  it('Control+K opens a dialog named "Go to" listing the seven destinations', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.keyboard('{Control>}k{/Control}');

    const dialog = await screen.findByRole('dialog', { name: 'Go to' });
    for (const destination of DESTINATIONS) {
      expect(within(dialog).getByText(destination.label)).toBeInTheDocument();
    }
  });

  it('Meta+K also opens the palette', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.keyboard('{Meta>}k{/Meta}');

    expect(await screen.findByRole('dialog', { name: 'Go to' })).toBeInTheDocument();
  });

  it("Prevents the keydown's default action, so the browser's own Control+K is not triggered", () => {
    renderShell();

    const event = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true });
    document.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });

  it('Typing filters to the match, and Enter navigates and focuses the nav link', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.keyboard('{Control>}k{/Control}');
    const dialog = await screen.findByRole('dialog', { name: 'Go to' });
    await user.type(within(dialog).getByRole('combobox'), 'rul');
    await user.keyboard('{Enter}');

    expect(await screen.findByRole('heading', { name: 'Rules' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    const rulesLink = screen.getByRole('link', { name: /^rules$/i });
    expect(rulesLink).toHaveFocus();
    expect(rulesLink).toHaveAttribute('aria-current', 'page');
  });

  it('The "Go to…" button opens the palette, and Escape returns focus to it', async () => {
    const user = userEvent.setup();
    renderShell();

    const trigger = screen.getByRole('button', { name: /go to…/i });
    await user.click(trigger);
    await screen.findByRole('dialog', { name: 'Go to' });

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  /**
   * Spec US6 "conflict" scenario: a stray ⌘K must not navigate an operator
   * out of an unsaved form. Modelled the way plan.md §4.4 checks for it — any
   * open `[role="dialog"][data-state="open"]` (or `alertdialog`) already in
   * the document — rather than by mounting a real RuleDialog, which would
   * pull in the rules feature's own mocking just to prove this one check.
   */
  it('Control+K opens no palette while another dialog is already open', async () => {
    const user = userEvent.setup();
    renderShell();

    render(
      <div role="dialog" aria-modal="true" data-state="open">
        <input aria-label="Rule name" />
      </div>,
    );

    await user.keyboard('{Control>}k{/Control}');

    expect(screen.queryByRole('dialog', { name: 'Go to' })).not.toBeInTheDocument();
  });
});
