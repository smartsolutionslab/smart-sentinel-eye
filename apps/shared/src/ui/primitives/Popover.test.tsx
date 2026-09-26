// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { Popover } from './Popover.js';

/**
 * Spec 266 (issue #2335) US3 — phase 4a is BEHAVIOUR-CHANGING (ADR-0139/0144):
 * every case below must be observed RED before `Popover.tsx` is a real Radix
 * wrapper.
 *
 * Committed against the signature-only stub (renders `null`, no Radix
 * import — plan.md §6), so every case fails on content ("unable to find role
 * dialog"), never on a missing module.
 */
afterEach(cleanup);

function renderPopover() {
  render(
    <Popover trigger={<button type="button">Offline</button>} label="Stream Offline">
      <p>Error: connection refused</p>
    </Popover>,
  );
  return screen.getByRole('button', { name: /offline/i });
}

describe('Popover', () => {
  it('Enter on the trigger opens a dialog named by label containing the children', async () => {
    const user = userEvent.setup();
    const trigger = renderPopover();

    trigger.focus();
    await user.keyboard('{Enter}');

    const dialog = await screen.findByRole('dialog', { name: 'Stream Offline' });
    expect(dialog).toHaveTextContent('Error: connection refused');
  });

  it('Escape closes the popover and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    const trigger = renderPopover();

    trigger.focus();
    await user.keyboard('{Enter}');
    await screen.findByRole('dialog');

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('An outside pointer-down closes the popover', async () => {
    const user = userEvent.setup();
    render(
      <>
        <Popover trigger={<button type="button">Offline</button>} label="Stream Offline">
          <p>Error: connection refused</p>
        </Popover>
        <button type="button">Elsewhere</button>
      </>,
    );

    await user.click(screen.getByRole('button', { name: /offline/i }));
    await screen.findByRole('dialog');

    await user.click(screen.getByRole('button', { name: /elsewhere/i }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
