import { expect, type Page } from '@playwright/test';

/**
 * Spec 200 (issue #2279) — reading what the operator console's own token
 * holds, mirroring `e2e/support/kiosk-session.ts`'s
 * `readKioskAccessToken`/`sseScopesOf` pair.
 *
 * The console keeps its grant in a **different** storage than the kiosk. The
 * kiosk moved to `window.localStorage` under ADR-0131
 * (`apps/kiosk-web/src/app/auth.ts`); `apps/management-web/src/app/auth.ts`
 * sets no `userStore` at all, so `react-oidc-context` (via `oidc-client-ts`)
 * falls back to its own default, `window.sessionStorage` — already noted at
 * `e2e/overlays.spec.ts:59`. A helper that searched `localStorage` here would
 * find nothing and this test would fail as "no token", which reads nothing
 * like the scope finding it is meant to report (spec 041 hit exactly this).
 */
export async function readManagementAccessToken(page: Page): Promise<ManagementTokenClaims> {
  const payload = await page.evaluate(() => {
    const key = Object.keys(window.sessionStorage).find((candidate) => candidate.startsWith('oidc.user:'));
    if (key === undefined) {
      return null;
    }
    const stored: unknown = JSON.parse(window.sessionStorage.getItem(key) ?? 'null');
    const accessToken = (stored as { access_token?: unknown } | null)?.access_token;
    if (typeof accessToken !== 'string') {
      return null;
    }
    const [, claims] = accessToken.split('.');
    if (claims === undefined) {
      return null;
    }
    return JSON.parse(atob(claims.replace(/-/g, '+').replace(/_/g, '/'))) as unknown;
  });

  expect(payload, 'the console should be holding an access token').not.toBeNull();
  return payload as ManagementTokenClaims;
}

export interface ManagementTokenClaims {
  azp?: string;
  scope?: string;
  groups?: string[];
}
