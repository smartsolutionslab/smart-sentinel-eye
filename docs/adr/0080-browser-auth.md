# ADR-0080: Browser Auth — react-oidc-context + Custom Kiosk Flow

**Status:** Accepted
**Amended by:** ADR-0131 (the kiosk flow; the management-app half stands)
**Date:** 2026-05-25

## Context

ADR-0007 picks Keycloak per fab as the identity provider. ADR-0008
defines two distinct browser auth flows: **management app** uses
standard auth code with PKCE; **kiosk app** uses device-bound
`client_credentials`.

## Decision

**`react-oidc-context`** (which wraps `oidc-client-ts`) for the
**management app**:

> **Corrected 2026-10-08.** This sketch named a client (`smart-sentinel-eye-management`)
> and a scope (`sse.management`) that never existed in the realm — both predate
> spec 200/#2279, which repointed the console at `management-web`'s own
> default-granted scopes. Corrected to match `apps/management-web/src/app/auth.ts`
> as actually built.

```typescript
// apps/management-web/src/app/auth.ts
export const oidcConfig: AuthProviderProps = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: 'management-web',
  redirect_uri: `${window.location.origin}/`,
  // `openid` alone: the 21 granular sse.* scopes and sse-groups are DEFAULT
  // client scopes of `management-web`, applied whether or not asked for.
  // Naming a scope this client does not hold fails sign-in outright.
  scope: 'openid',
  onSigninCallback: (user) => { /* restores the deep link, spec 011 FR-013 */ },
};
```

- Hooks: `useAuth()` returns `{ user, isAuthenticated, signinRedirect,
  signoutRedirect, ... }`.
- Tokens attached to API requests via RTK Query's `prepareHeaders`.
- Silent renew handled automatically.

For the **kiosk app**, a custom auth flow (ADR-0008):

> **Amended by ADR-0131 (2026-08-30).** The paragraph and sketch below were
> never built, and **cannot be**: they put a device credential in "a secure
> local store", and a browser has none — anything the page can read, anyone at
> the screen can read. The kiosk does use `react-oidc-context`, contrary to
> what this ADR states. It now keeps its grant in storage that survives the
> browser process, so a screen recovers from a short interruption unattended. **The original text is kept below deliberately**: what
> was decided is a different record from what happened, and overwriting the
> first loses the reason the second was needed.

```typescript
// apps/kiosk-web/src/auth.ts
async function bootKioskToken(): Promise<AccessToken> {
  const cred = await loadDeviceCredential();  // from secure local store
  return fetchClientCredentialsToken({
    tokenEndpoint: KEYCLOAK_TOKEN_ENDPOINT,
    clientId: cred.clientId,
    clientSecret: cred.clientSecret,
    scope: 'sse.kiosk.view',
  });
}
```

- Kiosk does **not** use `react-oidc-context`; the flow is too
  different.
- Operator workstations sign in through the management app flow and
  **bind to a kiosk** via a separate API call to gain control scopes.

## Consequences

- **Positive:** mature OIDC library handles the bulk of the work.
- **Positive:** kiosk's custom flow is bounded and explicit.
- **Negative:** two auth patterns to maintain. Acceptable; they
  serve genuinely different threat models.

## Alternatives Considered

- **Plain `oidc-client-ts` without the React wrapper** — more code.
- **`keycloak-js`** — vendor-specific; older API.
- **Hand-rolled OIDC PKCE flow** — weeks of work plus maintenance.
