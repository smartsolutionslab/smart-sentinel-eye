# Quickstart: seeing the one-origin console work

Stop any other Aspire stack first (one machine, one stack). Stop the stack before building.

## 0. The spike (do this first; it can stop the work)

| Id  | Do                                                                                                                                                                                                                            | Pass                                                                                                               |
| --- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| a   | Start the stack in run mode. Open the gateway's dev address. Edit a visible string in a shell component. Run `dotnet test tests/Architecture.Tests --filter "ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs"`. | The page updates without a reload; the test passes with **no edit to it**.                                         |
| b   | At the gateway address, sign in as an entitled operator and open a cameras page, then an event-ingestion page. In the browser's network tab, filter by `remote`.                                                              | Both pages render; every remote request is under `/remotes/<name>/`; no request goes to `:5176` or `:5177`.        |
| c   | `curl -i http://<gateway>/system-variables/...` (no `Sec-Fetch-Dest`), then the same with `-H "Sec-Fetch-Dest: document"`.                                                                                                    | The first reaches the REST route (a service answer, 401 without a token); the second does not (the shell answers). |

Record the three results in `verification.md`. A failure of (a) stops the work and amends ADR-0192.

## 1. The operator flow (User Story 1)

1. Open the gateway's dev address; sign in; return to the same address.
2. Open `/cameras` and `/event-ingestion/...`; reload each; the same page returns.
3. Reload `/system-variables`: the shell page, not a REST answer (Q1).
4. In the network tab, confirm no application request leaves the one origin (the identity provider and the hub excepted).

## 2. The fixed ports (User Story 2)

1. Open `http://localhost:5173` directly; sign in; open a cameras page. It works through Vite's own proxy.
2. Edit a string in `management-cameras`; the page updates in place at both the gateway address and `:5173`.

## 3. A production build (User Story 3)

1. Build `management-web`, `management-cameras`, `management-event-ingestion` with `VITE_API_GATEWAY_URL=/api`,
   `VITE_CAMERAS_REMOTE_URL=/remotes/cameras`, `VITE_EVENT_INGESTION_REMOTE_URL=/remotes/event-ingestion`.
2. Start the gateway with `Ui__Mode=Static` and the three roots pointing at the `dist` folders; repeat section 1.
3. Rebuild one app with a required value removed: the app fails at load naming the variable.

## 4. The rate limit (User Story 4)

Run the stack with the production limit (not the run-lane 6000/min override) and load the console cold, then visit every remote page, from one
source address: zero 429 on page and asset requests. Then send more than 100 API calls in a minute: the surplus is 429.

## 5. Before the PR

`pnpm` suites in the three apps (counts unchanged), `eslint`, `tsc`, `prettier --check`, `Architecture.Tests`, the gateway integration tests,
the e2e federation spec (three URL substrings changed), and click-to-first-frame measured twice.
