// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Badge, type BadgeTone } from './Badge.js';

/**
 * Spec 297 (issue #2635) T006, plan.md §5.3. New behaviour, RED
 * (ADR-0139/0144): `Badge.tsx` is a signature-only stub — no tone/size
 * classes, no `Slot` for `asChild` — so every case below fails against the
 * stub, not against a missing file.
 */
describe('Badge', () => {
  const TONE_CLASSES: Record<BadgeTone, [string, string]> = {
    active: ['bg-accent-active-subtle', 'text-accent-active'],
    warning: ['bg-accent-warning-subtle', 'text-accent-warning'],
    fault: ['bg-accent-fault-subtle', 'text-accent-fault'],
    neutral: ['bg-bg-raised', 'text-fg-muted'],
  };

  it.each(Object.entries(TONE_CLASSES) as ReadonlyArray<[BadgeTone, [string, string]]>)(
    'Renders the %s tone with its two classes',
    (tone, [fillClass, textClass]) => {
      render(<Badge tone={tone}>Label</Badge>);

      const badge = screen.getByText('Label');
      expect(badge.className).toContain(fillClass);
      expect(badge.className).toContain(textClass);
    },
  );

  it('Defaults to size sm (px-2 py-0.5 text-xs)', () => {
    render(<Badge tone="neutral">Label</Badge>);

    const badge = screen.getByText('Label');
    expect(badge.className).toContain('px-2');
    expect(badge.className).toContain('py-0.5');
    expect(badge.className).toContain('text-xs');
  });

  it('size="md" gives px-3 py-1 text-xs', () => {
    render(
      <Badge tone="neutral" size="md">
        Label
      </Badge>,
    );

    const badge = screen.getByText('Label');
    expect(badge.className).toContain('px-3');
    expect(badge.className).toContain('py-1');
    expect(badge.className).toContain('text-xs');
  });

  it('Carries no call-site alpha modifier, border, shadow, blur, transition, animation, opacity or ring class (FR-004)', () => {
    render(<Badge tone="fault">Label</Badge>);

    const badge = screen.getByText('Label');
    const banned = /\/\d+$|^border|shadow|backdrop|transition|animate|opacity|ring/;
    const offending = badge.className.split(/\s+/).filter((token) => banned.test(token));
    expect(offending).toEqual([]);
  });

  it('asChild over a div merges Badge classes onto the child, keeping its role and test id (FR-002)', () => {
    render(
      <Badge tone="warning" asChild>
        <div role="status" data-testid="x">
          Live updates degraded
        </div>
      </Badge>,
    );

    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('data-testid', 'x');
    expect(status.tagName).toBe('DIV');
    expect(status.className).toContain('bg-accent-warning-subtle');
    // No Badge element of its own is rendered beside the child.
    expect(screen.getAllByText('Live updates degraded')).toHaveLength(1);
  });

  it('asChild over a button keeps it a button, with no wrapping element', () => {
    const { container } = render(
      <Badge tone="active" asChild>
        <button type="button">Healthy</button>
      </Badge>,
    );

    const button = screen.getByRole('button', { name: 'Healthy' });
    expect(button.className).toContain('bg-accent-active-subtle');
    // asChild merges onto the button itself — no <span> (or any other
    // element) wraps it (FR-002).
    expect(container.firstElementChild).toBe(button);
  });

  it('Appends className after the Badge classes', () => {
    render(
      <Badge tone="neutral" className="fixed bottom-3 right-3">
        Label
      </Badge>,
    );

    const badge = screen.getByText('Label');
    expect(badge.className).toContain('fixed');
    expect(badge.className).toContain('bottom-3');
    expect(badge.className).toContain('right-3');
  });
});
