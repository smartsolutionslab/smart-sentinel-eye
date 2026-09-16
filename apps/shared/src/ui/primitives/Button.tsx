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
   * Reference implementations: `OverlayEditor.tsx` Undo/Redo (the handler
   * already no-ops), `ChainRecoveryNotice.tsx` Retry/Reload
   * (`if (reReading) return;`), `OverlayEditorDialog.tsx` /
   * `LayoutEditorDialog.tsx` Save (the form's `onSubmit`).
   */
  unavailable?: boolean;
}

// Custom design-system button (ADR-0077). Built on Radix Slot so it can wrap
// arbitrary children when asChild is set. Tailwind tokens via CSS custom
// properties (ADR-0078).
export function Button({ variant = 'primary', asChild, className, type, unavailable, ...rest }: ButtonProps) {
  const Component = asChild ? Slot : 'button';
  const base =
    'inline-flex items-center justify-center rounded-md px-4 py-2 text-sm font-medium ' +
    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 ' +
    'disabled:pointer-events-none disabled:opacity-50 transition-colors';
  const variants: Record<ButtonVariant, string> = {
    primary: 'bg-accent-active text-bg-base hover:opacity-90',
    secondary: 'border border-fg-muted text-fg-primary hover:bg-bg-elevated',
    ghost: 'text-fg-primary hover:bg-bg-elevated',
    // Reuses the fault token rather than adding one: it is already the
    // product's red, on error banners and the Offline health badge. A
    // destructive action reading as the same red an operator already knows
    // means trouble is the point.
    danger: 'bg-accent-fault text-bg-base hover:opacity-90',
  };
  return (
    <Component
      type={asChild ? undefined : (type ?? 'button')}
      aria-disabled={unavailable}
      className={clsx(base, variants[variant], unavailable !== undefined && 'aria-disabled:opacity-50', className)}
      {...rest}
    />
  );
}
