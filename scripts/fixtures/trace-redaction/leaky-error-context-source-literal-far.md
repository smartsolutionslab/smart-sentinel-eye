# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: fixture-source-literal.spec.ts >> fixture: an unrelated failure padded ~95 lines below the constant still echoes it via the large codeframe window
- Location: scripts\fixtures\trace-redaction\fixture-source-literal.spec.ts:58:5

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: 4
Received: 3
```

# Test source

```ts
  43  | //     inside this much larger one — and its own per-test codeframe / Test
  44  | //     source section still reads the constant off the top of the file. This
  45  | //     is "the HTML report for every OTHER test in the same file" the review
  46  | //     named: the codeframe read is file-scoped, but the scrubber's sweep is
  47  | //     scoped to one test result, so the second test's leak has no pairing to
  48  | //     be swept by, exactly like the first.
  49  | //
  50  | // The literal is written directly in this file's source — not imported from
  51  | // `sentinels.mjs` — for the same reason `fixture.spec.ts` does it: the
  52  | // codeframe re-reads *this file's own bytes*, so an imported identifier would
  53  | // put only the identifier name into the codeframe, never the value.
  54  | const FIXTURE_SOURCE_LITERAL_SECRET = 'SSE_FAKE_SOURCE_LITERAL_SECRET';
  55  | import { test, expect } from '@playwright/test';
  56  | test('fixture: an unrelated failure right next to the constant echoes it via the small codeframe window', async () => { expect(1).toBe(2); });
  57  | 
  58  | test('fixture: an unrelated failure padded ~95 lines below the constant still echoes it via the large codeframe window', async () => {
  59  |   // Padding lines below push this failure's line number far enough from the
  60  |   // constant above that it falls outside `error.snippet`'s tiny (2-above/
  61  |   // 3-below) window while remaining inside `error-context.md` /
  62  |   // `errors[].codeframe`'s much larger (100-above/100-below) one.
  63  |   // padding line 0 -- keeps this failure far from the constant above
  64  |   // padding line 1 -- keeps this failure far from the constant above
  65  |   // padding line 2 -- keeps this failure far from the constant above
  66  |   // padding line 3 -- keeps this failure far from the constant above
  67  |   // padding line 4 -- keeps this failure far from the constant above
  68  |   // padding line 5 -- keeps this failure far from the constant above
  69  |   // padding line 6 -- keeps this failure far from the constant above
  70  |   // padding line 7 -- keeps this failure far from the constant above
  71  |   // padding line 8 -- keeps this failure far from the constant above
  72  |   // padding line 9 -- keeps this failure far from the constant above
  73  |   // padding line 10 -- keeps this failure far from the constant above
  74  |   // padding line 11 -- keeps this failure far from the constant above
  75  |   // padding line 12 -- keeps this failure far from the constant above
  76  |   // padding line 13 -- keeps this failure far from the constant above
  77  |   // padding line 14 -- keeps this failure far from the constant above
  78  |   // padding line 15 -- keeps this failure far from the constant above
  79  |   // padding line 16 -- keeps this failure far from the constant above
  80  |   // padding line 17 -- keeps this failure far from the constant above
  81  |   // padding line 18 -- keeps this failure far from the constant above
  82  |   // padding line 19 -- keeps this failure far from the constant above
  83  |   // padding line 20 -- keeps this failure far from the constant above
  84  |   // padding line 21 -- keeps this failure far from the constant above
  85  |   // padding line 22 -- keeps this failure far from the constant above
  86  |   // padding line 23 -- keeps this failure far from the constant above
  87  |   // padding line 24 -- keeps this failure far from the constant above
  88  |   // padding line 25 -- keeps this failure far from the constant above
  89  |   // padding line 26 -- keeps this failure far from the constant above
  90  |   // padding line 27 -- keeps this failure far from the constant above
  91  |   // padding line 28 -- keeps this failure far from the constant above
  92  |   // padding line 29 -- keeps this failure far from the constant above
  93  |   // padding line 30 -- keeps this failure far from the constant above
  94  |   // padding line 31 -- keeps this failure far from the constant above
  95  |   // padding line 32 -- keeps this failure far from the constant above
  96  |   // padding line 33 -- keeps this failure far from the constant above
  97  |   // padding line 34 -- keeps this failure far from the constant above
  98  |   // padding line 35 -- keeps this failure far from the constant above
  99  |   // padding line 36 -- keeps this failure far from the constant above
  100 |   // padding line 37 -- keeps this failure far from the constant above
  101 |   // padding line 38 -- keeps this failure far from the constant above
  102 |   // padding line 39 -- keeps this failure far from the constant above
  103 |   // padding line 40 -- keeps this failure far from the constant above
  104 |   // padding line 41 -- keeps this failure far from the constant above
  105 |   // padding line 42 -- keeps this failure far from the constant above
  106 |   // padding line 43 -- keeps this failure far from the constant above
  107 |   // padding line 44 -- keeps this failure far from the constant above
  108 |   // padding line 45 -- keeps this failure far from the constant above
  109 |   // padding line 46 -- keeps this failure far from the constant above
  110 |   // padding line 47 -- keeps this failure far from the constant above
  111 |   // padding line 48 -- keeps this failure far from the constant above
  112 |   // padding line 49 -- keeps this failure far from the constant above
  113 |   // padding line 50 -- keeps this failure far from the constant above
  114 |   // padding line 51 -- keeps this failure far from the constant above
  115 |   // padding line 52 -- keeps this failure far from the constant above
  116 |   // padding line 53 -- keeps this failure far from the constant above
  117 |   // padding line 54 -- keeps this failure far from the constant above
  118 |   // padding line 55 -- keeps this failure far from the constant above
  119 |   // padding line 56 -- keeps this failure far from the constant above
  120 |   // padding line 57 -- keeps this failure far from the constant above
  121 |   // padding line 58 -- keeps this failure far from the constant above
  122 |   // padding line 59 -- keeps this failure far from the constant above
  123 |   // padding line 60 -- keeps this failure far from the constant above
  124 |   // padding line 61 -- keeps this failure far from the constant above
  125 |   // padding line 62 -- keeps this failure far from the constant above
  126 |   // padding line 63 -- keeps this failure far from the constant above
  127 |   // padding line 64 -- keeps this failure far from the constant above
  128 |   // padding line 65 -- keeps this failure far from the constant above
  129 |   // padding line 66 -- keeps this failure far from the constant above
  130 |   // padding line 67 -- keeps this failure far from the constant above
  131 |   // padding line 68 -- keeps this failure far from the constant above
  132 |   // padding line 69 -- keeps this failure far from the constant above
  133 |   // padding line 70 -- keeps this failure far from the constant above
  134 |   // padding line 71 -- keeps this failure far from the constant above
  135 |   // padding line 72 -- keeps this failure far from the constant above
  136 |   // padding line 73 -- keeps this failure far from the constant above
  137 |   // padding line 74 -- keeps this failure far from the constant above
  138 |   // padding line 75 -- keeps this failure far from the constant above
  139 |   // padding line 76 -- keeps this failure far from the constant above
  140 |   // padding line 77 -- keeps this failure far from the constant above
  141 |   // padding line 78 -- keeps this failure far from the constant above
  142 |   // padding line 79 -- keeps this failure far from the constant above
> 143 |   expect(3).toBe(4);
      |             ^ Error: expect(received).toBe(expected) // Object.is equality
  144 | });
  145 | 
  146 | // --- S2: an unlabelled password field, no accessible name to key a redaction off ---
  147 | //
  148 | // `scrub-playwright-artifacts.mjs`'s `error-context.md` handling
  149 | // (`redactAriaSnapshotSection`) only redacts a role line whose *quoted label*
  150 | // reads as credential-shaped (`ARIA_ROLE_VALUE_LINE_PATTERN` requires a
  151 | // `"<label>"` to test). A password field with no associated `<label>`, no
  152 | // `aria-label` and no `placeholder` gets no quoted name at all in Playwright's
  153 | // own ARIA snapshot, so the pattern never even reaches the point of testing
  154 | // the label — the typed value is left completely untouched.
  155 | import { PASSWORD_SENTINEL, EXPECTED_VALUE_SENTINEL } from './sentinels.mjs';
  156 | 
  157 | test('fixture: an unlabelled password field embeds its typed value with no accessible name', async ({ page }) => {
  158 |   await page.setContent(`<input type="password" />`);
  159 | 
  160 |   await page.locator('input[type="password"]').fill(PASSWORD_SENTINEL);
  161 | 
  162 |   // Mirrors `fixture.spec.ts`'s second test: a plain (non-locator) assertion
  163 |   // failure while the field is still filled and the page still open is what
  164 |   // makes Playwright capture a whole-page ARIA snapshot into `error-context.md`.
  165 |   expect(true, 'forced failure so the page snapshot is captured while the password field is still filled').toBe(false);
  166 | });
  167 | 
  168 | // --- S3: the credential on the *expected* side of a failed assertion -------
  169 | //
  170 | // `scrub-playwright-artifacts.mjs`'s report-level value sweep
  171 | // (`collectReportSweepValues`) only harvests a value out of Playwright's own
  172 | // `unexpected value "..."` call-log wording — the RECEIVED side of a failed
  173 | // `toHaveValue`. A test that asserts a field's value should equal a real
  174 | // credential, and it doesn't, produces the credential on the EXPECTED side
  175 | // instead (`Expected: "<credential>" ... Received: "<something else>"`), a
  176 | // shape none of the scrubber's patterns or pairings look for.
  177 | test('fixture: a toHaveValue assertion fails with the credential on the expected side', async ({ page }) => {
  178 |   await page.setContent(`
  179 |     <form>
  180 |       <label for="confirm-secret">Confirm secret</label>
  181 |       <input id="confirm-secret" type="text" value="not-the-real-secret" />
  182 |     </form>
  183 |   `);
  184 | 
  185 |   await expect(page.locator('#confirm-secret')).toHaveValue(EXPECTED_VALUE_SENTINEL, { timeout: 1000 });
  186 | });
  187 | 
```