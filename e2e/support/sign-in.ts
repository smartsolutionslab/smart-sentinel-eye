import { expect, type Page } from '@playwright/test';

// ADR-0108 — shared e2e sign-in. Drives the real Keycloak login as the seeded
// `operator` and lands on the management shell (which opens on Cameras). The
// operator's token carries `sse.management`, which grandfathers the granular
// `sse.*` policies (ServiceDefaults RequireScopeExtensions).
export async function signInAsOperator(page: Page): Promise<void> {
  await page.goto('/');

  // Unauthenticated: the management shell shows the sign-in screen.
  await page.getByRole('button', { name: /sign in/i }).click();

  // Real Keycloak login form (standard login-theme element ids).
  await page.locator('#username').fill('operator');
  await page.locator('#password').fill('Operator1234');
  await page.locator('#kc-login').click();

  // Back in the app, authenticated — the shell renders (opens on Cameras).
  await expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible();
}
