# Contract: gateway paths and the `Ui` configuration

## Path map (fixed by ADR-0192)

| Path                          | Served by                                                                           | Mode `Proxy` (dev)                                       | Mode `Static` (built bundles)                   |
| ----------------------------- | ----------------------------------------------------------------------------------- | -------------------------------------------------------- | ----------------------------------------------- |
| `/api/<context>/**`           | the context's service, `/api/<context>` removed                                     | same                                                     | same                                            |
| `/<context>/**` (bare)        | the context's service, `/<context>` removed, **non-navigations only**               | same                                                     | same                                            |
| `/remotes/cameras/**`         | the cameras remote                                                                  | Vite dev server (5176), base `/remotes/cameras/`         | the cameras `dist` directory                    |
| `/remotes/event-ingestion/**` | the event-ingestion remote                                                          | Vite dev server (5177), base `/remotes/event-ingestion/` | its `dist` directory                            |
| everything else, lowest order | the shell                                                                           | Vite dev server (5173), base `/`                         | shell `dist`; unknown path → shell `index.html` |
| `/kiosk/**`, `/wall/**`       | served from spec 494: `specs/494-kiosk-wall-single-origin/contracts/kiosk-paths.md` |                                                          |                                                 |

`<context>` is one of the nine: `camera-catalog`, `stream-distribution`, `layout-composition`, `event-ingestion`, `overlay-designer`,
`system-variables`, `audit-observability`, `automation`, `identity`.

## Rules

1. **Carve-out twin.** `POST /stream-distribution/streams/authorize` and `POST /api/stream-distribution/streams/authorize` are both a
   hard 404 at the gateway (a literal beats the catch-all).
2. **Navigation rule (Q1, option A).** **When the UI is hosted** (`Ui:Mode` set), each bare route also requires the request header `Sec-Fetch-Dest` not to
   contain `document` (a YARP config filter adds it; it is not written in `appsettings.json`). A page navigation therefore matches no
   bare route and reaches the shell. The `/api/...` routes and the UI routes have **no** such condition. **With no `Ui` section the
   rule is absent** and a bare route answers a browser exactly as it always has (there is no page to fall through to).
3. **Policies.** Every `/api/<context>` route carries the same `CorsPolicy` and `RateLimiterPolicy` as its bare twin, and the same
   transforms except the removed prefix; `X-Fab` is removed as today; `Authorization` is forwarded unchanged.
4. **UI routes and static files** carry no `RateLimiterPolicy` and are not subject to `per-source`.
5. **Unknown `/api/...` path** (no such context) is a gateway 404; it is **not** answered by the shell fallback.
6. **UI hosting is optional.** With no `Ui` section the gateway serves no UI route and an unrouted path is a 404 (as today).

## `Ui` configuration

```json
"Ui": {
  "Mode": "Proxy | Static",
  "Static": {
    "ShellRoot": "<directory of the shell build>",
    "CamerasRoot": "<directory of the cameras remote build>",
    "EventIngestionRoot": "<directory of the event-ingestion remote build>"
  }
}
```

- `Proxy`: destinations come from the three Vite resources by service discovery (`http://management-web`, `http://management-cameras`,
  `http://management-event-ingestion`); the AppHost sets it in run mode only. `Static` is selected by configuration and is not wired in
  the AppHost in this phase (no production deployment, ADR-0192 decision 10).
- `Static` fails fast at start if a root directory is missing or has no `index.html` / `remoteEntry.js`.

## Frontend environment contract (changes to `docs/deployment-frontend-env.md`)

| Variable                                   | One-origin value             | Guard                                                   |
| ------------------------------------------ | ---------------------------- | ------------------------------------------------------- |
| `VITE_API_GATEWAY_URL`                     | `/api`                       | still required in production builds; unset still throws |
| `VITE_CAMERAS_REMOTE_URL`                  | `/remotes/cameras`           | still required; unset still throws                      |
| `VITE_EVENT_INGESTION_REMOTE_URL`          | `/remotes/event-ingestion`   | still required; unset still throws                      |
| `VITE_KEYCLOAK_URL`, `VITE_LAYOUT_HUB_URL` | unchanged (absolute, direct) | unchanged                                               |

## Not in this contract

CORS allowed origins (unchanged, FR-013), Content-Security-Policy (deferred, plan D9), TLS and Ingress
(phase 4).
