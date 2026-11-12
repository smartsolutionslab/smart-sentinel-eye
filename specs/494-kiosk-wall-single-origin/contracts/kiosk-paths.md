# Contract: the kiosk and wall paths and their `Ui` configuration

Extends `specs/491-gateway-single-origin-shell/contracts/gateway-paths.md`; replaces its row "`/kiosk/**`, `/wall/**` — reserved, not served
in this phase". Every other row and rule there is unchanged.

## Path map additions

| Path        | Served by               | Mode `Proxy` (dev)                                                 | Mode `Static` (built bundles)                                                                |
| ----------- | ----------------------- | ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------- |
| `/kiosk`    | gateway                 | redirect to `/kiosk/`                                              | redirect to `/kiosk/`                                                                        |
| `/kiosk/**` | the kiosk (`kiosk-web`) | `kiosk-web` Vite dev server (5174), base `/kiosk/`, unchanged path | `Ui:Static:KioskRoot`; unclaimed path → the kiosk's `index.html` by the shared fallback rule |
| `/wall`     | gateway                 | redirect to `/wall/`                                               | redirect to `/wall/`                                                                         |
| `/wall/**`  | the wall (`kiosk-wall`) | `kiosk-wall` Vite dev server (5175), base `/wall/`                 | `Ui:Static:WallRoot`; unclaimed path → the wall's `index.html`                               |

## Rules

1. **Precedence.** `/kiosk/**` and `/wall/**` win over the shell's catch-all in both modes. A path under either prefix is never answered by
   the shell.
2. **Both instances are required** (owner, Q4). With `Ui:Mode=Proxy`, a missing `Ui:Proxy:Kiosk` or `Ui:Proxy:Wall` stops the gateway at
   start, naming the key; with `Ui:Mode=Static`, a missing `Ui:Static:KioskRoot` or `Ui:Static:WallRoot` does the same. There is no
   running state with an unconfigured prefix.
3. **Policies.** The four routes carry no `RateLimiterPolicy` and no `CorsPolicy`. Static files are served before the limiter.
4. **Fallback (Static).** The rule spec 493 sets for the shell, applied per prefix with that instance's `index.html`: a page navigation or an
   extension-less path gets `index.html`; a non-navigation request for a path with an extension that no file claims is a 404. Every response
   serving an instance's `index.html` carries `Cache-Control: no-cache` and `X-Content-Type-Options: nosniff`; every static file keeps
   `nosniff`.
5. **Fail fast.** A `KioskRoot`/`WallRoot` that is not a directory or has no `index.html` stops the gateway at start, naming the key.
6. **Not on the gateway:** `/hubs/**` (the hub is reached directly on `layout-composition`, below), WHEP (the server-provided `whepUrl`),
   Keycloak.

## `Ui` configuration additions

```json
"Ui": {
  "Proxy": { "Kiosk": "<kiosk-web dev server>", "Wall": "<kiosk-wall dev server>" },
  "Static": { "KioskRoot": "<kiosk build, base /kiosk/>", "WallRoot": "<wall build, base /wall/>" }
}
```

The two keys of the selected mode are required, like spec 491's three. The AppHost sets the two `Proxy` keys in run mode, beside spec 491's
three, in the same place it sets `Ui__Mode`.

## The hub's CORS answer (`layout-composition`)

| Item                    | Value                                                                                                                                   |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Setting                 | `LayoutHub:Cors:AllowedOrigins` (array of origins). Absent or empty: no origin admitted (today's behaviour).                            |
| Refused at start        | any entry containing `*` (credentialed CORS cannot name `*`; production must list origins)                                              |
| Scope                   | requests whose path starts with `/hubs/layouts` only; REST paths get no CORS answer                                                     |
| Pipeline position       | before authentication, so a token-less preflight is answered by CORS and not refused by the hub's `[Authorize]`                         |
| Allowed origin          | echoed in `Access-Control-Allow-Origin`, with `Access-Control-Allow-Credentials: true`                                                  |
| Allowed request headers | `Authorization`, `X-Requested-With`, `X-SignalR-User-Agent`, `Content-Type`                                                             |
| Allowed methods         | `GET`, `POST`, `DELETE`                                                                                                                 |
| Preflight max-age       | 10 minutes                                                                                                                              |
| Token on the WebSocket  | `access_token` query value, already accepted on the hub path by `Program.cs`'s `OnMessageReceived` hook (unchanged)                     |
| Dev values (AppHost)    | the `kiosk-web`, `kiosk-wall` and `api-gateway` endpoints (the fixed kiosk ports and the one origin), set where the kiosks are declared |
| Production              | set by the deploy layer to the public origin(s) the kiosk pages are served from                                                         |

## Kiosk environment

| Variable                 | `kiosk-web` (dev)                                                                             | `kiosk-wall` (dev) | Production build                                                           |
| ------------------------ | --------------------------------------------------------------------------------------------- | ------------------ | -------------------------------------------------------------------------- |
| `VITE_KIOSK_MODE`        | unset                                                                                         | `wall`             | unset for the kiosk build, `wall` for the wall build; **decides the base** |
| `VITE_API_GATEWAY_URL`   | the gateway's endpoint (unchanged)                                                            | unchanged          | required, the gateway's public origin (unchanged, Q1 option A)             |
| `VITE_LAYOUT_HUB_URL`    | **new**: `<layout-composition endpoint>/hubs/layouts`                                         | **new**: the same  | required, absolute (unchanged)                                             |
| `VITE_LAYOUT_HUB_ORIGIN` | unchanged; the `/hubs` proxy it builds is now only a fallback for Vite started outside Aspire | unchanged          | not used                                                                   |
| `VITE_KEYCLOAK_URL`      | unchanged                                                                                     | unchanged          | required (unchanged)                                                       |

## Application contract

- Base: `import.meta.env.BASE_URL` (`/kiosk/` or `/wall/`); never re-derived in application code.
- Router basename: the base without its trailing slash.
- OIDC `redirect_uri`: `window.location.origin` + base + `oidc/callback`.
- Sign-in return: the stored `returnTo` only when it is a path that starts with the base (`/kiosk/...`); otherwise the base. Never `/`,
  never `//...`, never another origin.
