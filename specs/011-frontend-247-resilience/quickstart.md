# Quickstart: verifying Frontend 24/7 Resilience

How to induce each failure class against the local Aspire stack and
observe self-healing. This doubles as the Phase-5 verification protocol;
each step names the success criteria it demonstrates.

## Prerequisites

```powershell
# full stack (Postgres, Keycloak, RabbitMQ, MediaMTX, services, SPAs)
aspire run   # from src/AppHost, or run the AppHost from the IDE
```

- Kiosk: http://localhost:5174 — sign in, open a published multi-tile layout.
- Management: http://localhost:5173.
- Keep the browser devtools console open: every resilience transition
  logs with the `[resilience]` prefix (FR-017) — the assertions below
  reference those lines.
- Known local gotchas: stale `postgres-data`/`keycloak-data` volumes
  (drop them if MigrationRunner or realm scopes misbehave); stop the
  AppHost gracefully (orphaned `dcp.exe` otherwise).

## 1. Stream recovery (US1 → SC-001, SC-002)

1. With tiles live, stop the `mediamtx` resource from the Aspire
   dashboard.
2. **Expect ≤ 10 s**: every tile leaves "Live" → "Reconnecting…";
   console shows `[resilience] stream live→reconnecting` per tile.
   *A frozen frame labeled Live at this point is a FAIL (SC-002).*
3. Wait ≥ 2 min (prove retries don't give up), then restart `mediamtx`.
4. **Expect ≤ 60 s**: all tiles back to moving video, no reload
   (SC-001). Retry attempts in the console should be visibly jittered,
   not synchronized (SC-005 sanity).
5. Boot-order case: stop `mediamtx`, hard-reload the kiosk, then start
   `mediamtx` — tiles must connect on their own (first-attempt failures
   retry).
6. Teardown case: switch layouts and check MediaMTX (dashboard/logs) —
   WHEP sessions are DELETEd on switch, not left to time out.

## 2. Live-update resilience (US2 → SC-004)

1. Stop the LayoutComposition service (hub host) from the Aspire
   dashboard.
2. **Expect**: discreet "live updates degraded" badge appears on the
   kiosk; `[resilience] hub connected→degraded`. Leave it down ≥ 2 min —
   the badge stays, retries continue (never gives up).
3. While it is down, from management: change a system variable bound to
   a visible overlay, and archive a published overlay used by a tile.
4. Restart the service.
5. **Expect ≤ 10 s after reconnect**: badge clears; the overlay shows
   the NEW variable value; the archived overlay's tile shows "Overlay
   unavailable" (SC-004). No page reload.
6. Boot-order case: stop the service, reload the kiosk (badge shown from
   the start), restart the service — connection establishes without a
   reload.
7. Pre-mount case: with the kiosk closed, archive an overlay; open the
   kiosk → the affected tile shows "Overlay unavailable" immediately
   (FR-009).

## 3. Session survival (US3 → SC-003 leg)

Use a short-lifetime test realm so this is minutes, not 10 hours:

1. Keycloak admin (Aspire dashboard → keycloak endpoint, realm
   `smart-sentinel-eye`): set *SSO Session Max* ≈ 3 min and *Access
   Token Lifespan* ≈ 1 min. (Realm edits: remember the stale
   `keycloak-data` volume gotcha if changes don't stick.)
2. Watch a kiosk layout across the 1-min token expiry: **expect** no
   visual interruption; `[resilience] session renewing→authenticated`
   (FR-011).
3. Wait out SSO Session Max: **expect** the kiosk to auto-redirect and
   come back to the SAME layout (non-interactive re-signin while the
   Keycloak cookie allows), or — when interaction is required — the
   dedicated full-screen session-expired state. It must never show the
   plain sign-in-button screen under a torn-down wall, and must never
   redirect-loop.
4. Management: let the session lapse, click any row action → **expect**
   one silent renew attempt then an explicit re-sign-in prompt; no
   silent no-op, no generic "Could not load" banner storm (FR-012/014).
5. 401-mid-flight: with devtools, block the network for one request so
   it 401s → the request is retried once after renewal, transparently.

## 4. Crash containment (US4 → SC-006)

Dev-only crash trigger (guarded so it cannot ship active): append
`?crash=render` on a kiosk route in a dev build to throw during render.

1. Management + `?crash=render`: **expect** a bounded error panel with a
   working "Try again"; the shell (nav) stays alive.
2. Kiosk + `?crash=render` (the boundary strips the param before
   reload): **expect** auto-reload ≤ 30 s back to the same layout
   (SC-006); `[resilience] crash reload scheduled`.
3. Crash-loop: keep the param via a forced re-throw (test hook) —
   reload delays must step 5 → 15 → 60 s, not hot-loop.

## 5. Automated checks

```powershell
pnpm typecheck ; pnpm lint ; pnpm test          # unit/component suites
pnpm test:e2e                                    # incl. new kiosk project:
                                                 # hub-blocked → badge shown → unblocked → badge clears
```

## 6. The 72-hour criterion (SC-003)

Full SC-003 (72 h unattended with induced restarts) is a pilot-rig
soak, not a dev-loop check. The dev-loop proxy is: run steps 1–3 in one
session without ever reloading manually — the kiosk must end fully
live, current, and authenticated.
