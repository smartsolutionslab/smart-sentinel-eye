# Contract: how the console learns the hub's address

There is no new HTTP surface. The contract is two configuration keys and the rule that joins them.

| Key                                 | Owner / reader                                                    | Value                                                | Required                                                                             |
| ----------------------------------- | ----------------------------------------------------------------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `VITE_LAYOUT_HUB_URL`               | AppHost sets it on `management-web`; `hubUrl.ts` reads it         | `<layout-composition http endpoint>/hubs/layouts`    | Dev: set by AppHost. Production build: **required** (`hubUrl.ts` throws without it). |
| `LayoutHub:Cors:AllowedOrigins:<n>` | AppHost sets it on `layout-composition`; `LayoutHubCors` reads it | One exact origin per entry; `<n>` carries no meaning | Unset or empty admits none; a wildcard or a non-origin string refuses to start.      |

**The joining rule**: every origin a page can be loaded from and that opens the hub MUST appear in the list. After this feature those are
`kiosk-web`, `kiosk-wall`, `api-gateway` (the one origin) and `management-web` (the console's own port).

**What does not change**: the hub path (`LayoutLifecycleHub.Path`), the policy's headers and methods, the credential handling, and the
gateway, which carries no hub route and whose assembly may not reference SignalR, WebSockets or Http.Connections.
