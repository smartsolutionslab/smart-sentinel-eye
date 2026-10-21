// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { createRef } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { LinkButton } from './LinkButton.js';

/**
 * Spec 427 (issue #2668) — `LinkButton`: a text-only action whose colour is inherited from the
 * surrounding alert. Phase 4a is behaviour-changing (ADR-0139): the first four cases are observed
 * red against a declaration-only stub. The four PIN cases (no colour, ref, caller className, no
 * motion) are declared GREEN in advance: they hold at the stub for a trivial reason and exist to
 * stop the real primitive from breaking them.
 *
 * `apps/shared` carries no jest-dom, so classes and attributes are read off the element.
 */
afterEach(cleanup);

const FOCUS_RING = ['focus-visible:outline-2', 'focus-visible:outline-offset-2', 'focus-visible:outline-focus-ring'];

describe('LinkButton', () => {
  it('defaults to type="button" so it never submits a surrounding form', () => {
    render(<LinkButton>Retry</LinkButton>);

    expect(screen.getByRole('button', { name: 'Retry' }).getAttribute('type')).toBe('button');
  });

  it('honours an explicit type', () => {
    render(<LinkButton type="submit">Retry</LinkButton>);

    expect(screen.getByRole('button', { name: 'Retry' }).getAttribute('type')).toBe('submit');
  });

  it('is underlined and draws the design-system focus ring', () => {
    render(<LinkButton>Retry</LinkButton>);

    const { classList } = screen.getByRole('button', { name: 'Retry' });

    expect(classList.contains('underline')).toBe(true);
    for (const utility of FOCUS_RING) {
      expect(classList.contains(utility), utility).toBe(true);
    }
  });

  it('aria-disabled announces without natively disabling, and dims to the neutral disabled colour', () => {
    render(<LinkButton aria-disabled={true}>Retry</LinkButton>);

    const button = screen.getByRole('button', { name: 'Retry' });

    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(false);
    expect(button.classList.contains('aria-disabled:text-fg-disabled')).toBe(true);
  });

  it('PIN: chooses no colour, size, background or border at rest (it inherits from the alert)', () => {
    render(<LinkButton>Retry</LinkButton>);

    const unprefixed = [...screen.getByRole('button', { name: 'Retry' }).classList].filter(
      (name) => !name.includes(':') && /^(text|bg|border)-/.test(name),
    );

    expect(unprefixed).toEqual([]);
  });

  it('PIN: a ref reaches the native button element', () => {
    const ref = createRef<HTMLButtonElement>();
    render(<LinkButton ref={ref}>Retry</LinkButton>);

    expect(ref.current).toBe(screen.getByRole('button', { name: 'Retry' }));
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });

  it('PIN: the call-site className is kept', () => {
    render(<LinkButton className="aria-disabled:cursor-progress">Retry</LinkButton>);

    expect(screen.getByRole('button', { name: 'Retry' }).classList.contains('aria-disabled:cursor-progress')).toBe(
      true,
    );
  });

  it('PIN: no state animates (ADR-0146)', () => {
    render(<LinkButton>Retry</LinkButton>);

    const motion = [...screen.getByRole('button', { name: 'Retry' }).classList].filter((name) =>
      /^(transition|duration-|ease-|animate-)/.test(name),
    );

    expect(motion).toEqual([]);
  });
});
