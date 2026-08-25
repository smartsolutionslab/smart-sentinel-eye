import { test, expect } from '@playwright/test';
import { readKioskAccessToken, signInToKiosk, sseScopesOf } from './support/kiosk-session';

/**
 * Spec 041 US2 — what a kiosk carries.
 *
 * These assertions are on the **token**, not on behaviour, because behaviour
 * cannot see them. A kiosk holding write-everything authority lists layouts,
 * opens walls and passes every other check in this suite identically to one
 * that holds nothing but reads. The absence exists nowhere else.
 *
 * `KeycloakScopeBundles.Kiosk` is the set Identity grants every kiosk device it
 * enrols. The browser kiosk was the only kiosk in the system not holding it —
 * and the only one holding the management bundle, on the least physically
 * secure surface in the product.
 */
const KIOSK_PERSONA = [
  'sse.cameras.read',
  'sse.events.write',
  'sse.layouts.read',
  'sse.overlays.read',
  'sse.streams.read',
  'sse.variables.read',
];

test('a kiosk carries its operator’s fab', async ({ page }) => {
  await signInToKiosk(page);

  const claims = await readKioskAccessToken(page);

  expect(claims.azp).toBe('kiosk-web');
  expect(claims.groups).toContain('/fabs/munich');
});

test('a kiosk does not carry the management bundle', async ({ page }) => {
  await signInToKiosk(page);

  const claims = await readKioskAccessToken(page);

  // SC-002. Asserted as an absence: a check that only confirms the kiosk works
  // passes just as happily with this scope restored, which is how the weakness
  // comes back.
  expect(claims.scope ?? '').not.toContain('sse.management');
});

test('a kiosk carries exactly the scopes an enrolled kiosk device gets', async ({ page }) => {
  await signInToKiosk(page);

  const claims = await readKioskAccessToken(page);

  // SC-003, compared as a set rather than sampled — a scope added to either
  // side must fail. `KioskScopeParityTests` guards the same pair statically;
  // this is the half that observes what Keycloak actually minted.
  expect([...sseScopesOf(claims)].sort()).toEqual(KIOSK_PERSONA);
});
