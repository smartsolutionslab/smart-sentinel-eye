// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmDialog } from './ConfirmDialog.js';

/**
 * Spec 032 T004–T005 — the product's first destructive confirmation.
 *
 * Every assertion here is a **call count** or a **role**, not "it rendered".
 * A confirmation that renders beautifully and calls its action on dismiss is
 * worse than no confirmation at all, and a render assertion cannot tell them
 * apart.
 */
afterEach(cleanup);

function renderDialog(overrides: Partial<Parameters<typeof ConfirmDialog>[0]> = {}) {
  const onConfirm = vi.fn();
  const onOpenChange = vi.fn();

  render(
    <ConfirmDialog
      open
      onOpenChange={onOpenChange}
      title="Retire this camera?"
      confirmLabel="Retire camera"
      onConfirm={onConfirm}
      {...overrides}
    >
      <p>This cannot be undone.</p>
    </ConfirmDialog>,
  );

  return { onConfirm, onOpenChange };
}

describe('ConfirmDialog', () => {
  /**
   * T005. The whole reason this is a separate primitive from `Dialog`. If
   * someone later "simplifies" it back onto `@radix-ui/react-dialog`, this is
   * the assertion that fails — and the focus behaviour below is what would
   * silently be lost with it.
   */
  it('Is an alert dialog, not a dialog', () => {
    renderDialog();

    expect(screen.getByRole('alertdialog')).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  /**
   * The reason the role matters, made concrete. Radix puts initial focus on
   * the cancelling action, so the keyboard default for an irreversible action
   * is to back out of it.
   */
  it('Puts the keyboard on cancel, not on the destructive action', () => {
    renderDialog();

    expect(document.activeElement).toBe(screen.getByRole('button', { name: /cancel/i }));
  });

  it('Confirming calls the action exactly once', () => {
    const { onConfirm } = renderDialog();

    fireEvent.click(screen.getByRole('button', { name: /retire camera/i }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  /**
   * FR-008, asserted as a call count. A dialog that closes cleanly and fires
   * the action anyway passes any assertion about the dialog closing — which is
   * the assertion a hurried author writes.
   */
  it('Cancelling calls the action zero times', () => {
    const { onConfirm } = renderDialog();

    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));

    expect(onConfirm).not.toHaveBeenCalled();
  });

  /**
   * FR-015, and the one that fails on a real double-click rather than on a
   * contrived one. `pending` is the caller's in-flight state; while it is set
   * the action must refuse, or a slow network turns one retirement into two
   * requests.
   */
  it('Refuses a second confirmation while the first is in flight', () => {
    const { onConfirm } = renderDialog({ pending: true });

    const action = screen.getByRole('button', { name: /retire camera/i });
    fireEvent.click(action);
    fireEvent.click(action);

    expect(onConfirm).toHaveBeenCalledTimes(0);
  });

  /**
   * The counterpart to the above: `pending` must not be permanently disabling.
   * Without this, a primitive that ignored the prop entirely and disabled the
   * action always would pass the test above.
   */
  it('Allows exactly one confirmation when nothing is in flight', () => {
    const { onConfirm } = renderDialog({ pending: false });

    fireEvent.click(screen.getByRole('button', { name: /retire camera/i }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('Renders the caller words describing what confirming will do', () => {
    renderDialog();

    expect(screen.getByText(/this cannot be undone/i)).toBeTruthy();
  });
});
