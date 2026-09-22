# Verification — Spec 214 (#2509)

## Phase 4a — CHARACTERISATION, independently re-confirmed at the build level

This delivery adds tests only; there is no production code (Phase 4b: skipped — investigation delivers tests and a finding, not a fix). Independently re-verified:

- `dotnet build tests/Integration.Tests -c Release` — clean, 0 errors, only pre-existing advisory warnings unrelated to any file this spec touches.
- `dotnet test tests/Integration.Tests --list-tests --filter FullyQualifiedName~LockoutSessionSurvivalIntegrationTests` — all six facts discovered by name (SC-3, SC-1, SC-2, SC-5, the malformed-token bad-request case, and SC-4).
- `pnpm typecheck:e2e`, `npx eslint e2e/wall-survives-a-lockout.spec.ts --max-warnings 0`, `npx playwright test --list e2e/wall-survives-a-lockout.spec.ts` — all clean; the one new e2e case discovered by name in the `wall` project.
- `git diff --stat` against `origin/develop` confirms `BruteForceLockoutIntegrationTests.cs`, `RealmProbe.cs`, and `e2e/wall-survives-a-process-death.spec.ts` are all untouched — this delivery adds two new files, widens nothing.

Read the full `LockoutSessionSurvivalIntegrationTests.cs` and `wall-survives-a-lockout.spec.ts` myself, line by line, including their secrets discipline (status/`error`/token-presence only, never token text or a raw response body in a failure message — both files hold this throughout) and their teardown guarantees (throwaway probe users deleted in `finally`; `wall-munich`'s attack-detection record cleared in a `finally` that runs on any failure, not just success).

**Not independently re-observed as red-then-green:** T003's counterfactual (inverting SC-1's own assertion, confirming it fails, reverting) has not been executed anywhere yet — see below.

## Phase 5 — the live observation: not obtained locally, deferred to CI

**This is the actual question the spec exists to answer, and it remains unanswered as of this note.** Free RAM measured 3.2GB before this phase, consistent with the resource pressure already documented delivering #2432, #2430, and #2428 earlier today — all three deferred live Aspire verification for the same reason, and all three were later confirmed correct by reading CI's own job log/TRX artifact rather than the bucket's aggregate conclusion (the working pattern this note follows for a fourth time).

No local Aspire stack was available at any point during this delivery (`mcp__aspire__list_apphosts` returned empty every time it was checked), so:

- **T001's SC-1** (`A_locked_out_account_can_still_spend_its_refresh_token`) — the primary observation — has never been executed against a live server.
- **T002's SC-6** (`wall-survives-a-lockout.spec.ts`) — the same question against the real wall client — has never been executed against a live server.
- **T003's counterfactual** (inverting SC-1, observing it fail, reverting) has never been executed against a live server.

**Before merging, the orchestrator will read the CI logs for both named jobs**, not their aggregate conclusion — `develop` has no required status checks, so this reading is the actual gate:

- `integration tests (Docker)` — read for `LockoutSessionSurvivalIntegrationTests`'s six facts by name, above all `A_locked_out_account_can_still_spend_its_refresh_token`. Download the run's `integration-test-results` artifact and grep the `.trx` for the exact test name and its `outcome`, the same way #2428's PR was confirmed (a `Passed!` summary line alone does not name which tests ran).
- `e2e (Playwright, full stack)` — read for `wall-survives-a-lockout.spec.ts`'s one case by name.

**If SC-1 or SC-6 comes back red, that is the severe finding (per spec.md's A3), and per this task's own instruction the response is: file a follow-up issue carrying the verbatim CI evidence, label it the way #2510 is, ship the tests asserting the observed (not the predicted) behaviour with a doc-comment stating plainly that the assertion now encodes a tracked defect — and do not edit the realm or pick a mitigation, since ADR-0144 forbids the autonomous lane from weakening or strengthening a security control on its own judgement.** If both come back green, the predicted (benign) answer is confirmed and spec 207's open question is closed in place (T004) with a pointer to this file.

T003's live counterfactual will be read from the same `integration tests (Docker)` job's log/artifact — a `Passed` outcome for `A_disabled_account_cannot_spend_its_refresh_token` (SC-4) is what confirms the instrument that produced SC-1's result is capable of registering a refusal at all, closing the "checks its own input" risk this fact exists to rule out.

**Keycloak version, per spec.md's G1**: the image is unpinned (`AppHost.cs`), so the running version must be recorded from whatever CI's boot actually reports, not assumed from the source line read during phase 1's research (Keycloak 26.3.0 was the version read for the `TokenManager`/`AuthenticationProcessor` source analysis in `plan.md`; the live version may differ and the live behaviour is what settles the question regardless).

Latency: **N/A** — this is an identity/auth investigation off constitution §IV's six legs; no leg is touched or claimed.
