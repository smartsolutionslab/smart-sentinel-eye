# Quickstart — 051 tell an outage from a lockout

How to see the defect, how to see the fix, and what none of it establishes.

---

## Seeing the defect as it stands

The kiosk can be run against the development identity provider without booting
the rest of the stack — the auth states in `App.tsx` all return before the
router, so no backend is needed to reach them.

```sh
# the provider from a previous run is usually still up
docker ps --filter name=keycloak

cd apps/kiosk-web
VITE_KEYCLOAK_URL=https://127.0.0.1:10756 npx vite --port 5174 --strictPort
```

Sign in as `operator` / `Operator1234`, then in the page's console age the stored
grant so the next load must talk to the provider:

```js
const k = Object.keys(localStorage).find(x => x.includes('oidc.user:'));
const u = JSON.parse(localStorage.getItem(k));
u.expires_at = Math.floor(Date.now() / 1000) - 3600;
localStorage.setItem(k, JSON.stringify(u));
```

**The recoverable failure**: `docker stop <keycloak>`, reload. The screen reads
*"Sign-in failed / Failed to fetch"*. Start the provider again and **wait** —
this is the defect. Ninety seconds of a healthy provider, and the screen is
still dark.

**The refused failure**: with the provider up, disable `operator` through the
admin API, age the grant, reload. **The provider's own login form appears** —
a username and password prompt on what is meant to be a wall display.

> Re-enable the account afterwards. Every probe that changed the running
> provider during this spec was reverted, and the same applies here.

---

## What "done" looks like, per story

| Story | Done when | Not done merely because |
|---|---|---|
| **US1** | provider stopped, screen shows *Reconnecting*, provider restarted, and the wall returns **untouched within 2 minutes** | the screen shows a nicer message |
| **US2** | a disabled account produces a screen that says the screen is not authorized and **shows no credential field** | the app stopped erroring |
| **US3** | several screens' attempts land spread rather than together | jitter exists in the code |

---

## Inducing failures in an automated test

Route interception, because it runs in CI:

```ts
await page.route('**/protocol/openid-connect/token', (route) => route.abort('failed'));
```

`abort('failed')` produces the same `TypeError: Failed to fetch` a stopped
provider produces — checked, not assumed.

**Three things that will bite, all named in research §R5:**

1. **Assert the route actually fired.** `signinSilent` may run in a hidden
   iframe, and a pattern registered on the page alone can match nothing. *A test
   that intercepts nothing looks exactly like a test that passes.*
2. **`ErrorTimeout` needs its own case.** An aborted request never times out, so
   the timeout branch ships untested unless it is fulfilled deliberately.
3. **Stubbing a `server_error` body is a stub of the response, not of load.** It
   is the right test to write and it is not the same thing as an overloaded
   provider.

---

## What a fully green suite will still not establish

Stated here so `verification.md` cannot quietly imply otherwise.

- **That a wall stays recovered.** Every automated check watches one screen for
  seconds. Unattended operation is a property of twenty screens over days.
- **That twenty screens were exercised.** They will not be. US3 will be shown
  with a handful, and the property demonstrated is the spread, not the scale.
- **That the classification covers what a real provider emits.** The rule is
  written against the codes this provider was observed to send plus the OAuth
  registry. A provider under genuine load may answer in ways nobody here has
  seen, and unrecognised codes are deliberately treated as recoverable.
- **That a real network partition behaves like an aborted request.** DNS
  failure, TLS failure and a hung connection are all untested by interception.
- **Anything about production.** There is no production deployment (ADR-0130).

---

## Environment notes that cost time during this spec

- The Keycloak admin password is `dev-only-keycloak-admin`, readable from the
  container's environment — not `admin`.
- Containers from a previous run outlive the AppHost, so the provider is often
  already up; a **restarted** provider keeps its old realm, so a realm-file edit
  will not appear without deleting the volume.
- The dev provider serves HTTPS with a development certificate: browser contexts
  need `ignoreHTTPSErrors`, and `fetch` from Node needs the check disabled.
- The provider takes roughly 20 seconds to serve after `docker start`. Waiting
  for the realm's discovery document beats a fixed sleep.
