# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: fixture.spec.ts >> fixture: a page snapshot at failure time embeds the typed password value
- Location: scripts\fixtures\trace-redaction\fixture.spec.ts:51:5

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
  23 | // `.fill('Operator1234')` as a literal in its own source, so its own
  24 | // codeframe would embed it the same way.
  25 | const FIXTURE_PASSWORD_VALUE = 'SSE_FAKE_PASSWORD_VALUE';
  26 | 
  27 | import { test, expect } from '@playwright/test';
  28 | 
  29 | test('fixture: a locator assertion reports the filled password value in its own call log', async ({ page }) => {
  30 |   await page.setContent(`
  31 |     <form>
  32 |       <label for="password">Password</label>
  33 |       <input id="password" type="password" />
  34 |     </form>
  35 |   `);
  36 | 
  37 |   // A genuine Playwright fill(), so the value lands in the DOM the way a
  38 |   // real sign-in flow's does.
  39 |   await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);
  40 | 
  41 |   // Deliberately mismatched, so the assertion fails. Playwright's own
  42 |   // `toHaveValue` call log then repeats the field's actual value as a bare
  43 |   // quoted literal — `unexpected value "SSE_FAKE_PASSWORD_VALUE"` — inside
  44 |   // `error.message`, which the JSON reporter serialises verbatim. That shape
  45 |   // (a quoted literal in free text) is not `"key":"value"`, not `key=value`,
  46 |   // not a JWT and not a `Bearer ...` header, so none of the scrubber's four
  47 |   // pattern-backstop regexes match it.
  48 |   await expect(page.locator('#password')).toHaveValue('this-will-never-match', { timeout: 1000 });
  49 | });
  50 | 
  51 | test('fixture: a page snapshot at failure time embeds the typed password value', async ({ page }) => {
  52 |   await page.setContent(`
  53 |     <form>
  54 |       <label for="password">Password</label>
  55 |       <input id="password" type="password" />
  56 |     </form>
  57 |   `);
  58 | 
  59 |   await page.locator('#password').fill(FIXTURE_PASSWORD_VALUE);
  60 | 
  61 |   // A plain assertion (not a locator matcher) fails while the field is
  62 |   // still filled and the page is still open. That is what makes Playwright
  63 |   // capture a whole-page ARIA snapshot after the test function returns
  64 |   // (`ArtifactsRecorder._takePageSnapshot`, `page.ariaSnapshot({mode:'ai'})`)
  65 |   // and write it into `error-context.md` under its own "# Page snapshot"
  66 |   // heading — the same mechanism as the first test, on a different path
  67 |   // through Playwright's own reporting code, so both are exercised for
  68 |   // real rather than only one being assumed to generalise to the other.
> 69 |   expect(true, 'forced failure so the page snapshot is captured while the password field is still filled').toBe(false);
     |                                                                                                            ^ Error: forced failure so the page snapshot is captured while the password field is still filled
  70 | });
  71 | 
```