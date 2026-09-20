// Spec 186 / #2287 — the fake secrets planted throughout this fixture set.
//
// Every value here is a clearly-fake, greppable, unique string — never a real
// credential. Shared between `make-leaky-trace.mjs` (which plants them into a
// real Chromium-produced trace) and `scrub-playwright-artifacts.test.mjs`
// (which asserts they are present before scrubbing and absent after), so the
// two never drift out of sync with each other or with `leaky-report.json`'s
// hand-written literals.

export const ACCESS_TOKEN_SENTINEL = 'SSE_FAKE_ACCESS_TOKEN_PAYLOAD';
export const REFRESH_TOKEN_SENTINEL = 'SSE_FAKE_REFRESH_TOKEN_VALUE';
export const CLIENT_SECRET_SENTINEL = 'SSE_FAKE_CLIENT_SECRET';
export const PASSWORD_SENTINEL = 'SSE_FAKE_PASSWORD_VALUE';
// Phase-6 reopen round 3 (#2287): the exact string `fixture-source-literal.spec.ts`
// hardcodes as a plain module-level constant, mirroring a real e2e literal like
// `e2e/wall-authority.spec.ts`'s `const WALL_PASSWORD = 'Wall-munich-1234';`.
// Hardcoded there too, not imported from here — the codeframe re-reads that
// file's own bytes, so an imported identifier would put only the identifier
// name into the codeframe, never the value.
export const SOURCE_LITERAL_SENTINEL = 'SSE_FAKE_SOURCE_LITERAL_SECRET';
// Phase-6 reopen round 3 (#2287), S3: the value asserted-but-never-received by
// a `toHaveValue` failure, so it lands on the `Expected:` side of Playwright's
// own call-log wording rather than the `Received:` side
// `collectReportSweepValues`'s `UNEXPECTED_VALUE_PATTERN` already harvests.
export const EXPECTED_VALUE_SENTINEL = 'SSE_FAKE_EXPECTED_CREDENTIAL_VALUE';

// The shape a real Keycloak access token takes: a JWT with a real-looking
// header, the sentinel standing in for the payload, and a throwaway signature
// segment. `spec.md` §1.3 S1/S2/S3 all show this exact shape.
export const ACCESS_TOKEN_JWT = `eyJhbGciOiJSUzI1NiJ9.${ACCESS_TOKEN_SENTINEL}.sig`;
