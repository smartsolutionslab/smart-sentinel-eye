// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { FaultNotice } from './FaultNotice.js';

/**
 * The shared fault-notice box (`role="alert"`, the opaque fault tint).
 */
describe('FaultNotice', () => {
  it('Renders its children inside one alert', () => {
    render(<FaultNotice>Could not apply that change.</FaultNotice>);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not apply that change.');
  });

  it('Renders a control passed as a child inside the alert', () => {
    render(
      <FaultNotice>
        Stale.
        <button type="button">Reload</button>
      </FaultNotice>,
    );

    const alert = screen.getByRole('alert');
    expect(within(alert).getByRole('button', { name: 'Reload' })).toBeInTheDocument();
  });

  it('Paints the fault box on the fault tokens and nothing translucent', () => {
    render(<FaultNotice>Could not apply that change.</FaultNotice>);

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
    expect([...alert.classList].some((token) => /\/\d+$/.test(token))).toBe(false);
  });
});
