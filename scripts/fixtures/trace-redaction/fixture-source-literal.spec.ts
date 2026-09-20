// scripts/fixtures/trace-redaction/fixture-source-literal.spec.ts
//
// Spec 186 / #2287, phase-6 reopen round 3 (2026-09-20). A THROWAWAY Playwright
// spec, run only by `make-leaky-report-source-literal.mjs` through the real
// Playwright Test runner — never part of `e2e/`, never run by
// `pnpm test:e2e` or CI, and never referencing this repository's real e2e
// sources.
//
// A third security review reproduced four further genuine gaps in the
// already-shipped scrubber, all on real Playwright 1.62.1 output. The four
// tests below produce them for real rather than by hand-authored guesswork.
//
// --- B1: a source-code literal, echoed with no runtime pairing at all -----
//
// The first two tests prove a credential-shaped literal declared as a plain
// module-level source constant (mirroring a real e2e literal such as
// `e2e/wall-authority.spec.ts`'s `const WALL_PASSWORD = 'Wall-munich-1234';`)
// is echoed into Playwright's own codeframes and snippets whenever ANY test
// in the file fails close enough to read it — with no `.fill()`/locator call
// anywhere nearby to pair it with, because neither test below ever touches
// this constant at all. The scrubber's sweep mechanism only fires on a
// runtime call-log pairing (a `fill()` action, a `toHaveValue` mismatch); a
// bare source-code literal has neither, so it survives untouched.
//
// Two different Playwright codeframe mechanisms are exercised, each with its
// own line-count window — verified directly in the installed
// `playwright/lib/errorContext.js` and `playwright/lib/runner/index.js`, not
// assumed from documentation:
//
//   - The JSON report's `result.error.snippet` field and `result.errors[]`'s
//     `message` field (built by `formatError` / `addLocationAndSnippetToError`
//     in `playwright/lib/runner/index.js`) both come from a plain
//     `@babel/code-frame` call using ITS OWN default window —
//     `linesAbove: 2, linesBelow: 3`. Only a failure within a couple of lines
//     of the constant pulls it in. The FIRST test below fails on the very
//     next statement, deliberately inside that tiny window.
//   - `error-context.md`'s "# Test source" section (`buildCodeFrame` in
//     `errorContext.js`) and the HTML report's per-test `errors[].codeframe`
//     (`createErrorCodeframe` in `runner/index.js`) both use a much larger,
//     explicitly-set window — `linesAbove: 100, linesBelow: 100`. The SECOND
//     test below fails ~95 comment-padded lines below the constant —
//     deliberately past the first test's tiny window, but still comfortably
//     inside this much larger one — and its own per-test codeframe / Test
//     source section still reads the constant off the top of the file. This
//     is "the HTML report for every OTHER test in the same file" the review
//     named: the codeframe read is file-scoped, but the scrubber's sweep is
//     scoped to one test result, so the second test's leak has no pairing to
//     be swept by, exactly like the first.
//
// The literal is written directly in this file's source — not imported from
// `sentinels.mjs` — for the same reason `fixture.spec.ts` does it: the
// codeframe re-reads *this file's own bytes*, so an imported identifier would
// put only the identifier name into the codeframe, never the value.
const FIXTURE_SOURCE_LITERAL_SECRET = 'SSE_FAKE_SOURCE_LITERAL_SECRET';
import { test, expect } from '@playwright/test';
test('fixture: an unrelated failure right next to the constant echoes it via the small codeframe window', async () => { expect(1).toBe(2); });

test('fixture: an unrelated failure padded ~95 lines below the constant still echoes it via the large codeframe window', async () => {
  // Padding lines below push this failure's line number far enough from the
  // constant above that it falls outside `error.snippet`'s tiny (2-above/
  // 3-below) window while remaining inside `error-context.md` /
  // `errors[].codeframe`'s much larger (100-above/100-below) one.
  // padding line 0 -- keeps this failure far from the constant above
  // padding line 1 -- keeps this failure far from the constant above
  // padding line 2 -- keeps this failure far from the constant above
  // padding line 3 -- keeps this failure far from the constant above
  // padding line 4 -- keeps this failure far from the constant above
  // padding line 5 -- keeps this failure far from the constant above
  // padding line 6 -- keeps this failure far from the constant above
  // padding line 7 -- keeps this failure far from the constant above
  // padding line 8 -- keeps this failure far from the constant above
  // padding line 9 -- keeps this failure far from the constant above
  // padding line 10 -- keeps this failure far from the constant above
  // padding line 11 -- keeps this failure far from the constant above
  // padding line 12 -- keeps this failure far from the constant above
  // padding line 13 -- keeps this failure far from the constant above
  // padding line 14 -- keeps this failure far from the constant above
  // padding line 15 -- keeps this failure far from the constant above
  // padding line 16 -- keeps this failure far from the constant above
  // padding line 17 -- keeps this failure far from the constant above
  // padding line 18 -- keeps this failure far from the constant above
  // padding line 19 -- keeps this failure far from the constant above
  // padding line 20 -- keeps this failure far from the constant above
  // padding line 21 -- keeps this failure far from the constant above
  // padding line 22 -- keeps this failure far from the constant above
  // padding line 23 -- keeps this failure far from the constant above
  // padding line 24 -- keeps this failure far from the constant above
  // padding line 25 -- keeps this failure far from the constant above
  // padding line 26 -- keeps this failure far from the constant above
  // padding line 27 -- keeps this failure far from the constant above
  // padding line 28 -- keeps this failure far from the constant above
  // padding line 29 -- keeps this failure far from the constant above
  // padding line 30 -- keeps this failure far from the constant above
  // padding line 31 -- keeps this failure far from the constant above
  // padding line 32 -- keeps this failure far from the constant above
  // padding line 33 -- keeps this failure far from the constant above
  // padding line 34 -- keeps this failure far from the constant above
  // padding line 35 -- keeps this failure far from the constant above
  // padding line 36 -- keeps this failure far from the constant above
  // padding line 37 -- keeps this failure far from the constant above
  // padding line 38 -- keeps this failure far from the constant above
  // padding line 39 -- keeps this failure far from the constant above
  // padding line 40 -- keeps this failure far from the constant above
  // padding line 41 -- keeps this failure far from the constant above
  // padding line 42 -- keeps this failure far from the constant above
  // padding line 43 -- keeps this failure far from the constant above
  // padding line 44 -- keeps this failure far from the constant above
  // padding line 45 -- keeps this failure far from the constant above
  // padding line 46 -- keeps this failure far from the constant above
  // padding line 47 -- keeps this failure far from the constant above
  // padding line 48 -- keeps this failure far from the constant above
  // padding line 49 -- keeps this failure far from the constant above
  // padding line 50 -- keeps this failure far from the constant above
  // padding line 51 -- keeps this failure far from the constant above
  // padding line 52 -- keeps this failure far from the constant above
  // padding line 53 -- keeps this failure far from the constant above
  // padding line 54 -- keeps this failure far from the constant above
  // padding line 55 -- keeps this failure far from the constant above
  // padding line 56 -- keeps this failure far from the constant above
  // padding line 57 -- keeps this failure far from the constant above
  // padding line 58 -- keeps this failure far from the constant above
  // padding line 59 -- keeps this failure far from the constant above
  // padding line 60 -- keeps this failure far from the constant above
  // padding line 61 -- keeps this failure far from the constant above
  // padding line 62 -- keeps this failure far from the constant above
  // padding line 63 -- keeps this failure far from the constant above
  // padding line 64 -- keeps this failure far from the constant above
  // padding line 65 -- keeps this failure far from the constant above
  // padding line 66 -- keeps this failure far from the constant above
  // padding line 67 -- keeps this failure far from the constant above
  // padding line 68 -- keeps this failure far from the constant above
  // padding line 69 -- keeps this failure far from the constant above
  // padding line 70 -- keeps this failure far from the constant above
  // padding line 71 -- keeps this failure far from the constant above
  // padding line 72 -- keeps this failure far from the constant above
  // padding line 73 -- keeps this failure far from the constant above
  // padding line 74 -- keeps this failure far from the constant above
  // padding line 75 -- keeps this failure far from the constant above
  // padding line 76 -- keeps this failure far from the constant above
  // padding line 77 -- keeps this failure far from the constant above
  // padding line 78 -- keeps this failure far from the constant above
  // padding line 79 -- keeps this failure far from the constant above
  expect(3).toBe(4);
});

// --- S2: an unlabelled password field, no accessible name to key a redaction off ---
//
// `scrub-playwright-artifacts.mjs`'s `error-context.md` handling
// (`redactAriaSnapshotSection`) only redacts a role line whose *quoted label*
// reads as credential-shaped (`ARIA_ROLE_VALUE_LINE_PATTERN` requires a
// `"<label>"` to test). A password field with no associated `<label>`, no
// `aria-label` and no `placeholder` gets no quoted name at all in Playwright's
// own ARIA snapshot, so the pattern never even reaches the point of testing
// the label — the typed value is left completely untouched.
import { PASSWORD_SENTINEL, EXPECTED_VALUE_SENTINEL } from './sentinels.mjs';

test('fixture: an unlabelled password field embeds its typed value with no accessible name', async ({ page }) => {
  await page.setContent(`<input type="password" />`);

  await page.locator('input[type="password"]').fill(PASSWORD_SENTINEL);

  // Mirrors `fixture.spec.ts`'s second test: a plain (non-locator) assertion
  // failure while the field is still filled and the page still open is what
  // makes Playwright capture a whole-page ARIA snapshot into `error-context.md`.
  expect(true, 'forced failure so the page snapshot is captured while the password field is still filled').toBe(false);
});

// --- S3: the credential on the *expected* side of a failed assertion -------
//
// `scrub-playwright-artifacts.mjs`'s report-level value sweep
// (`collectReportSweepValues`) only harvests a value out of Playwright's own
// `unexpected value "..."` call-log wording — the RECEIVED side of a failed
// `toHaveValue`. A test that asserts a field's value should equal a real
// credential, and it doesn't, produces the credential on the EXPECTED side
// instead (`Expected: "<credential>" ... Received: "<something else>"`), a
// shape none of the scrubber's patterns or pairings look for.
test('fixture: a toHaveValue assertion fails with the credential on the expected side', async ({ page }) => {
  await page.setContent(`
    <form>
      <label for="confirm-secret">Confirm secret</label>
      <input id="confirm-secret" type="text" value="not-the-real-secret" />
    </form>
  `);

  await expect(page.locator('#confirm-secret')).toHaveValue(EXPECTED_VALUE_SENTINEL, { timeout: 1000 });
});
