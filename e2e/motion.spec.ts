import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';

// Spec 292 (issue #2334), tasks.md T002, plan.md §7 item 5 — pins Button's rendered
// transition timing exactly as it arrives today, through the
// `--default-transition-duration`/`--default-transition-timing-function` bridge
// (spec 292 §1). CHARACTERISATION (ADR-0139/ADR-0144): observed green on `develop`
// *before* Button.tsx migrates to `transition-colors duration-state ease-state`
// (T011), and must pass unmodified afterwards — `duration-state`/`ease-state`
// resolve to the same `--duration-fast`/`--ease-out` scale steps (plan.md §2), so
// the computed values must not move.
test.describe('Button keeps its rendered timing (spec 292 characterisation)', () => {
  test('computed transition-duration is 0.12s, transition-timing-function is cubic-bezier(0.2, 0, 0, 1)', async ({
    page,
  }) => {
    await signInAsOperator(page);

    const registerButton = page.getByRole('button', { name: /register camera/i });
    await expect(registerButton).toHaveCSS('transition-duration', '0.12s');
    await expect(registerButton).toHaveCSS('transition-timing-function', 'cubic-bezier(0.2, 0, 0, 1)');
  });
});
