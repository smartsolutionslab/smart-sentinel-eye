// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Button } from './Button.js';

/**
 * Spec 163 (issue #2399) T002 — Phase 4a is BEHAVIOUR-CHANGING for this file:
 * new tests must be observed RED before `Button.tsx` gains the `unavailable`
 * prop (ADR-0139/ADR-0144).
 *
 * **Case 4 below ("no unavailable prop") was declared GREEN in advance for
 * spec 163.** It passed then because `unavailable` did not exist yet, so
 * rendering without it was — trivially — unchanged. That was a pin on what
 * must not move, not a phase-4a failure; do not read its green result as
 * evidence the whole file passed. **Spec 268 (below) rewrites case 4's
 * native-`disabled` assertions, and that half is red again** — see spec 268's
 * own paragraph and that case's doc comment.
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
 *
 * **Spec 268 (issue #2336), T003 — rewrites cases 1 and 4 below, plan.md
 * §5.2.** `Button.tsx`'s disabled treatment is moving from `opacity-50` (this
 * spec's whole finding: ADR-0146 item 5, "a uniform opacity fade is not a
 * state") to a neutral `text-fg-disabled` label at ≥3:1 contrast, no fill, a
 * subtle border. The two class-string assertions below are rewritten to that
 * target class; **the ADR-0151 half of each case is kept verbatim** — this
 * spec touches Button's colour treatment, not its focus-retention contract.
 * `busy` is spec 268's own new prop (US2): a static in-flight state that
 * announces `aria-busy`, shows `cursor-progress`, and — the precedence fix —
 * keeps the variant's rest fill instead of the disabled/unavailable colour
 * treatment, because every real adoption site is already `disabled` or
 * `unavailable` while busy (spec §1 finding 7). Observed RED on unmodified
 * `develop`: the rewritten halves of cases 1 and 4, and every `busy` case
 * below (`busy` does not exist on `Button.tsx` yet beyond the type-only
 * declaration T003 adds, so it always falls into `...rest` and never reaches
 * `aria-busy`, `cursor-progress`, or any precedence over the old
 * `disabled`/`aria-disabled` opacity classes).
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
    // Spec 268 (issue #2336) T003 — rewritten from 'aria-disabled:opacity-50':
    // the disabled/unavailable treatment is a neutral fg-disabled label, not
    // an opacity fade (ADR-0146 item 5). The two ADR-0151 lines above are
    // unchanged.
    expect(hasClass(button, 'aria-disabled:text-fg-disabled')).toBe(true);
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
   * With no `unavailable` prop at all, no `aria-disabled` class or attribute
   * applies — that part is unchanged and still green on develop.
   *
   * **Spec 268 (issue #2336) T003 rewrites the native-`disabled` half, and
   * that half is now RED on unmodified `develop`**: the base disabled
   * treatment moves from `disabled:opacity-50` to `disabled:text-fg-disabled`
   * (ADR-0146 item 5 — no opacity fade behind a state). This case is no
   * longer a declared-green pin as a whole; only the `aria-disabled`
   * assertion above it still is.
   */
  it('no unavailable prop leaves rendering unchanged', () => {
    render(<Button>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(hasClass(button, 'aria-disabled:opacity-50')).toBe(false);
    expect(hasClass(button, 'disabled:opacity-50')).toBe(false);
    expect(hasClass(button, 'disabled:text-fg-disabled')).toBe(true);
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

/**
 * Spec 268 (issue #2336) T003, US2 — `busy` (plan.md §3, §5.2). A static
 * in-flight state: `aria-busy`, `cursor-progress`, the variant's rest fill
 * held. It never sets `disabled`/`aria-disabled` itself (that stays the call
 * site's own `disabled`/`unavailable` — ADR-0151's call), and while it is set
 * the disabled/unavailable *colour* treatment is dropped so the rest fill
 * shows through even at a site that is also `disabled` or `unavailable`
 * while its request is in flight (spec §1 finding 7 — every real busy
 * adoption site is one of those two already).
 *
 * **All red on unmodified `develop`.** `busy` is, for now, a type-only
 * declaration on `ButtonProps` (T003) — `Button` does not destructure or
 * render anything from it, so it falls into `...rest` exactly as
 * `unavailable` once did, and none of the assertions below can pass.
 */
describe('Button busy state', () => {
  function hasAnyClassStartingWith(el: HTMLElement, prefix: string): boolean {
    return Array.from(el.classList).some((className) => className.startsWith(prefix));
  }

  it('busy announces aria-busy and shows a progress cursor', () => {
    render(<Button busy>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(hasClass(button, 'cursor-progress')).toBe(true);
  });

  /**
   * **Declared GREEN in advance — a pin, not phase-4a evidence.** This is the
   * "nothing in flight" case (spec §4's own gherkin calls it a "bad request"
   * scenario): with no `busy` prop, there is nothing to announce whether or
   * not `busy` exists at all, so this holds unmodified today and must still
   * hold once `busy` is implemented.
   */
  it('no busy prop leaves the button unannounced: no aria-busy attribute at all', () => {
    render(<Button>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('aria-busy')).toBe(false);
  });

  /**
   * **Declared GREEN in advance — a pin, not phase-4a evidence.** `busy` must
   * never itself set `disabled`/`aria-disabled` (spec §4: "busy composes
   * with, and never replaces, disabled/unavailable" — that stays each call
   * site's own choice, ADR-0151). The assertion holds today only because
   * `busy` does not exist, but it must hold identically once it does — an
   * implementation that started setting either attribute from `busy` alone
   * would need to turn this case red, which is exactly what it is here to
   * catch.
   */
  it('busy alone sets neither disabled nor aria-disabled', () => {
    render(<Button busy>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('disabled')).toBe(false);
    expect(button.hasAttribute('aria-disabled')).toBe(false);
  });

  it('busy and disabled together: the button is natively disabled', () => {
    render(
      <Button busy disabled>
        Save
      </Button>,
    );

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('disabled')).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
  });

  /**
   * **Declared GREEN in advance — a pin, not phase-4a evidence.** `unavailable`
   * already sets `aria-disabled` and keeps focus on its own (ADR-0151,
   * unaffected by this file's other describe block); `busy` must never take
   * that away. Holds today because `busy` does nothing yet, and must hold
   * identically afterwards.
   */
  it('busy and unavailable together: aria-disabled is set and the button stays focusable', () => {
    render(
      <Button busy unavailable>
        Save
      </Button>,
    );

    const button = screen.getByRole('button', { name: /save/i });

    expect(ariaDisabled(button)).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(false);
    button.focus();
    expect(document.activeElement).toBe(button);
  });

  it('busy keeps the rest fill instead of the disabled treatment while natively disabled', () => {
    render(
      <Button busy disabled>
        Save
      </Button>,
    );

    const button = screen.getByRole('button', { name: /save/i });

    // None of disabledTreatment's colour classes (plan.md §3) — only the
    // structural disabled:pointer-events-none survives.
    expect(hasClass(button, 'disabled:border-border-subtle')).toBe(false);
    expect(hasClass(button, 'disabled:bg-transparent')).toBe(false);
    expect(hasClass(button, 'disabled:text-fg-disabled')).toBe(false);
    expect(hasClass(button, 'disabled:pointer-events-none')).toBe(true);
    // The default primary variant's rest fill is still there.
    expect(hasClass(button, 'bg-accent')).toBe(true);
  });

  it('busy keeps the rest fill instead of the unavailable treatment while unavailable', () => {
    render(
      <Button busy unavailable>
        Save
      </Button>,
    );

    const button = screen.getByRole('button', { name: /save/i });

    // None of unavailableTreatment's colour classes (plan.md §3).
    expect(hasClass(button, 'aria-disabled:border-border-subtle')).toBe(false);
    expect(hasClass(button, 'aria-disabled:bg-transparent')).toBe(false);
    expect(hasClass(button, 'aria-disabled:text-fg-disabled')).toBe(false);
    expect(hasClass(button, 'bg-accent')).toBe(true);
  });

  it('busy suppresses hover and pressed feedback: no hover:/active: class is present', () => {
    render(<Button busy>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(hasAnyClassStartingWith(button, 'hover:')).toBe(false);
    expect(hasAnyClassStartingWith(button, 'active:')).toBe(false);
  });

  /**
   * **Observed GREEN on unmodified `develop` — not by design, and reported
   * rather than forced, the same finding this file's own `unavailable`
   * "never reaches the DOM" case (above) records.** `busy` is not
   * destructured today, so it falls into `...rest` and is spread onto the
   * native `<button>` (confirmed: React logs "Received `true` for a
   * non-boolean attribute `busy`" for exactly this render) — but React
   * itself refuses to *write* an unrecognised attribute whose value is a
   * plain boolean, independent of whether `Button.tsx` ever destructures the
   * prop. So `hasAttribute('busy')` reads `false` whether or not `busy` is
   * implemented. Kept as a plain, non-flaky pin: it is expected to stay green
   * after T013 too, so a regression here would mean destructuring was
   * removed, not that it was never added.
   */
  it('busy never reaches the DOM as a stray attribute', () => {
    render(<Button busy>Save</Button>);

    const button = screen.getByRole('button', { name: /save/i });

    expect(button.hasAttribute('busy')).toBe(false);
  });
});
