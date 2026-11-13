# Quickstart: observing the console's live update

For the verification note (phase 5). Run it **twice**: before the change, to record the failure (FR-001), and after.

1. Stop any running stack, then start one: `dotnet run --project src/AppHost` (one machine, one stack).
2. Sign in to the console at `http://localhost:5180/` (the one origin), open **Walls**, and open any wall's detail page. Repeat at
   `http://localhost:5173/`.
3. In the browser's network tab, find the request to `/hubs/layouts/negotiate`.
   - **Before:** it goes to the page's own origin and answers 404 or the shell's HTML; no WebSocket opens.
   - **After:** it goes to `layout-composition`'s address, answers 200, and a WebSocket opens.
4. Switch the wall's scene from elsewhere (a second tab, or `POST /api/layout-composition/walls/{id}/switch`, which takes the wall's `If-Match` version). The page re-reads the wall
   within 2 seconds (SC-001) without a reload.
5. Counterfactual (SC-004): from a page at an origin that is not listed, repeat the preflight against `layout-composition`'s hub path; it
   gets no `Access-Control-Allow-Origin`. `LayoutHubCorsTests` already pins this; observe it once on the live service.
6. Open `/kiosk/` and `/wall/` and confirm they connect as before (FR-006).

The access token is a bearer credential: do not paste the negotiate URL's query string into the PR.
