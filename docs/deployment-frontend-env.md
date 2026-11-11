# Frontend deployment environment contract

Spec 011 (`specs/011-frontend-247-resilience/contracts/resilience-interfaces.md` §6).

Both SPAs (`management-web`, `kiosk-web`) are static Vite builds. Every
runtime origin they need is injected at build time via `VITE_*`
variables. **Production builds fail loudly at module load when a
required variable is missing** — a deliberate guard (FR-010) against
bundles that silently fall back to `localhost` or same-origin and
malfunction only at runtime on the fab floor.

| Variable                          | Consumed by                                         | Dev default                        | Production                                                                                                    |
| --------------------------------- | --------------------------------------------------- | ---------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `VITE_API_GATEWAY_URL`            | `apps/shared/src/api/gateway.ts`                    | same-origin (unit tests, previews) | **required** — the API base: `/api` for `management-web` on the one origin (below); the public gateway origin for `kiosk-web` |
| `VITE_KEYCLOAK_URL`               | `apps/*/src/app/auth.ts`                            | `http://localhost:8080`            | **required** — Keycloak origin; MUST match the issuer the services validate                                   |
| `VITE_LAYOUT_HUB_URL`             | `apps/shared/src/realtime/hubUrl.ts`                | `/hubs/layouts` (Vite dev proxy)   | **required** — absolute LayoutLifecycle hub URL; the dev-only Vite `/hubs` proxy does not exist in production |
| `VITE_CAMERAS_REMOTE_URL`         | `apps/management-web/src/app/navigation/remotes.ts` | `/remotes/cameras` (Aspire)        | **required** — where the cameras remote is served (spec 316); on the one origin, the relative path `/remotes/cameras` |
| `VITE_EVENT_INGESTION_REMOTE_URL` | `apps/management-web/src/app/navigation/remotes.ts` | `/remotes/event-ingestion` (Aspire) | **required** — where the event-ingestion remote is served (issue #2780); on the one origin, `/remotes/event-ingestion` |

"Production" means `import.meta.env.PROD === true`, i.e. any `vite build`.

## Dev (Aspire)

Aspire's AppHost injects `VITE_API_GATEWAY_URL`, `VITE_KEYCLOAK_URL`
and both remote URLs into `management-web`'s Vite dev server; the
SignalR hub is reached through each app's Vite `/hubs` proxy, so
`VITE_LAYOUT_HUB_URL` stays unset. No changes needed for local
development.

## Prod (k3s / Helm)

The deploy layer must supply every required variable to the SPA build
step it applies to (`management-web` needs all five; `kiosk-web` needs
only the first three; a remote app's own build needs none of them — it
serves no API client and reaches no hub), or the Ingress must serve
`/hubs` on the app origin, in which case `VITE_LAYOUT_HUB_URL=/hubs/layouts`
is still set explicitly. Verify a candidate bundle by loading it once:
a missing variable throws `VITE_… must be set in production builds` in
the console on first paint.

## One origin (spec 491, ADR-0192)

The operator console is reached at **one origin, the API gateway's**. The shell is `/`, the two federated remotes are
`/remotes/cameras/` and `/remotes/event-ingestion/`, and REST is `/api/<context>/...` (the `/api/<context>` prefix is removed before
the request reaches the service). The kiosk and the wall are not served here yet (`/kiosk/` and `/wall/` are reserved, #3246), and
Keycloak, the SignalR hub and WebRTC media stay direct. The full path map and the gateway configuration are in
`specs/491-gateway-single-origin-shell/contracts/gateway-paths.md`.

- **The three variables above keep their guard**: unset in a production build still throws, naming the variable. Their one-origin
  values are `/api`, `/remotes/cameras` and `/remotes/event-ingestion`: explicit relative paths, not an absence.
- **Each remote is built with a Vite `base` equal to its prefix** (a constant in its `vite.config.ts`), so its entry file and every
  chunk resolve under `/remotes/<name>/`. A remote's own dev port therefore serves `/remotes/<name>/remoteEntry.js`, not
  `/remoteEntry.js`.
- **Dev:** run mode gives the gateway the fixed address `http://localhost:5180` and `Ui__Mode=Proxy`, so it proxies the three Vite dev
  servers, hot reload included. Opened directly at `:5173`, the shell's Vite dev server proxies `/api` and `/remotes/*` the same way
  (the gateway origin reaches it as `VITE_GATEWAY_PROXY_TARGET`, a dev-only alias; unset, there is no proxy).
- **Production bundles:** start the gateway with `Ui__Mode=Static` and `Ui__Static__ShellRoot`, `Ui__Static__CamerasRoot` and
  `Ui__Static__EventIngestionRoot` pointing at the three `vite build` output directories. It refuses to start if a root is missing or
  lacks its entry file (`index.html`, `remoteEntry.js`). Static files are served before the rate limiter and carry
  `X-Content-Type-Options: nosniff`. With no `Ui__Mode` the gateway serves no page, as before.
- **A browser reload of `/system-variables` or `/event-ingestion/...`** shows the shell, not the REST route of the same name: when
  the UI is hosted, the bare REST routes answer only requests that are not page navigations (`Sec-Fetch-Dest` is not `document`).
