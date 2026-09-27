import { Slot } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { ComponentPropsWithRef } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

// `ComponentPropsWithRef<'button'>`, not `ButtonHTMLAttributes<HTMLButtonElement>`
// (spec 156, plan.md §3b): a strict superset that also carries `ref` — React
// 19.2.8 already passes `ref` through `{...rest}` onto the native element at
// runtime, so this widening is TypeScript-only and no call site changes.
export interface ButtonProps extends ComponentPropsWithRef<'button'> {
  variant?: ButtonVariant;
  asChild?: boolean;
  /**
   * Announce unavailability with `aria-disabled` instead of natively
   * disabling (ADR-0151). Reach for this — instead of `disabled` — for a
   * control that can become unavailable **while it holds focus**:
   * canonically, one where activating it is what makes it unavailable (Save
   * disabling on submit, Undo disabling once the stack is exhausted, Retry
   * disabling while it re-reads). A browser blurs a natively-`disabled`
   * element to `<body>` the instant it disables, and nothing restores focus
   * — the operator's next `Tab` starts from the top of the document.
   * `disabled` is still correct for a control that cannot hold focus when it
   * disables (e.g. a bulk action disabled because nothing is selected).
   *
   * **The guard is part of the rule, not a follow-up.** `aria-disabled` is
   * an announcement, not a behaviour — this prop does not stop `onClick`
   * from firing, and nothing here can enforce a guard, because the guard is
   * a statement inside a handler body, or on a different element entirely.
   * Without one, the control is merely lying about being unavailable. For a
   * submit button the guard belongs on the **form's** `onSubmit`, before
   * validation, because implicit submission (Enter in a text field) never
   * goes through the button's `onClick`. A new submit-button site owes a
   * real-browser Playwright test that presses `Enter` in a text field (not
   * on the button) and proves the guard discriminates by disabling it and
   * watching the request count rise — `user-event`'s Enter handling
   * dispatches a synthetic click and cannot exercise implicit submission.
   *
   * Do not pass this together with `disabled` — the browser enforces
   * `disabled` regardless, and the control loses focus anyway.
   *
   * This prop only ever emits the neutral disabled/unavailable treatment
   * (spec 268, issue #2336: a bordered, unfilled fill with a `text-fg-disabled`
   * label — no opacity fade, ADR-0146 item 5); cursor treatment (e.g.
   * `aria-disabled:cursor-progress`) stays a call-site `className`, because
   * `cursor-progress` claims *busy*, not *unavailable*, and not every
   * unavailable control is mid-request.
   *
   * Reference implementations: `OverlayEditor.tsx` Undo/Redo (the handler
   * already no-ops), `ChainRecoveryNotice.tsx` Retry/Reload
   * (`if (reReading) return;`), `OverlayEditorDialog.tsx` /
   * `LayoutEditorDialog.tsx` Save (the form's `onSubmit`).
   */
  unavailable?: boolean;
  /**
   * In flight. Announces `aria-busy` and shows `cursor-progress`; never
   * disables — pass `disabled` or `unavailable` for that (spec 268, issue
   * #2336, US2).
   *
   * **Busy wins over the disabled/unavailable look, not over disabled
   * semantics.** At every adoption site the button is also `disabled` or
   * `unavailable` while its request is in flight, so while `busy` is set the
   * neutral disabled/unavailable *colour* treatment (and hover/pressed
   * feedback) is dropped and the variant's rest fill holds instead — a
   * control that is working, not one that has been refused. The native
   * `disabled` attribute, `disabled:pointer-events-none` and `aria-disabled`
   * are untouched: which one a call site uses is ADR-0151's call, unaffected
   * by `busy`.
   */
  busy?: boolean;
}

// Custom design-system button (ADR-0077). Built on Radix Slot so it can wrap
// arbitrary children when asChild is set. Tailwind tokens via CSS custom
// properties (ADR-0078).
export function Button({
  variant = 'primary',
  asChild,
  className,
  type,
  unavailable,
  busy,
  'aria-disabled': ariaDisabled,
  ...domProps
}: ButtonProps) {
  const Component = asChild ? Slot : 'button';
  // No `border-transparent` here — it used to sit here unconditionally, tied
  // in specificity with `restFill.secondary`'s `border-border-strong`, and
  // Tailwind 4.3.3 emits `.border-transparent` after `.border-border-strong`
  // in the compiled sheet, so the later rule won the tie and secondary's rest
  // border never rendered. Each variant now supplies its own border colour
  // below, so at most one border-colour utility is ever in play per variant
  // and there is nothing left to race.
  const base =
    'inline-flex items-center justify-center rounded-md border px-4 py-2 ' +
    'text-sm font-medium transition-colors ' +
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring ' +
    'disabled:pointer-events-none';

  // Omitted while busy (below), so an in-flight control keeps its rest fill.
  const disabledTreatment = 'disabled:border-border-subtle disabled:bg-transparent disabled:text-fg-disabled';

  const restFill: Record<ButtonVariant, string> = {
    primary: 'border-transparent bg-accent text-fg-on-accent',
    secondary: 'border-border-strong text-fg-primary',
    ghost: 'border-transparent text-fg-primary',
    // Reuses the fault token rather than adding one: it is already the
    // product's red, on error banners and the Offline health badge. A
    // destructive action reading as the same red an operator already knows
    // means trouble is the point.
    danger: 'border-transparent bg-accent-fault text-fg-on-fault',
  };

  const interactive: Record<ButtonVariant, string> = {
    primary: 'hover:bg-accent-hover active:bg-accent-pressed',
    secondary: 'hover:bg-bg-hover active:bg-bg-pressed',
    ghost: 'hover:bg-bg-hover active:bg-bg-pressed',
    danger: 'hover:bg-accent-fault-hover active:bg-accent-fault-pressed',
  };

  // Omitted while busy (below), for the same reason as disabledTreatment.
  const unavailableTreatment =
    'aria-disabled:border-border-subtle aria-disabled:bg-transparent aria-disabled:text-fg-disabled';

  return (
    <Component
      type={asChild ? undefined : (type ?? 'button')}
      aria-disabled={unavailable ?? ariaDisabled}
      aria-busy={busy || undefined}
      className={clsx(
        base,
        restFill[variant],
        !busy && interactive[variant],
        !busy && disabledTreatment,
        !busy && unavailable !== undefined && unavailableTreatment,
        busy && 'cursor-progress',
        className,
      )}
      {...domProps}
    />
  );
}
