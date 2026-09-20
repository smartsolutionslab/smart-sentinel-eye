# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: fixture.spec.ts >> fixture: a page snapshot at failure time embeds the typed password value
- Location: scripts\fixtures\trace-redaction\fixture.spec.ts:54:5

# Error details

```
Error: forced failure so the page snapshot is captured while the password field is still filled

expect(received).toBe(expected) // Object.is equality

Expected: false
Received: true
```

# Page snapshot

```yaml
- generic [ref=e2]:
  - text: Password
  - textbox "Password" [active] [ref=e3]: SSE_FAKE_PASSWORD_VALUE
```

# Test source

```ts
  1  | // scripts/fixtures/trace-redaction/fixture.spec.ts
  2  | //
  3  | // Spec 186 / #2287, phase-6 reopen. A THROWAWAY Playwright spec — never part
  4  | // of `e2e/`, never run by `pnpm test:e2e` or CI, and never referencing this
  5  | // repository's real e2e sources. Its only purpose is to be run once, by hand,
  6  | // through the real Playwright Test runner (`make-leaky-report.mjs`) so that
  7  | // Playwright's own reporters produce the two artifact shapes the phase-6
  8  | // security review found unguarded:
  9  | //
  10 | //   - `playwright-report/index.html` — the HTML reporter's self-contained
  11 | //     report, which embeds an `errors[].codeframe` built by re-reading this
  12 | //     file's own source around the failure location.
  13 | //   - `error-context.md` — Playwright's ARIA-snapshot of the page at the
  14 | //     moment of failure, written for every failing test, with no distinct
  15 | //     ARIA role for a password input (it falls back to a generic "textbox"
  16 | //     and writes the typed value in verbatim).
  17 | //
  18 | // The literal below is written directly in this file's source — not
  19 | // imported from `sentinels.mjs` — because the codeframe re-reads *this
  20 | // file's own bytes*; an imported identifier would put only the identifier
  21 | // name in the codeframe, not the value. This mirrors the exact shape the
  22 | // review is proving: a real e2e file (e.g. `e2e/support/sign-in.ts`) has
  23 | // its own literal seeded-realm-password passed to `.fill(...)` directly in
  24 | // source, so its own codeframe would embed it the same way. (Deliberately
  25 | // not spelling that literal here: this fixture's own policy is planted
  26 | // SSE_FAKE_* sentinels only, and this file's source is itself captured
  27 | // verbatim into the generated report data below.)
  28 | const FIXTURE_PASSWORD_VALUE = 'SSE_FAKE_PASSWORD_VALUE';
  29 | 
  30 | import { test, expect } from '@playwright/test';
  31 | 
  32 | test('fixture: a locator assertion reports the filled password value in its own call log', async ({ page }) => {
  33 |   await page.setContent(`
  34 |     <form>
  35 |       <label for="password">Password</label>
  36 |       <input id="password" type="password" />
  37 |     </form>
  38 |   `);
  39 | 
  40 |   // A genuine Playwright fill(), so the value lands in the DOM the way a
  41 |   // real sign-in flow's does.
  42 |   await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);
  43 | 
  44 |   // Deliberately mismatched, so the assertion fails. Playwright's own
  45 |   // `toHaveValue` call log then repeats the field's actual value as a bare
  46 |   // quoted literal — `unexpected value "SSE_FAKE_PASSWORD_VALUE"` — inside
  47 |   // `error.message`, which the JSON reporter serialises verbatim. That shape
  48 |   // (a quoted literal in free text) is not `"key":"value"`, not `key=value`,
  49 |   // not a JWT and not a `Bearer ...` header, so none of the scrubber's four
  50 |   // pattern-backstop regexes match it.
  51 |   await expect(page.locator('#password')).toHaveValue('this-will-never-match', { timeout: 1000 });
  52 | });
  53 | 
  54 | test('fixture: a page snapshot at failure time embeds the typed password value', async ({ page }) => {
  55 |   await page.setContent(`
  56 |     <form>
  57 |       <label for="password">Password</label>
  58 |       <input id="password" type="password" />
  59 |     </form>
  60 |   `);
  61 | 
  62 |   await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);
  63 | 
  64 |   // A plain assertion (not a locator matcher) fails while the field is
  65 |   // still filled and the page is still open. That is what makes Playwright
  66 |   // capture a whole-page ARIA snapshot after the test function returns
  67 |   // (`ArtifactsRecorder._takePageSnapshot`, `page.ariaSnapshot({mode:'ai'})`)
  68 |   // and write it into `error-context.md` under its own "# Page snapshot"
  69 |   // heading — the same mechanism as the first test, on a different path
  70 |   // through Playwright's own reporting code, so both are exercised for
  71 |   // real rather than only one being assumed to generalise to the other.
> 72 |   expect(true, 'forced failure so the page snapshot is captured while the password field is still filled').toBe(false);
     |                                                                                                            ^ Error: forced failure so the page snapshot is captured while the password field is still filled
  73 | });
  74 | 
```