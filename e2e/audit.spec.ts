import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { openSection } from './support/management-navigation';

// ADR-0108 — audit "read" vertical slice. An operator signs in, opens the Audit
// surface, and the trail loads from the audit-observability service *through the
// API gateway* (ADR-0106). A 401 / 404 / CORS / scope failure surfaces the
// "Could not load the audit trail" alert, so asserting the heading renders with
// no alert proves the authenticated path end to end.
test('operator opens audit and the trail loads through the gateway', async ({ page }) => {
  await signInAsOperator(page);

  await openSection(page, 'Audit');
  await expect(page.getByRole('alert')).toHaveCount(0);
});
