#!/usr/bin/env node
// scripts/fixtures/trace-redaction/make-leaky-trace.mjs
//
// Spec 186 / #2287, `spec.md` §6.1 / §1.3. Produces `leaky-trace.zip`: a REAL
// Playwright trace, captured from a real headless Chromium run under real
// Playwright tracing, carrying planted `SSE_FAKE_*` sentinels (see
// `sentinels.mjs`) in the exact places a real trace carries real secrets —
// never a real credential.
//
// It drives a throwaway `node:http` server that mimics the four shapes this
// stack actually produces during e2e sign-in:
//   - POST /realms/sse/protocol/openid-connect/token  -> {access_token, refresh_token}
//   - GET  /api/layouts             with Authorization: Bearer <token>
//   - GET  /hubs/layout/negotiate?access_token=<token>
//   - a Keycloak-shaped login form, filled via page.locator('#password').fill(...)
//
// **Requires an installed Chromium** (`pnpm exec playwright install chromium`,
// or `pnpm test:e2e:install`). **Run by hand, not in CI** — regenerate this
// fixture only when `@playwright/test` is version-bumped, then re-run
// `scripts/scrub-playwright-artifacts.test.mjs`'s presence assertions (AS-2)
// to confirm the regenerated trace still carries every sentinel in the
// entries `specs/186-a-trace-that-keeps-its-secrets/spec.md` §1.3 names. This
// script is not part of `pnpm test:guards` and is never invoked by CI.
//
//   node scripts/fixtures/trace-redaction/make-leaky-trace.mjs [outputPath]

import { chromium } from '@playwright/test';
import { createServer } from 'node:http';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  ACCESS_TOKEN_JWT,
  CLIENT_SECRET_SENTINEL,
  PASSWORD_SENTINEL,
  REFRESH_TOKEN_SENTINEL,
} from './sentinels.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const outputPath = path.resolve(process.argv[2] ?? path.join(here, 'leaky-trace.zip'));

// A Keycloak-shaped login form. The `client-secret` field is not something a
// real Keycloak login page renders — a confidential client's secret is never
// typed by a user — but this probe needs Playwright to genuinely `fill()` it
// so the sentinel lands in `trace.trace` (the action log and its DOM
// snapshot) the same way the real form's `#password` field does, rather than
// only being asserted about the (untyped) password field.
const LOGIN_PAGE = `<!doctype html>
<html>
<body>
<form id="login-form">
  <input id="username" name="username" type="text" value="operator" />
  <input id="client-secret" name="client_secret" type="text" />
  <input id="password" name="password" type="password" />
  <button id="submit" type="submit">Sign in</button>
</form>
<script>
  document.getElementById('login-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const password = document.getElementById('password').value;
    const clientSecret = document.getElementById('client-secret').value;

    const tokenResponse = await fetch('/realms/sse/protocol/openid-connect/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body:
        'grant_type=password&client_id=sse-management&client_secret=' +
        encodeURIComponent(clientSecret) +
        '&username=operator&password=' +
        encodeURIComponent(password),
    });
    const tokenJson = await tokenResponse.json();
    const accessToken = tokenJson.access_token;

    await fetch('/api/layouts', {
      headers: { Authorization: 'Bearer ' + accessToken },
    });

    await fetch('/hubs/layout/negotiate?access_token=' + encodeURIComponent(accessToken));

    window.__probeDone = true;
  });
</script>
</body>
</html>`;

function handleRequest(request, response) {
  const url = new URL(request.url, 'http://127.0.0.1');

  if (request.method === 'GET' && url.pathname === '/login') {
    response.writeHead(200, { 'Content-Type': 'text/html' });
    response.end(LOGIN_PAGE);
    return;
  }

  if (request.method === 'POST' && url.pathname === '/realms/sse/protocol/openid-connect/token') {
    // The request body (containing client_secret= and password=) is read but
    // otherwise unused server-side — its presence on the wire, captured by
    // Playwright's own tracing, is the point of this probe, not anything the
    // server does with it.
    request.on('data', () => {});
    request.on('end', () => {
      response.writeHead(200, { 'Content-Type': 'application/json' });
      response.end(
        JSON.stringify({
          access_token: ACCESS_TOKEN_JWT,
          refresh_token: REFRESH_TOKEN_SENTINEL,
          token_type: 'Bearer',
        }),
      );
    });
    return;
  }

  if (request.method === 'GET' && url.pathname === '/api/layouts') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ layouts: [] }));
    return;
  }

  if (request.method === 'GET' && url.pathname === '/hubs/layout/negotiate') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ connectionId: 'probe-connection', availableTransports: [] }));
    return;
  }

  response.writeHead(404);
  response.end();
}

async function main() {
  const server = createServer(handleRequest);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address();

  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();

  try {
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });

    await page.goto(`http://127.0.0.1:${port}/login`);
    await page.locator('#client-secret').fill(CLIENT_SECRET_SENTINEL);
    await page.locator('#password').fill(PASSWORD_SENTINEL);
    await page.locator('#submit').click();
    await page.waitForFunction(() => window.__probeDone === true);

    mkdirSync(path.dirname(outputPath), { recursive: true });
    await context.tracing.stop({ path: outputPath });
  } finally {
    await browser.close();
    await new Promise((resolve) => server.close(resolve));
  }

  console.log(`wrote ${outputPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
