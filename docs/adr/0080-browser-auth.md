# ADR-0080: Browser Auth — react-oidc-context + Custom Kiosk Flow

**Status:** Accepted
**Date:** 2026-05-25

## Context

ADR-0007 picks Keycloak per fab as the identity provider. ADR-0008
defines two distinct browser auth flows: **management app** uses
standard auth code with PKCE; **kiosk app** uses device-bound
`client_credentials`.

## Decision

**`react-oidc-context`** (which wraps `oidc-client-ts`) for the
**management app**:

```typescript
// apps/management-web/src/main.tsx
<AuthProvider
  authority="https://keycloak.fab.local/realms/sse"
  client_id="smart-sentinel-eye-management"
  redirect_uri={window.location.origin + '/auth/callback'}
  scope="openid profile sse.management"
  automaticSilentRenew
>
  <App />
</AuthProvider>
```

- Hooks: `useAuth()` returns `{ user, isAuthenticated, signinRedirect,
  signoutRedirect, ... }`.
- Tokens attached to API requests via RTK Query's `prepareHeaders`.
- Silent renew handled automatically.

For the **kiosk app**, a custom auth flow (ADR-0008):

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
