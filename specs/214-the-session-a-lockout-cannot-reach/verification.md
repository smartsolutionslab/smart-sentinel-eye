# Verification — Spec 214 (#2509)

## Phase 4a — CHARACTERISATION, independently re-confirmed at the build level

This delivery adds tests only; there is no production code (Phase 4b: skipped — investigation delivers tests and a finding, not a fix). Independently re-verified:

- `dotnet build tests/Integration.Tests -c Release` — clean, 0 errors, only pre-existing advisory warnings unrelated to any file this spec touches.
- `dotnet test tests/Integration.Tests --list-tests --filter FullyQualifiedName~LockoutSessionSurvivalIntegrationTests` — all six facts discovered by name (SC-3, SC-1, SC-2, SC-5, the malformed-token bad-request case, and SC-4).
- `pnpm typecheck:e2e`, `npx eslint e2e/wall-survives-a-lockout.spec.ts --max-warnings 0`, `npx playwright test --list e2e/wall-survives-a-lockout.spec.ts` — all clean; the one new e2e case discovered by name in the `wall` project.
- `git diff --stat` against `origin/develop` confirms `BruteForceLockoutIntegrationTests.cs`, `RealmProbe.cs`, and `e2e/wall-survives-a-process-death.spec.ts` are all untouched — this delivery adds two new files, widens nothing.

Read the full `LockoutSessionSurvivalIntegrationTests.cs` and `wall-survives-a-lockout.spec.ts` myself, line by line, including their secrets discipline (status/`error`/token-presence only, never token text or a raw response body in a failure message — both files hold this throughout) and their teardown guarantees (throwaway probe users deleted in `finally`; `wall-munich`'s attack-detection record cleared in a `finally` that runs on any failure, not just success).

**T003's counterfactual is proven a different, stronger way than originally planned.** The plan called for temporarily inverting SC-1's own assertion and observing it fail; that mechanical exercise was never performed, locally or otherwise. What CI's run actually supplies instead is SC-4 itself passing — a genuinely different mechanism (a disabled account, not a locked one) producing a real refusal that the same instrument correctly detects. That is stronger evidence than a synthetic inversion: it shows the instrument catches an actual red case, not only a deliberately-broken copy of the green one. See Phase 5 below.

## Phase 5 — the live observation: OBTAINED, via CI, benign branch confirmed

Free RAM measured 3.2GB throughout this delivery on this machine, consistent with the resource pressure already documented delivering #2432, #2430, and #2428 earlier today — no local Aspire stack was ever available (`mcp__aspire__list_apphosts` returned empty every time it was checked). The live observation was deferred to CI, the same pattern already confirmed correct three times today, and PR #2529's CI run (`35701594957`) supplied it.

**Read directly from the `integration tests (Docker)` job's `integration-test-results` artifact (`.trx`), not the job's aggregate `Passed!` summary line** — every one of `LockoutSessionSurvivalIntegrationTests`'s six facts, by exact `testName` and `outcome`:

```
A_fresh_account_is_issued_both_an_access_token_and_a_refresh_token           outcome="Passed"
A_locked_out_account_can_still_spend_its_refresh_token                       outcome="Passed"   ← SC-1, the observation
The_locked_out_account_is_refused_its_correct_password_in_the_same_window    outcome="Passed"
A_second_account_is_unaffected_while_the_first_is_locked                     outcome="Passed"
A_disabled_account_cannot_spend_its_refresh_token                            outcome="Passed"   ← SC-4, the counterfactual
A_malformed_refresh_token_is_refused_without_moving_the_failure_counter      outcome="Passed"
```

**Read directly from the `e2e (Playwright, full stack)` job's own log**, the one named case:

```
✓ [wall] › e2e/wall-survives-a-lockout.spec.ts:167:7 › A wall survives a lockout (spec 214 US2, SC-6)
    › a running wall display keeps its wall while wall-munich is locked out (2.4s)
```

**This is the benign branch, observed live rather than only predicted.** A locked-out account's refresh token still mints a fresh access token — at the raw API level (SC-1) and through a real wall client holding a real offline grant on `kiosk-wall` (SC-6). SC-4's own `Passed` outcome closes the "checks its own input" risk phase 6 flagged: the instrument that produced SC-1's green is independently confirmed capable of registering a refusal (against a disabled account) rather than being structurally incapable of ever failing.

Per this note's own pre-committed branching: **the benign branch merges.** T004 (amending spec 207's open question in place, with a pointer to this file) is done as part of this same commit.

**Not the harmless branch, said plainly rather than left implied:** every operator account and every `wall-*` account remains lockable out of *new* sign-ins by an unauthenticated party, at two `POST`s per minute, with no per-account exemption — that risk is unresolved by this delivery and remains #2510's and #2488's open territory.

**Keycloak version — not captured from this CI run's own logs.** `spec.md`'s G1 asked for the running version to come from CI's actual boot rather than the phase-1 source read; the image-pull/startup banner naming it was not found in either job's log text (Docker layer caching may have skipped a fresh pull banner entirely). Recorded honestly as uncaptured rather than backfilled from the phase-1 research figure, which was never precise past "the 26.x line" in the first place (see the correction earlier in this file's history). The live behaviour observed above does not depend on knowing the exact point version.

Latency: **N/A** — this is an identity/auth investigation off constitution §IV's six legs; no leg is touched or claimed.
