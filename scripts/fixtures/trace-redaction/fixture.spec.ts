// scripts/fixtures/trace-redaction/fixture.spec.ts
//
// Spec 186 / #2287, phase-6 reopen. A THROWAWAY Playwright spec — never part
// of `e2e/`, never run by `pnpm test:e2e` or CI, and never referencing this
// repository's real e2e sources. Its only purpose is to be run once, by hand,
// through the real Playwright Test runner (`make-leaky-report.mjs`) so that
// Playwright's own reporters produce the two artifact shapes the phase-6
// security review found unguarded:
//
//   - `playwright-report/index.html` — the HTML reporter's self-contained
//     report, which embeds an `errors[].codeframe` built by re-reading this
//     file's own source around the failure location.
//   - `error-context.md` — Playwright's ARIA-snapshot of the page at the
//     moment of failure, written for every failing test, with no distinct
//     ARIA role for a password input (it falls back to a generic "textbox"
//     and writes the typed value in verbatim).
//
// The literal below is written directly in this file's source — not
// imported from `sentinels.mjs` — because the codeframe re-reads *this
// file's own bytes*; an imported identifier would put only the identifier
// name in the codeframe, not the value. This mirrors the exact shape the
// review is proving: a real e2e file (e.g. `e2e/support/sign-in.ts`) has
// its own literal seeded-realm-password passed to `.fill(...)` directly in
// source, so its own codeframe would embed it the same way. (Deliberately
// not spelling that literal here: this fixture's own policy is planted
// SSE_FAKE_* sentinels only, and this file's source is itself captured
// verbatim into the generated report data below.)
const FIXTURE_PASSWORD_VALUE = 'SSE_FAKE_PASSWORD_VALUE';

import { test, expect } from '@playwright/test';

test('fixture: a locator assertion reports the filled password value in its own call log', async ({ page }) => {
  await page.setContent(`
    <form>
      <label for="password">Password</label>
      <input id="password" type="password" />
    </form>
  `);

  // A genuine Playwright fill(), so the value lands in the DOM the way a
  // real sign-in flow's does.
  await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);

  // Deliberately mismatched, so the assertion fails. Playwright's own
  // `toHaveValue` call log then repeats the field's actual value as a bare
  // quoted literal — `unexpected value "SSE_FAKE_PASSWORD_VALUE"` — inside
  // `error.message`, which the JSON reporter serialises verbatim. That shape
  // (a quoted literal in free text) is not `"key":"value"`, not `key=value`,
  // not a JWT and not a `Bearer ...` header, so none of the scrubber's four
  // pattern-backstop regexes match it.
  await expect(page.locator('#password')).toHaveValue('this-will-never-match', { timeout: 1000 });
});

test('fixture: a page snapshot at failure time embeds the typed password value', async ({ page }) => {
  await page.setContent(`
    <form>
      <label for="password">Password</label>
      <input id="password" type="password" />
    </form>
  `);

  await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);

  // A plain assertion (not a locator matcher) fails while the field is
  // still filled and the page is still open. That is what makes Playwright
  // capture a whole-page ARIA snapshot after the test function returns
  // (`ArtifactsRecorder._takePageSnapshot`, `page.ariaSnapshot({mode:'ai'})`)
  // and write it into `error-context.md` under its own "# Page snapshot"
  // heading — the same mechanism as the first test, on a different path
  // through Playwright's own reporting code, so both are exercised for
  // real rather than only one being assumed to generalise to the other.
  expect(true, 'forced failure so the page snapshot is captured while the password field is still filled').toBe(false);
});
