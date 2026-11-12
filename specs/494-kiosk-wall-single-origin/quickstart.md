# Quickstart: seeing the kiosk and the wall on one origin

Spec: [spec.md](./spec.md). Plan: [plan.md](./plan.md). Paths: [contracts/kiosk-paths.md](./contracts/kiosk-paths.md).

## What was already proven at plan time (no stack)

The two spikes in [research.md](./research.md) R1 and R2 ran against the installed Vite 8.3.6 on free ports 5190/5191, with a fake hub
upstream. They decided:

| Claim                                                                          | Result                                                                                                                               |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| A port's root redirects to a non-root base                                     | `GET /` → `302 Location: /kiosk/`                                                                                                    |
| Un-prefixed deep links on the port                                             | 404 with Vite's "did you mean" hint (no e2e spec uses one)                                                                           |
| HTML entry, favicon and scripts are rewritten under the base                   | yes                                                                                                                                  |
| A plugin-injected `/@fs/` preload is rewritten                                 | **no** → plan D7                                                                                                                     |
| `/kiosk` without slash                                                         | 404 → plan D6 (the gateway redirects)                                                                                                |
| The `/hubs` proxy still works with a base                                      | yes (proxy runs before the base middleware)                                                                                          |
| A page at `http://localhost:5180` may call the instance's `/hubs` cross-origin | preflight 204 with `Access-Control-Allow-Origin: http://localhost:5180`; `Authorization` reaches the upstream; WebSocket upgrade 101 |
| A non-localhost origin may                                                     | no `Access-Control-Allow-Origin`: refused (dev-only bound)                                                                           |

The last two rows belong to the first hub proposal, which the owner replaced at the Plan gate with a CORS policy on `layout-composition`
(plan D3). They remain as evidence of what a SignalR browser client sends cross-origin; the replacement is proven by W5's in-process test
and, end to end, by CI's `kiosk`/`wall` projects.

To repeat it: the spike script was a throwaway (not committed): `createServer` from the installed Vite with `configFile: false`,
`base: '/kiosk/'`, the kiosk's `/hubs` proxy options aimed at a plain Node HTTP server that echoes method, URL, `Origin` and `Authorization`
and answers a WebSocket upgrade with 101, and a plugin that injects a `/@fs/` preload link. Pass the base with `MSYS_NO_PATHCONV=1` in Git
Bash, which otherwise rewrites `/kiosk/` into a Windows path. Then the `curl` calls listed in R1/R2.

## Dev, once implemented (needs the Aspire stack)

1. Start the AppHost in run mode. The gateway is at `http://localhost:5180` with `Ui__Mode=Proxy` and the kiosk and wall destinations.
2. Open `http://localhost:5180/kiosk/`. Sign in as a kiosk account. Expect the sign-in request to name client `kiosk-web`, scope `openid`,
   `redirect_uri=http://localhost:5180/kiosk/oidc/callback`, and the browser to come back to `/kiosk/`.
   - **If the token request fails with a CORS error**, that is plan D8's case: record it verbatim, try the console at `:5180/` too, and file
     a separate issue against spec 491 and all apps on the one origin. The realm is not changed in this spec.
3. Open a layout with a live camera and a bound overlay; change the variable from the console several times in quick succession. In the
   network panel: pages, scripts and REST on `:5180`; `negotiate` (with its one `OPTIONS` preflight) and the hub WebSocket on
   **`layout-composition`'s own address**; **no further `OPTIONS` while the labels change** (plan W12); WHEP on the media server; Keycloak on
   its own port. The preflight response carries `Access-Control-Allow-Origin: http://localhost:5180`, `Access-Control-Allow-Credentials:
true` and `Access-Control-Max-Age: 600`.
4. Read the gateway's request log (Aspire dashboard, `api-gateway` traces) for the run: no `/hubs/` and no WHEP path.
5. Reload `/kiosk/layouts/<id>`; open it in a new tab; open `/kiosk` (no slash): each reaches the kiosk.
6. Open `http://localhost:5180/wall/` in a separate profile, sign in as a wall account: client `kiosk-wall`, scope `openid offline_access`,
   return to `/wall/`. Open a wall.
7. Open `http://localhost:5174/` and `http://localhost:5175/`: each redirects to its base and signs in as before.
8. Edit a kiosk component with `/kiosk/` open: it updates without a reload. Same on `/wall/`.

## Production bundles, locally

```sh
# from apps/kiosk-web, two builds of the same app
VITE_API_GATEWAY_URL=http://localhost:5180 VITE_KEYCLOAK_URL=http://localhost:8080 \
  VITE_LAYOUT_HUB_URL=http://localhost:<layout-composition>/hubs/layouts \
  pnpm vite build --outDir <scratch>/kiosk-dist
VITE_KIOSK_MODE=wall VITE_API_GATEWAY_URL=… VITE_KEYCLOAK_URL=… VITE_LAYOUT_HUB_URL=… \
  pnpm vite build --outDir <scratch>/wall-dist
```

Check every asset URL in each `index.html` is under `/kiosk/` or `/wall/` respectively. Start the gateway with `Ui__Mode=Static`, spec 491's
three roots, `Ui__Static__KioskRoot` and `Ui__Static__WallRoot`, and request: `/kiosk/`, `/kiosk/layouts/x` (navigation), `/wall/walls/x`,
`/kiosk/assets/nope.js` (404), `/kiosk` (redirect). Each `index.html` response carries `Cache-Control: no-cache` and `nosniff`. Unset one
root, or point it at an empty directory: the gateway refuses to start, naming the key (owner, Q4).

## The hub's CORS answer, without the stack

`layout-composition` needs its database to start, so its policy is proven in-process (plan W5): a host with only `LayoutHubCors` and
stand-in endpoints at `/hubs/layouts/negotiate` and a REST path. With `LayoutHub:Cors:AllowedOrigins:0 = http://localhost:5180`:

- `OPTIONS /hubs/layouts/negotiate` from `Origin: http://localhost:5180`, requesting `POST` and
  `authorization,content-type,x-requested-with,x-signalr-user-agent`, with **no token** → 204 and the CORS headers above.
- the same from `Origin: http://fab.example` → no `Access-Control-Allow-Origin`.
- the same against the REST path → no CORS headers.
- `AllowedOrigins:0 = *` → the host refuses to start, naming `LayoutHub:Cors:AllowedOrigins`.

## Rate limit (SC-005)

Real gateway, production limit (100/min/source): request `/kiosk/`, `/wall/` and their assets 300 times from one source; zero 429. Then
exceed the limit with API calls; they are refused as today (spec 491 T024's method, including its two false starts).
