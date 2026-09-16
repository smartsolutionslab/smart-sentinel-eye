// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Button } from './Button.js';

/**
 * Spec 163 (issue #2399) T002 — Phase 4a is BEHAVIOUR-CHANGING for this file:
 * new tests must be observed RED before `Button.tsx` gains the `unavailable`
 * prop (ADR-0139/ADR-0144).
 *
 * **Case 4 below ("no unavailable prop") is declared GREEN in advance.** It
 * passes today because `unavailable` does not exist yet, so rendering
 * without it is — trivially — unchanged. That is a pin on what must not
 * move, not a phase-4a failure; do not read its green result as evidence the
 * whole file passed.
 *
 * **Observed on unmodified `develop`: cases 1, 2 and 3 are RED. Case 5 is
 * ALSO green, and that is a finding, not a design choice** — see its own
 * doc comment. The task brief for this slice expected case 5 to be red too
 * (`unavailable` "falls into `...rest`"), but React itself silently drops an
 * unrecognised attribute when its value is a plain boolean, independent of
 * whether `Button.tsx` destructures the prop — so this assertion cannot be
 * made to discriminate the way FR-004's counterfactual (D5) describes.
 * Recorded here rather than forced red by a flaky construction, per this
 * repo's own rule: a test that is green on first run has not established
 * anything, and adjusting it until it goes red is not the fix.
 *
 * vitest transpiles with esbuild and does not typecheck, so this file runs
 * against a `Button` that has no `unavailable` prop at all — it lands in
 * `...rest` and is spread onto the DOM element, so the assertions below fail
 * at runtime. `npm run typecheck` separately reports `Property 'unavailable'
 * does not exist on type 'ButtonProps'` for the duration of phase 4a; that
 * compile error is NOT the red recorded here.
 *
 * `apps/shared` does not carry `@testing-library/jest-dom` (only
 * `apps/management-web` does — see `OverlayEditorBackdrop.test.tsx`'s own
 * comment), so attributes and classes are read directly off the element
 * rather than via `toHaveAttribute()`/`toHaveClass()`.
 */
afterEach(cleanup);

function ariaDisabled(el: HTMLElement): string | null {
  return el.getAttribute('aria-disabled');
}

function hasClass(el: HTMLElement, className: string): boolean {
  return el.classList.contains(className);
}

describe('Button', () => {
  it('unavailable announces without natively disabling', () => {
    render(<Button unavailable>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(ariaDisabled(button)).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(false);
    expect(hasClass(button, 'aria-disabled:opacity-50')).toBe(true);
  });

  it('an unavailable Button keeps the operators place: it stays focusable and its onClick still fires', () => {
    const onClick = vi.fn();
    render(
      <Button unavailable onClick={onClick}>
        Save
      </Button>,
    );

    const button = screen.getByRole('button', { name: /save/i });

    // The button must actually BE unavailable (aria-disabled="true") for
    // "still focusable, still receives clicks while unavailable" to mean
    // anything — without this line the assertions below hold trivially on
    // unmodified `develop`, where `unavailable` has no effect at all, and
    // would establish nothing (this is the ADR's whole point: a NATIVE
    // `disabled` here would blur the element and swallow the click).
    expect(ariaDisabled(button)).toBe('true');

    button.focus();
    expect(document.activeElement).toBe(button);

    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('unavailable={false} renders aria-disabled="false", not the attributes absence', () => {
    render(<Button unavailable={false}>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(ariaDisabled(button)).toBe('false');
  });

  /**
   * Expected GREEN on `develop` — declared in advance (see file doc comment).
   * With no `unavailable` prop at all, rendering must be byte-identical to
   * today's: no `aria-disabled`, no `aria-disabled:opacity-50`, and the base
   * native-`disabled` classes still present.
   */
  it('no unavailable prop leaves rendering unchanged', () => {
    render(<Button>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(hasClass(button, 'aria-disabled:opacity-50')).toBe(false);
    expect(hasClass(button, 'disabled:opacity-50')).toBe(true);
    expect(hasClass(button, 'disabled:pointer-events-none')).toBe(true);
  });

  /**
   * FR-004: `unavailable` must be consumed by `Button` and never handed to
   * the DOM element at all.
   *
   * **Observed GREEN on unmodified `develop` — not by design, and reported
   * rather than forced.** `unavailable` is not destructured today, so it
   * genuinely falls into `...rest` and is spread onto the native
   * `<button>` (confirmed: React logs "Received `true` for a non-boolean
   * attribute `unavailable`" for exactly this render, in the case above).
   * But React itself then refuses to *write* an unrecognised attribute
   * whose value is a boolean — a stable, dev-mode-only behaviour, not
   * something `Button.tsx` controls — so `hasAttribute('unavailable')`
   * reads `false` whether or not `Button` ever destructures the prop. A
   * `console.error`-spy version of this test was tried and is worse: React
   * also de-duplicates that warning per attribute name for the process
   * lifetime, so it only fires on the FIRST render anywhere in the file
   * that spreads `unavailable` onto a DOM node (here, the case above) and
   * is silent on every later one — making such a test pass or fail by test
   * order, which is the flakiness this repo's conventions rule out. Kept
   * as a plain, non-flaky pin: it is expected to stay green after T005
   * too, so a regression here would mean the *new* destructuring was
   * removed, not that it was never added.
   */
  it('unavailable never reaches the DOM as a stray attribute', () => {
    render(<Button unavailable>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('unavailable')).toBe(false);
  });
});
