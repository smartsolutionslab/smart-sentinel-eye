# Verification 186 — A trace that keeps its secrets (#2287)

**Latency: N/A.** CI/test-infrastructure change; no production assembly
changes, no leg of constitution §IV's event→overlay path touched.

## Summary

This delivery went through **four rounds**: the original phase 4a/4b (a
scrubber for Playwright's trace zip), then three phase-6 security-review
reopens, each finding a genuine, real, evidence-based gap the previous
round's fixture didn't exercise. Every fixture in every round is a real
artifact produced by real headless Chromium under real Playwright 1.62.1
tracing or the real Playwright Test runner — never hand-approximated — and
every fix was independently re-verified by the orchestrator (not only
trusted from an agent's own report) before the next round began.

This is recorded in full, including the parts that did not go cleanly,
because a security fix whose own history is edited down to "and then it
worked" is exactly the kind of record this repository's own culture
distrusts.

## Round 0 — phase 4a/4b, the original scrubber

**Red (ADR-0139)**, quoted from commit `06bf8280`, reproduced independently:
a real Chromium trace (`leaky-trace.zip`, 9 entries, 15,387 bytes — every
value a planted `SSE_FAKE_*` sentinel, manually unzipped and read by the
orchestrator before and after every subsequent change) carrying a Keycloak
token response, a bearer-authenticated call, a SignalR negotiate, and a
password sign-in, asserted leaky in the specific entries `spec.md` §1.3
names; all four presence checks passed, all absence checks failed with
`MODULE_NOT_FOUND` (the scrubber didn't exist yet).

**Fix** (`eacf8a25`): `scripts/scrub-playwright-artifacts.mjs` — structural
redaction of `trace.network`/`trace.trace`, a pattern backstop for
resource files, `sources: false` in `playwright.config.ts`, and the CI
wiring (`Scrub credentials from the Playwright artifacts` step, gated
upload). `fflate@0.8.3` — exact pin, zero transitive dependencies,
confirmed from `pnpm-lock.yaml` directly, never imported outside
`scripts/`.

## Round 1 — first phase-6 review: two blockers

`security-reviewer` proved, by constructing the exact artifact shape and
running the shipped scrubber against it:

1. **Playwright's HTML reporter embeds an entire report as a base64 zip
   inside `index.html`**, wrapped in `<template id="playwrightReportBase64">`
   — never classified as a candidate (neither `.json` nor zip-magic-prefixed
   at the file's own start), so it passed through untouched. Its
   `errors[].codeframe` re-reads the failing test's own source file, which
   for this repository's real `e2e/**` files means literal seeded realm
   passwords (`Wall-munich-1234`, `Operator1234`, `dev-only-keycloak-admin`).
2. **`error-context.md`** (Playwright's per-failing-test ARIA page snapshot,
   written for every failure, not just retries) was never a candidate at
   all, and Playwright has no distinct ARIA role for a password input — it
   falls back to a generic `textbox` and writes the typed value verbatim.

Fixed in `b6c8780d`: HTML-report detection/decode/redact/re-encode, `.md`
handling with label-scoped redaction, plus a fixture-policy correction
(`4fa94f19` — the test spec's own doc comment had spelled out
`Operator1234` as an illustrative example; not a new disclosure since that
value is already public in `e2e/**`, but it broke this feature's own
"planted sentinels only" policy, so it was reworded and both generated
fixtures regenerated). All 12 guard facts green, independently re-run by
the orchestrator; 49/49 full `test:guards`; `lint:e2e`/`typecheck:e2e`
clean.

Also self-corrected in this round: the CI comment's stated reason for
avoiding `continue-on-error: true` was checked against GitHub's own
documented `outcome`/`conclusion` semantics (fetched live) and found
backwards — `outcome` is the *pre*-`continue-on-error` result, not the
post-override one, so the upload gate (`steps.scrub.outcome == 'success'`)
was already correct regardless; only the comment's stated mechanism was
wrong. Corrected in `6438b80a`.

## Round 2 — second phase-6 review: one blocker, three should-fixes

A second review, scoped to the round-1 fix, found:

**Blocker — the sweep mechanism only fires when a value is paired with a
`fill()`/locator shape in the same test result.** A bare quoted literal in
free text (Playwright's own `unexpected value "..."` call-log wording)
matched none of the four pattern-backstop regexes.

**Should-fixes**, all reproduced on real output: HTML-report detection
matched an exact strict template string and failed **open** (silently
skipped, exit 0) on any wrapping drift, with its own intended fail-closed
`throw` unreachable dead code; the ARIA redaction required a quoted
accessible name, so an *unlabelled* password input's value survived; the
assertion-message harvesting only read the `Received:`/`unexpected value`
side, so a credential on the `Expected:` side of a `toHaveValue` failure
survived.

Fixed in `29e1bc38` (after test-writer extended the fixture/guard in
`f21a5f4e`, verified 17→23 facts, asymmetric red confirmed independently):
a document-wide (not just per-result) sweep for source-literal shapes,
loose-detect/strict-extract for the HTML path so a format drift now fails
closed instead of silently skipping, unlabelled-textbox redaction, and
`Expected:`-side harvesting. 23/23 guard facts, 60/60 full suite,
independently re-verified.

## Round 3 — third phase-6 review: a deeper root-cause finding

A third review found the actual root cause underlying rounds 1 and 2: **a
literal credential assigned directly in e2e source code
(`const WALL_PASSWORD = 'Wall-munich-1234';`) gets echoed into Playwright's
codeframes/snippets whenever *any* test *anywhere in that file* fails
within the codeframe's window** — with no `fill()`/locator pairing nearby
at all, since the failing test can be entirely unrelated to the constant.
Reproduced on real Playwright 1.62.1 output, with the two different
codeframe windows Playwright actually uses verified directly against the
installed package source (`@babel/code-frame`'s small 2/3-line window for
`error.snippet`/`errors[].message`; `createErrorCodeframe`'s much larger
100/100-line window for `error-context.md` and the HTML report).

Fixed in `29e1bc38` alongside round 2's fixes: a new structural pairing
(`SOURCE_LITERAL_ASSIGNMENT_PATTERN`) matching a `const`/`let`/`var`
assignment whose *identifier* reads as credential-shaped — never matched
by value — applied as a **document-wide** pass (not scoped to one test
result, since the leak isn't), because the codeframe window is anchored to
*its own* failure's line number, not the constant's.

**Self-found and self-fixed in this same round, before opening the PR**:
independently re-checking every real credential occurrence in this
repository's actual `e2e/**` against the shipped pattern, one did not
match — `e2e/wall-withdrawal.spec.ts:43` assigns through
`process.env['SSE_KEYCLOAK_ADMIN_PASSWORD'] ?? 'dev-only-keycloak-admin'`,
and the original lazy-match pattern captured the env-var's own *name*
(itself a quoted string earlier on the line) rather than the real fallback
literal. Fixed by widening to a greedy match that backtracks to the last
quoted string before the statement ends (`a1908855`), verified against
both shapes directly and via a full scrubber run over a report-shaped
fixture containing the exact real line. 23/23 guard facts and 60/60 full
suite confirmed green after this change; not independently re-reviewed by
a fourth dispatched agent, given diminishing returns after three thorough
rounds — recorded here so the provenance is explicit.

## Independent verification performed by the orchestrator, every round

Not merely trusted from any agent's report: manually unzipped and read
every committed binary/generated fixture byte-for-byte before and after
each round (`leaky-trace.zip`, `leaky-report-index.html`'s embedded base64
zip — decoded and re-unzipped independently each time,
`leaky-error-context*.md`, `leaky-report-call-log.json`,
`leaky-report-source-literal*`); re-ran `node --test
scripts/scrub-playwright-artifacts.test.mjs` and the full `pnpm test:guards`
/ `lint:e2e` / `typecheck:e2e` independently after every commit, never
accepting a reported pass/fail count without reproducing it; ran the
scrubber against fresh copies of every fixture and grepped the output
broadly (not only for the specific sentinels each test names) for anything
credential-shaped; confirmed the `fflate` dependency's exact pin and zero
transitive dependencies directly from the lockfile; fetched GitHub's own
Actions documentation to verify the `outcome`/`conclusion` semantics claim
rather than trust it from memory; cross-checked the `trace: { mode,
sources }` config shape against the actual installed Playwright 1.62.1
type definitions; stripped one `Co-Authored-By` footer violation via
`git filter-branch`, confirming via `git diff` that only the commit
message changed, never the tree.

## What was NOT performed — recorded explicitly, not silently omitted

**Spec.md §6.2's live counterfactual against a real booted Aspire stack and
real Keycloak-issued tokens was not performed.** It requires booting the
full stack and forcing a genuine e2e failure against real credentials —
deferred given this delivery's resource constraints (two review-subagent
dispatches failed on account rate/spend limits earlier in this same
session; parallel dispatch was the trigger, sequential dispatch worked
throughout the rest of this delivery).

**Why the fixture-based evidence is judged a reasonable stand-in for the
token-shape findings (S1-S4 of the original round), but explicitly not for
the source-literal findings (rounds 2-3):** the scrubber's structural and
pattern-backstop logic operates on shape, not on whether a token's value
happens to be real or fake — a real Keycloak-issued JWT starts with `eyJ`
and follows the exact three-dot-separated shape `JWT_PATTERN` already
matches, identically to the fixture's fake one. But rounds 2 and 3's
findings were never about token realism — they were about **artifact types
and code paths the original tracing-API-only fixture generator structurally
could not produce** (an HTML report, a JSON test report, an
`error-context.md`, a source-literal assignment reached by an unrelated
failure) because it never ran a real Playwright Test runner pass. Every
one of those artifact types **has** now been reproduced for real, via an
actual `pnpm exec playwright test` run against a throwaway fixture spec
(rounds 1-3's `fixture*.spec.ts` files) — the remaining gap in §6.2 is
specifically the substitution of `SSE_FAKE_*` sentinels for genuine
Keycloak-issued tokens and a genuine sign-in flow against this repository's
own real realm, not a missing artifact type.

**Recommendation**: perform §6.2 as a fast-follow — boot the stack, force
one real e2e failure in a spec that calls a real sign-in helper, and grep
the resulting real artifact tree before and after scrubbing. Given four
rounds of real, structural findings on the fixture side, this live check
is now more likely to *confirm* the fix than to find a fifth gap — but
after four rounds each finding something real, that confidence is stated
as a judgement, not certainty.

## Residuals accepted in writing, not fixed

- **A localized (non-English) ARIA label would not match
  `CREDENTIAL_SELECTOR_PATTERN` (`/password|secret|token/i`)** — e.g. a
  German `"Passwort"` label. This repository's actual UI and e2e specs are
  English-only today (confirmed: no i18n framework in `apps/`, no
  non-English strings in `e2e/**`), so this is latent, not live. Recorded
  because the pattern is not documented as English-only anywhere, and a
  future localization effort should re-read this file before assuming
  coverage is complete.
- **The scrubber's design is structural/contextual by deliberate choice
  (never a hardcoded credential-value list)**, and this trade-off has a
  real, inherent limit: a genuinely novel fourth or fifth way a credential
  might appear in free-form prose or source (a template literal, a
  destructured default, a value passed through an unusual number of
  intermediate variables before reaching a `fill()` call) could still slip
  through, because the design recognizes *shapes*, not values, and there
  will always be shapes not yet enumerated. Every shape actually present in
  this repository's real `e2e/**` source has been checked, found, and
  closed (confirmed by an exhaustive grep of every real credential
  occurrence against the final pattern set, immediately before opening this
  PR) — but this is a statement about today's code, not a permanent
  guarantee against a future one. The mitigating factor: `sources: false`
  already removes the *dominant* source-echoing vector (the trace zip's
  `resources/src@*.txt` full-file embed) at the config level, independent
  of pattern coverage; the residual is specifically about the smaller
  codeframe/snippet excerpts the other three artifact types still produce.
- **Playwright version upgrades remain a live risk by design, not an
  oversight** — `spec.md`'s own US2 exists because artifact shapes are
  observed against the pinned 1.62.1, not read from a stability guarantee.
  S1's fix (loose-detect, strict-extract, fail closed on drift) specifically
  hardens the HTML-report path against this; the guard's fixtures should be
  regenerated and the presence assertions re-run on any future
  `@playwright/test` version bump, per the generator scripts' own header
  comments.
