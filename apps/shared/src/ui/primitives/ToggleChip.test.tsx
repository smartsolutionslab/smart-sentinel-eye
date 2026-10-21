// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { ToggleChip, type ToggleChipProps } from './ToggleChip.js';

/**
 * Spec 427 (issue #2668) — `ToggleChip`: a native button with a required `pressed`, announced as
 * `aria-pressed`. Phase 4a is behaviour-changing (ADR-0139): the cases not marked PIN are observed
 * red against a declaration-only stub. The two PIN cases and the "pressed carries no hover or
 * active fill" case are declared GREEN in advance (they hold trivially at the stub).
 *
 * `apps/shared` carries no jest-dom, so classes and attributes are read off the element.
 */
afterEach(cleanup);

const FOCUS_RING = ['focus-visible:outline-2', 'focus-visible:outline-offset-2', 'focus-visible:outline-focus-ring'];

describe('ToggleChip', () => {
  it('pressed announces aria-pressed="true" and resolves by its pressed state', () => {
    render(<ToggleChip pressed>Archived</ToggleChip>);

    const chip = screen.getByRole('button', { name: 'Archived', pressed: true });

    expect(chip.getAttribute('aria-pressed')).toBe('true');
  });

  it('unpressed announces aria-pressed="false" and resolves by its pressed state', () => {
    render(<ToggleChip pressed={false}>All</ToggleChip>);

    const chip = screen.getByRole('button', { name: 'All', pressed: false });

    expect(chip.getAttribute('aria-pressed')).toBe('false');
  });

  it('pressed is drawn in the accent', () => {
    render(<ToggleChip pressed>Archived</ToggleChip>);

    const { classList } = screen.getByRole('button', { name: 'Archived' });

    expect(classList.contains('border-accent')).toBe(true);
    expect(classList.contains('bg-accent-subtle')).toBe(true);
    expect(classList.contains('text-accent')).toBe(true);
  });

  it('unpressed is drawn muted with no accent', () => {
    render(<ToggleChip pressed={false}>All</ToggleChip>);

    const { classList } = screen.getByRole('button', { name: 'All' });

    expect(classList.contains('border-border-subtle')).toBe(true);
    expect(classList.contains('text-fg-muted')).toBe(true);
    expect(classList.contains('border-accent')).toBe(false);
    expect(classList.contains('bg-accent-subtle')).toBe(false);
    expect(classList.contains('text-accent')).toBe(false);
  });

  it('unpressed offers hover and press feedback', () => {
    render(<ToggleChip pressed={false}>All</ToggleChip>);

    const { classList } = screen.getByRole('button', { name: 'All' });

    expect(classList.contains('hover:bg-bg-hover')).toBe(true);
    expect(classList.contains('active:bg-bg-pressed')).toBe(true);
  });

  it('PIN: pressed has no hover or active fill that would override its accent fill', () => {
    render(<ToggleChip pressed>Archived</ToggleChip>);

    const fills = [...screen.getByRole('button', { name: 'Archived' }).classList].filter(
      (name) => name.startsWith('hover:bg-') || name.startsWith('active:bg-'),
    );

    expect(fills).toEqual([]);
  });

  it('draws the design-system focus ring and the established chip geometry', () => {
    render(<ToggleChip pressed={false}>All</ToggleChip>);

    const { classList } = screen.getByRole('button', { name: 'All' });

    for (const utility of [...FOCUS_RING, 'rounded-md', 'border', 'px-3', 'py-1', 'text-sm']) {
      expect(classList.contains(utility), utility).toBe(true);
    }
  });

  it('defaults to type="button"', () => {
    render(<ToggleChip pressed={false}>All</ToggleChip>);

    expect(screen.getByRole('button', { name: 'All' }).getAttribute('type')).toBe('button');
  });

  it('PIN: pressed is consumed, never written to the DOM as an attribute', () => {
    render(<ToggleChip pressed>Archived</ToggleChip>);

    expect(screen.getByRole('button', { name: 'Archived' }).hasAttribute('pressed')).toBe(false);
  });

  it('PIN: no state animates (ADR-0146)', () => {
    render(<ToggleChip pressed>Archived</ToggleChip>);

    const motion = [...screen.getByRole('button', { name: 'Archived' }).classList].filter((name) =>
      /^(transition|duration-|ease-|animate-)/.test(name),
    );

    expect(motion).toEqual([]);
  });

  // TypeScript does not excess-check a hyphenated JSX attribute, so `aria-pressed` can still reach
  // the chip through a spread; `pressed` must win.
  it('pressed alone decides aria-pressed even when aria-pressed is passed to a pressed chip', () => {
    const override: Record<string, string> = { 'aria-pressed': 'false' };
    render(
      <ToggleChip pressed {...override}>
        Archived
      </ToggleChip>,
    );

    expect(screen.getByRole('button', { name: 'Archived' }).getAttribute('aria-pressed')).toBe('true');
  });

  it('pressed alone decides aria-pressed even when aria-pressed is passed to an unpressed chip', () => {
    const override: Record<string, string> = { 'aria-pressed': 'true' };
    render(
      <ToggleChip pressed={false} {...override}>
        Archived
      </ToggleChip>,
    );

    expect(screen.getByRole('button', { name: 'Archived' }).getAttribute('aria-pressed')).toBe('false');
  });
});

/**
 * Compile-time contract, checked by `tsc --noEmit` and never called: `pressed` is required and
 * `aria-pressed` is not an accepted prop, because the chip owns that attribute. JSX does not
 * type-check an undeclared hyphenated attribute, so the second case is an object literal.
 */
export function typeContract() {
  // @ts-expect-error `pressed` is required
  const missingPressed: ToggleChipProps = {};
  // @ts-expect-error `aria-pressed` is owned by `pressed`
  const ownedAttribute: ToggleChipProps = { pressed: true, 'aria-pressed': false };

  return [missingPressed, ownedAttribute];
}
