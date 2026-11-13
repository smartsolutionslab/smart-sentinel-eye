# Verification: Sign-in at the one origin

Observed, in the order the tasks ran. Nothing here is inferred.

## T001 — baseline (2026-11-13, branch `496-one-origin-sign-in`, clean tree, Release)

`dotnet test tests/Architecture.Tests -c Release --filter "WallClientDeclarationTests|RealmIdentityTests|KioskScopeParityTests|PasswordGrantClientTests|RealmAudienceTests"`:
**32 passed**, 0 failed. (Not the full `Architecture.Tests` run: it failed locally earlier because of a nested worktree, since removed, and a
full run is heavy on a machine that has been short of memory. CI runs it.)

## T002 — the defect, observed for all three apps (2026-11-13), before the fix

**Setup.** A fresh stack at this branch's pre-change tree: Release build (`-m:2`, no shared compiler, for memory), `dotnet run --project src/AppHost
-c Release --no-build -- ScenarioSimulator=false PersistentStack=false`, so a fresh Keycloak volume importing the realm as it stands. The
readiness script hit its 560 s cap with the machine at ~0.9 GB free memory (not every resource had reported), but the console, the gateway
and Keycloak answered, and the three sign-ins below reached the identity provider and returned a code, so Keycloak was serving the imported realm.
Observed with a scratch Chromium probe (kept outside the repo; the `code`, `state` and `session_state` values redacted), one app at a time, as
`operator` for the console and the kiosk and `wall-munich` for the wall display.

| App                 | Opened                           | After the Keycloak form                                                                                  | Heading shown      |
| ------------------- | -------------------------------- | -------------------------------------------------------------------------------------------------------- | ------------------ |
| Console             | `http://localhost:5180/`         | back at `http://localhost:5180/?state=…&iss=…&code=…`                                                    | **Sign-in failed** |
| Kiosk               | `http://localhost:5180/kiosk/`   | back at `http://localhost:5180/kiosk/oidc/callback?state=…&iss=…&code=…`                                 | **Reconnecting**   |
| Wall display        | `http://localhost:5180/wall/`    | back at `http://localhost:5180/wall/oidc/callback?state=…&iss=…&code=…`                                  | **Reconnecting**   |

For **each** of the three the browser logged the same two lines:

```text
CONSOLE ERROR Access to fetch at 'https://localhost:<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token'
  from origin 'http://localhost:5180' has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present on the requested resource.
REQUEST FAILED POST https://localhost:<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token - net::ERR_FAILED
```

**Premise 5 (the kiosk and the wall display fail too) is therefore observed, not predicted**, and it is the same cause for all three. One
consequence for the spec's assertion: the kiosk and the wall display do **not** say "Sign-in failed"; they sit on a "Reconnecting" screen at
their callback path. A check that only asserted the absence of a "Sign-in failed" heading would pass for them. The check must assert that
each app **reaches its first screen** (the console's *Cameras*, the others' *Pick a layout*), as plan decision 5 says.

**The counterfactual's "before"** (a token request with `client_id=management-web` and a bogus code, which Keycloak answers per client; only the
`Access-Control-Allow-Origin` header matters, the status is the bogus grant):

| `Origin`                | Response                                                       |
| ----------------------- | -------------------------------------------------------------- |
| `http://localhost:5173` | `400 Bad Request`, `Access-Control-Allow-Origin: http://localhost:5173` (admitted) |
| `http://localhost:5180` | `403 Forbidden`, **no** allow-origin (the defect)              |
| `http://localhost:5199` | `403 Forbidden`, **no** allow-origin (an unlisted origin)      |
