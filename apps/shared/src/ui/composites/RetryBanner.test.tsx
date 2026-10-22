// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RetryBanner } from './RetryBanner.js';

/**
 * Spec 298 (issue #2693) US0 — characterisation (ADR-0139/0144 phase-4a,
 * behaviour-preserving half). `RetryBanner` has no test of its own today; it
 * is exercised only indirectly through page tests that click "Retry". This
 * captures its current public contract — role, text, classes, the Retry
 * callback — green on the untouched tree, before it is rebuilt to draw its
 * box through `FaultNotice`. Must pass unmodified after that change.
 */
describe('RetryBanner', () => {
  it('Renders the message and a Retry button inside one alert', () => {
    render(<RetryBanner message="Could not load layouts." onRetry={() => {}} />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not load layouts.');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('Calls onRetry once when Retry is clicked', () => {
    const onRetry = vi.fn();
    render(<RetryBanner message="Could not load layouts." onRetry={onRetry} />);

    screen.getByRole('button', { name: 'Retry' }).click();

    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('Paints the fault box on the fault tokens', () => {
    render(<RetryBanner message="Could not load layouts." onRetry={() => {}} />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveClass(
      'border-accent-fault-border',
      'bg-accent-fault-subtle',
      'text-accent-fault',
      'mb-4',
      'rounded-md',
      'px-3',
      'py-2',
      'text-sm',
    );
  });

  // Spec 427 (issue #2668) R9, phase 4a RED: Retry is the shared LinkButton, so it draws the
  // design-system focus ring (the raw underlined button draws the browser default) and is still
  // underlined.
  it('Draws Retry as an underlined link with the design-system focus ring', () => {
    render(<RetryBanner message="Could not load layouts." onRetry={() => {}} />);

    expect(screen.getByRole('button', { name: 'Retry' })).toHaveClass(
      'underline',
      'focus-visible:outline-2',
      'focus-visible:outline-offset-2',
      'focus-visible:outline-focus-ring',
    );
  });
});

/**
 * Spec 429 (#3073) R1-R7, phase 4a RED. `failed`/`retrying` are driven through
 * `rerender`, standing in for the query state a page passes. The four tests
 * above are the unmodified characterisation: the defaults (FR-009) keep
 * `<RetryBanner message onRetry />` behaving as before.
 */
describe('RetryBanner — recovery (spec 429)', () => {
  const MESSAGE = 'Could not load layouts.';
  const LIVE_REGION = 'retry-banner-live-region';

  function banner(props: { failed: boolean; retrying: boolean; onRetry?: () => void; onRecovered?: () => void }) {
    return (
      <RetryBanner
        message={MESSAGE}
        onRetry={props.onRetry ?? (() => {})}
        failed={props.failed}
        retrying={props.retrying}
        onRecovered={props.onRecovered}
      />
    );
  }

  it('Keeps an empty live region mounted, and no alert, while nothing has failed', () => {
    render(banner({ failed: false, retrying: false }));

    expect(screen.getByTestId(LIVE_REGION).textContent).toBe('');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('Announces Retrying and keeps the same, focused, aria-disabled Retry node while in flight', async () => {
    const user = userEvent.setup();
    const { rerender } = render(banner({ failed: true, retrying: false }));
    const retry = screen.getByRole('button', { name: 'Retry' });

    await user.click(retry);
    rerender(banner({ failed: true, retrying: true }));

    expect(screen.getByTestId(LIVE_REGION)).toHaveTextContent('Retrying…');
    const inFlight = screen.getByRole('button', { name: 'Retry' });
    expect(inFlight).toBe(retry);
    expect(inFlight).toHaveAttribute('aria-disabled', 'true');
    expect(inFlight).not.toHaveAttribute('disabled');
    expect(inFlight).toHaveFocus();
  });

  it('Does not call onRetry again when Retry is clicked while the read is in flight', async () => {
    const user = userEvent.setup();
    const onRetry = vi.fn();
    const { rerender } = render(banner({ failed: true, retrying: false, onRetry }));

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: true, onRetry }));
    await user.click(screen.getByRole('button', { name: 'Retry' }));

    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('Calls onRecovered once and announces Loaded when a retried read succeeds', async () => {
    const user = userEvent.setup();
    const onRecovered = vi.fn();
    const { rerender } = render(banner({ failed: true, retrying: false, onRecovered }));

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: true, onRecovered }));
    rerender(banner({ failed: false, retrying: false, onRecovered }));

    expect(onRecovered).toHaveBeenCalledOnce();
    expect(screen.getByTestId(LIVE_REGION)).toHaveTextContent('Loaded.');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('Remounts the alert and refocuses Retry, with an empty live region, when a retried read fails again', async () => {
    const user = userEvent.setup();
    const { rerender } = render(banner({ failed: true, retrying: false }));
    const alertBefore = screen.getByRole('alert');

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: true }));
    rerender(banner({ failed: true, retrying: false }));

    expect(screen.getByRole('alert')).not.toBe(alertBefore);
    expect(screen.getByRole('button', { name: 'Retry' })).toHaveFocus();
    expect(screen.getByTestId(LIVE_REGION).textContent).toBe('');
  });

  it('Stays silent and does not call onRecovered when a read the operator did not request succeeds', () => {
    const onRecovered = vi.fn();
    const { rerender } = render(banner({ failed: true, retrying: false, onRecovered }));

    rerender(banner({ failed: true, retrying: true, onRecovered }));
    rerender(banner({ failed: false, retrying: false, onRecovered }));

    expect(onRecovered).not.toHaveBeenCalled();
    expect(screen.getByTestId(LIVE_REGION).textContent).toBe('');
  });

  it('Keeps the same alert and moves no focus when a read the operator did not request fails', () => {
    const { rerender } = render(banner({ failed: true, retrying: false }));
    const alertBefore = screen.getByRole('alert');
    const focusBefore = document.activeElement;

    rerender(banner({ failed: true, retrying: true }));
    rerender(banner({ failed: true, retrying: false }));

    expect(screen.getByRole('alert')).toBe(alertBefore);
    expect(document.activeElement).toBe(focusBefore);
  });
});

/**
 * Spec 429 (#3073) phase-6 findings, RED. A fast failure can batch pending and
 * rejected into one render, so props never show the retry; the handler's
 * returned promise is then the only evidence of its outcome. And recovery must
 * never pull focus out of something the operator opened mid-flight.
 */
describe('RetryBanner — batched outcomes and focus (spec 429 review)', () => {
  const LIVE_REGION = 'retry-banner-live-region';

  function banner(props: { failed: boolean; retrying: boolean; onRetry?: () => unknown; onRecovered?: () => void }) {
    return (
      <>
        <button type="button">Elsewhere</button>
        <RetryBanner
          message="Could not load layouts."
          onRetry={props.onRetry ?? (() => {})}
          failed={props.failed}
          retrying={props.retrying}
          onRecovered={props.onRecovered}
        />
      </>
    );
  }

  it('Announces a repeat failure when the retried read fails inside one batched render', async () => {
    const user = userEvent.setup();
    let settle: (value: unknown) => void = () => {};
    const onRetry = () => new Promise<unknown>((resolve) => (settle = resolve));
    const { rerender } = render(banner({ failed: true, retrying: false, onRetry }));
    const alertBefore = screen.getByRole('alert');

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: false, onRetry }));
    await act(async () => {
      settle({ isError: true });
    });

    expect(screen.getByRole('alert')).not.toBe(alertBefore);
    expect(screen.getByRole('button', { name: 'Retry' })).toHaveFocus();
  });

  it('Does not let a batched failure leave the click armed for the next unrequested read', async () => {
    const user = userEvent.setup();
    let settle: (value: unknown) => void = () => {};
    const onRetry = () => new Promise<unknown>((resolve) => (settle = resolve));
    const onRecovered = vi.fn();
    const { rerender } = render(banner({ failed: true, retrying: false, onRetry, onRecovered }));

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: false, onRetry, onRecovered }));
    await act(async () => {
      settle({ isError: true });
    });
    screen.getByRole('button', { name: 'Elsewhere' }).focus();
    rerender(banner({ failed: true, retrying: true, onRetry, onRecovered }));
    rerender(banner({ failed: false, retrying: false, onRetry, onRecovered }));

    expect(onRecovered).not.toHaveBeenCalled();
    expect(screen.getByTestId(LIVE_REGION).textContent).toBe('');
    expect(screen.getByRole('button', { name: 'Elsewhere' })).toHaveFocus();
  });

  it('Does not call onRecovered when focus has moved to something the operator opened mid-flight', async () => {
    const user = userEvent.setup();
    const onRecovered = vi.fn();
    const { rerender } = render(banner({ failed: true, retrying: false, onRecovered }));

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: true, onRecovered }));
    screen.getByRole('button', { name: 'Elsewhere' }).focus();
    rerender(banner({ failed: false, retrying: false, onRecovered }));

    expect(onRecovered).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Elsewhere' })).toHaveFocus();
  });

  it('Does not pull focus back to Retry after a repeat failure when focus has moved elsewhere', async () => {
    const user = userEvent.setup();
    const { rerender } = render(banner({ failed: true, retrying: false }));

    await user.click(screen.getByRole('button', { name: 'Retry' }));
    rerender(banner({ failed: true, retrying: true }));
    screen.getByRole('button', { name: 'Elsewhere' }).focus();
    rerender(banner({ failed: true, retrying: false }));

    expect(screen.getByRole('button', { name: 'Elsewhere' })).toHaveFocus();
  });
});
