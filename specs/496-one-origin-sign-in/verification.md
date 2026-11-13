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

| App          | Opened                         | After the Keycloak form                                                  | Heading shown      |
| ------------ | ------------------------------ | ------------------------------------------------------------------------ | ------------------ |
| Console      | `http://localhost:5180/`       | back at `http://localhost:5180/?state=…&iss=…&code=…`                    | **Sign-in failed** |
| Kiosk        | `http://localhost:5180/kiosk/` | back at `http://localhost:5180/kiosk/oidc/callback?state=…&iss=…&code=…` | **Reconnecting**   |
| Wall display | `http://localhost:5180/wall/`  | back at `http://localhost:5180/wall/oidc/callback?state=…&iss=…&code=…`  | **Reconnecting**   |

For **each** of the three the browser logged the same two lines:

```text
CONSOLE ERROR Access to fetch at 'https://localhost:<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token'
  from origin 'http://localhost:5180' has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present on the requested resource.
REQUEST FAILED POST https://localhost:<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token - net::ERR_FAILED
```

**Premise 5 (the kiosk and the wall display fail too) is therefore observed, not predicted**, and it is the same cause for all three. One
consequence for the spec's assertion: the kiosk and the wall display do **not** say "Sign-in failed"; they sit on a "Reconnecting" screen at
their callback path. A check that only asserted the absence of a "Sign-in failed" heading would pass for them. The check must assert that
each app **reaches its first screen** (the console's _Cameras_, the others' _Pick a layout_), as plan decision 5 says.

**The counterfactual's "before"** (a token request with `client_id=management-web` and a bogus code, which Keycloak answers per client; only the
`Access-Control-Allow-Origin` header matters, the status is the bogus grant):

| `Origin`                | Response                                                                           |
| ----------------------- | ---------------------------------------------------------------------------------- |
| `http://localhost:5173` | `400 Bad Request`, `Access-Control-Allow-Origin: http://localhost:5173` (admitted) |
| `http://localhost:5180` | `403 Forbidden`, **no** allow-origin (the defect)                                  |
| `http://localhost:5199` | `403 Forbidden`, **no** allow-origin (an unlisted origin)                          |

## T004 — the browser spec, red ×3 (2026-11-13), on the stack above (pre-change realm)

`e2e/one-origin-sign-in.spec.ts` (`--project=chromium --workers=1`): each test opens its app at the one origin, completes the real Keycloak
form, and then asserts the app's first screen. All three fail at that assertion, **after** the form, so the red is the missing first screen and
not a sign-in that never started:

```text
the operator console signs in at the one origin
    Locator: getByRole('heading', { name: 'Cameras', exact: true })   Expected: visible   Error: element(s) not found (30000ms)
the kiosk signs in at /kiosk/ on the one origin
    Locator: getByRole('heading', { name: 'Pick a layout', exact: true })   Expected: visible   Error: element(s) not found (30000ms)
the wall display signs in at /wall/ on the one origin
    Locator: getByRole('heading', { name: 'Pick a layout', exact: true })   Expected: visible   Error: element(s) not found (30000ms)
  3 failed
  3 passed (2.3m)        # the three teardown projects
```

This agrees with T002's capture of the same three flows (the token request blocked by CORS). The assertion is the first screen and not the absence
of a "Sign-in failed" heading, because T002 showed the kiosk and the wall display fail onto a "Reconnecting" screen.

## T003 — the guard, red ×3 (2026-11-13), Release, realm unchanged

`WallClientDeclarationTests.A_client_names_the_origin_it_is_served_from`, now six rows (two existing, four new). The stack was stopped first: a
running stack locks the service DLLs and the build fails with MSB3027.

| Row                       | Result                                                                         |
| ------------------------- | ------------------------------------------------------------------------------ |
| `kiosk-web` × `5174`      | passed (existing)                                                              |
| `kiosk-wall` × `5175`     | passed (existing)                                                              |
| `management-web` × `5173` | **passed** (new; the declared characterisation row: it closes a coverage hole) |
| `management-web` × `5180` | **failed**                                                                     |
| `kiosk-web` × `5180`      | **failed**                                                                     |
| `kiosk-wall` × `5180`     | **failed**                                                                     |

```text
'management-web' is served on port 5180; with webOrigins '+' its redirect URIs are the only thing that makes that an allowed origin,
and without it the token exchange is blocked by CORS
```

(the same message for `kiosk-web` and `kiosk-wall`, naming each). This is the SC-003 demonstration: a client that does not name the one origin
fails a check, naming it, for 3 of 3 clients.

## T007 — at the fix, on a fresh stack (2026-11-13): green, and a plan correction

**Setup.** Fresh stack (`PersistentStack=false`, so a fresh Keycloak volume importing the realm file with the three new entries): Release,
`wait-for-e2e-stack.sh` reported "every composed resource started after ~95s". The live Keycloak, read through the admin API, listed
`http://localhost:5180/*` on all three clients: the fresh import carried the fix.

**First run: the console passed, the kiosk and the wall display did not, and the reason was my assertion, not a second defect.** The spec as
first written asserted the "Pick a layout" heading. A probe showed both apps had signed in and loaded (no CORS error; back at `/kiosk/` and
`/wall/`) and said **"No layouts published yet. Ask your administrator to publish one."**: the picker renders its heading only when a layout is
published (`PickerPage.tsx:91`; the empty state is `:83`), and the `seed` project that publishes one runs for the `kiosk` and `wall` projects,
not for `chromium`. **Plan decision 5 ("assert the heading only, no seed needed") was wrong** and is corrected: the spec now accepts the picker
in either rendering (the heading, or "No layouts published yet."). Neither is what a failed app shows (T002: "Reconnecting").

**The corrected spec's counterfactual, on this same stack, through the admin API** (the way ADR-0131 says to change a live realm; a throwaway stack,
the realm file untouched): the `:5180` entry was removed from the three clients, leaving exactly the pre-fix lists, and the spec run:

```text
management-web  remove   ["http://localhost:*","http://localhost:5173/*"]
kiosk-web       remove   ["http://localhost:*","http://localhost:5174/*"]
kiosk-wall      remove   ["http://localhost:*","http://localhost:5175/*"]

Locator: getByRole('heading', { name: 'Cameras', exact: true })                                             Expected: visible   (console)
Locator: getByRole('heading', { name: 'Pick a layout', exact: true }).or(getByText('No layouts published yet.'))  Expected: visible   (kiosk)
Locator: getByRole('heading', { name: 'Pick a layout', exact: true }).or(getByText('No layouts published yet.'))  Expected: visible   (wall display)
  3 failed
  3 passed (2.3m)        # the three teardown projects
```

So the corrected assertion still fails for the defect. The entries were then restored through the same API, and the spec re-run:

```text
management-web  restore  ["http://localhost:*","http://localhost:5173/*","http://localhost:5180/*"]
kiosk-web       restore  ["http://localhost:*","http://localhost:5174/*","http://localhost:5180/*"]
kiosk-wall      restore  ["http://localhost:*","http://localhost:5175/*","http://localhost:5180/*"]
  6 passed (29.9s)        # the three tests and the three teardown projects
```

**SC-001 observed: 3 of 3 apps sign in at the one origin** (0 of 3 in T002).

**The counterfactual (FR-008, SC-004), after**: a token request with `client_id=management-web` and a bogus code (only the header matters):

| `Origin`                              | Before the fix                               | After the fix                                                          |
| ------------------------------------- | -------------------------------------------- | ---------------------------------------------------------------------- |
| `http://localhost:5173`               | `400`, `Access-Control-Allow-Origin: …:5173` | `400`, `Access-Control-Allow-Origin: http://localhost:5173`            |
| `http://localhost:5180`               | `403`, no allow-origin                       | `400`, `Access-Control-Allow-Origin: http://localhost:5180`            |
| `http://localhost:5199` (not listed)  | `403`, no allow-origin                       | `403`, **no allow-origin: still blocked**                              |
| `:5180` as `kiosk-web` / `kiosk-wall` | not run                                      | `400` with `Access-Control-Allow-Origin: http://localhost:5180` (both) |

**One thing seen that is not part of this change:** the kiosk and the wall display log a font-preload warning whose URL has the base **twice**,
`http://localhost:5180/kiosk/kiosk/@fs/…/IBMPlexSans-*.woff2` (and `/wall/wall/…`): "preloaded … but not used". Cosmetic, dev-proxy only,
from spec 494's font-preload work; noted here and not fixed.

## T008 — the fixed ports, one unmodified spec per project (2026-11-13), at the fix

One worker, one project at a time, on the stack above. **This is three specs, not the suite; CI runs the rest.**

| Project (port)       | Spec, unmodified                        | Result                                                          |
| -------------------- | --------------------------------------- | --------------------------------------------------------------- |
| `chromium` (`:5173`) | `e2e/cameras.spec.ts`                   | **6 passed** (the spec's tests and the three teardown projects) |
| `kiosk` (`:5174`)    | `e2e/kiosk-shows-a-wall.spec.ts`        | **7 passed** (the seeds, the spec, the teardowns) in 7.9 min    |
| `wall` (`:5175`)     | `e2e/wall-outlives-its-session.spec.ts` | **not discharged** (below)                                      |

**The wall project is not observed locally, and I am not claiming it.**

- Run 1: `9 passed, 1 failed`, the failure `worker process exited unexpectedly (code=143)`. 143 is SIGTERM and the run lasted 9.5 minutes against a
  580-second `timeout` I had put around it, so my own cap is the likely cause. That is an inference; I did not capture which test was running.
- Run 2 (a longer cap): `4 failed, 2 passed (45.4s)`, 4 did not run. The failures are `browserContext.newPage: Target crashed` in the seed
  setup and then `worker process exited unexpectedly (code=3221225794)` (`0xC0000142`, a process that cannot initialise for lack of resources)
  in the three teardown projects: **memory exhaustion**, not an assertion. The harness then stopped the background run for critical memory.
- I did not run it a third time. The stack was stopped afterwards (AppHost, services, containers, Playwright browsers: none left; free memory
  from ~1.8 GB to 6.4 GB).

What stands in its place, and is weaker than observing it: the realm change adds one entry to `kiosk-wall`'s `redirectUris` and touches nothing
else of that client, so its own-port entry (`http://localhost:5175/*`) and the `offline_access` configuration are unchanged; `WallClientDeclarationTests`
passes (36 of 36 in the realm-related architecture tests); and `kiosk-wall` at the one origin signs in (the spec's third test). CI runs the whole
`wall` project at `:5175` on a fresh stack.
