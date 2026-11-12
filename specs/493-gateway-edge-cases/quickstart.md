# Quickstart: observing spec 493's fixes

No Aspire and no Docker are needed or wanted for any step here (one machine, low memory). Run from the repository root in Git Bash.

## 1. Tests (phases 4 and 5)

```sh
# US1: dev proxy
pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/app/devProxy.test.ts

# US2, US3, US4: in-process gateway tests, no containers
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~ApiGateway.GatewayUiStaticTests|FullyQualifiedName~ApiGateway.GatewayApiPrefixTests|FullyQualifiedName~ApiGateway.GatewayHostCharacterisationTests|FullyQualifiedName~ApiGateway.GatewayUiProxyTests|FullyQualifiedName~ApiGateway.GatewayNavigationRuleTests"
```

Before each fix the new tests fail as the plan's *Test colour per story* table says; quote the failure in the PR. After, all pass, and
spec 491's C# gateway tests and spec 208's carve-out tests are green without an edit.

## 2. US1 on a real Vite dev server (the unit test mirrors Vite's matcher; this checks the mirror)

The shell's dev server pins port 5173 (`strictPort`). If 5173 is taken (a running stack), skip this step and say so in the verification
note rather than stopping anything; stopping a shared stack needs the user.

1. Start a stub target that echoes the path it received, on a free port, e.g. 5899:

   ```sh
   node -e "require('http').createServer((q,s)=>{s.end('stub '+q.url)}).listen(5899)" &
   ```

2. Start the shell dev server proxying to it:

   ```sh
   VITE_GATEWAY_PROXY_TARGET=http://localhost:5899 pnpm --filter @smart-sentinel-eye/management-web dev
   ```

3. Observe:

   | Request | Expected after the fix |
   |---|---|
   | `curl -s localhost:5173/audit-observability?limit=5` | `stub /audit-observability?limit=5` |
   | `curl -s localhost:5173/audit-observability/entries?limit=5` | `stub /audit-observability/entries?limit=5` |
   | `curl -s -H "Sec-Fetch-Dest: document" localhost:5173/system-variables?tab=x` | the shell's HTML, not `stub …` |
   | `curl -s localhost:5173/api/system-variables` | `stub /api/system-variables` |
   | `curl -s localhost:5173/apiary` | the shell's HTML (Vite's SPA fallback), not `stub …` |

   Before the fix, row 1 returns the shell's HTML and row 5 returns `stub /apiary`. Record both runs.

4. Stop the dev server and the stub.

## 3. US2-US4 against a running gateway (optional)

The in-process `FixtureLogic` tests are the observation for the gateway: they start the real gateway host, in Static mode, against
fixture roots. A manual check against the full stack would need Aspire and is **not** part of this spec's verification.
