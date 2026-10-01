// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
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
});
