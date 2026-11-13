# Quickstart: observing sign-in at the one origin

For the verification note (phase 5). Run it **twice** on a **fresh** Keycloak volume: before the change, to record the failure (FR-001), then
after it. The realm is imported only into an empty volume (ADR-0131), so `PersistentStack=false` is required.

1. Stop any running stack, then boot a fresh one:
   `dotnet run --project src/AppHost -c Release --no-build -- ScenarioSimulator=false PersistentStack=false`
   and wait with `scripts/wait-for-e2e-stack.sh`. One machine, one stack; stop it before building.
2. For each of `http://localhost:5180/`, `http://localhost:5180/kiosk/` and `http://localhost:5180/wall/`: press _Sign in_, authenticate
   (`operator` for the first two, `wall-munich` for the wall display), and note what the app shows.
   - **Before:** "Sign-in failed — Failed to fetch"; the browser console shows the `POST …/protocol/openid-connect/token` blocked by CORS with
     no `Access-Control-Allow-Origin`. `e2e/one-origin-sign-in.spec.ts` is red three times.
   - **After:** the console shows _Cameras_; the kiosk and the wall display show _Pick a layout_. The spec is green.
3. The fixed ports are unchanged: sign in at `http://localhost:5173/`, `:5174/`, `:5175/` as before, or run the existing `chromium`, `kiosk` and
   `wall` projects unmodified.
4. Counterfactual (FR-008): from the shell, send the token request with an origin that is listed and one that is not, and compare the headers:

   ```sh
   curl -sk -D - -o /dev/null -X POST "https://localhost:<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token" \
     -H "Origin: http://localhost:5180" \
     -d "grant_type=authorization_code&client_id=management-web&code=x&redirect_uri=http://localhost:5180/"
   ```

   With the change, `Origin: http://localhost:5180` gets `Access-Control-Allow-Origin: http://localhost:5180`; `Origin: http://localhost:5199`
   gets none. (The `400 invalid_grant` body is expected: the code is bogus; only the header matters.)

5. A persistent stack: record which you tested, a dropped `keycloak-data` volume or the admin API, per ADR-0131.

Never paste an authorization code, a token or the callback URL's query string into the PR.
