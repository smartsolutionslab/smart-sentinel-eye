import { test, expect } from '@playwright/test';
import { signInAsOperator } from './support/sign-in';
import { readManagementAccessToken } from './support/management-session';

/**
 * Spec 200 (issue #2279), SC-5 — what the running console actually holds.
 *
 * `ConsoleScopeGrantIntegrationTests` (SC-4) asks what Keycloak *will* mint
 * for `management-web`; this asks what the browser *is holding* after a real
 * sign-in through the app's own OIDC flow. They are different questions —
 * `guards that read the design artefact` is the standing lesson for why both
 * are needed.
 *
 * If `auth.ts` ever regresses to signing in as `smart-sentinel-eye-web` with
 * `scope: 'openid sse.management'`, `azp` would read `smart-sentinel-eye-web`
 * and `scope` would carry `sse.management` again — the exact shape of #2279.
 */
test('the console carries management-web scopes, not the legacy bundle', async ({ page }) => {
  await signInAsOperator(page);

  const claims = await readManagementAccessToken(page);

  expect(claims.azp).toBe('management-web');
  expect(claims.scope ?? '').not.toContain('sse.management');

  // Control: sse-groups is a default scope of both clients. A repoint that
  // lost it would break every fab-scoped read (ADR-0114) while every scope
  // assertion above stayed green.
  expect(claims.groups).toContain('/fabs/munich');
});
