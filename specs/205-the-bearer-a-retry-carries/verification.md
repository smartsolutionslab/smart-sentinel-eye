# Verification — Spec 205, the bearer a retry carries (#2301)

## Red → green, verbatim

Phase 4a (`test-writer`), against unpatched code — the header-assertion facts
that would have caught the defect, red for the actual reason (a compile
failure until the contract changes; a runtime assertion failure for the
three facts that exercise the live bug):

```
Failed: Renews once on 401 and retries with the token the renewal minted
  AssertionError: expected 'Bearer old-token' to be 'Bearer new-token'

Failed: A retry the server still refuses expires the session
  AssertionError: expected 'Bearer old-token' to be 'Bearer new-token'

Failed: Shares one renewal between concurrent 401s and retries both with the same new token
  AssertionError: expected 'Bearer old-token' to be 'Bearer new-token'
```

And the decisive evidence — the same defect reproduced against the **real,
unmocked** `react-oidc-context`/`oidc-client-ts` libraries, no simulated
ordering:

```
Failed: A renewal racing a retry does not end the session
  AssertionError: expected 'Bearer OLD-TOKEN' to be 'Bearer NEW-TOKEN'
```

Full pre-fix picture: `apps/shared` 407/410 (3 failing), `apps/kiosk-web`
160/163 (3 failing), `apps/management-web` 303/305 (2 failing) —
`staleBearerRetry.test.tsx` not yet counted in that figure since it's new;
its single fact failed as quoted above.

**A gap in the original task plan, found and fixed by the implementing agent
correctly pausing rather than improvising**: `gateway.test.ts:110`'s
`vi.fn(() => Promise.reject(new Error(...)))` had a genuine, pre-existing
TypeScript inference bug — `Promise.reject<T>()`'s default `T = never` fixed
the mock's type before its later reuse (resolving a token) could typecheck
against it. Verified independently as unrelated to the `SessionRenewer`
contract change (reproduced identically against the OLD boolean type too).
Fixed directly by the orchestrator with an explicit type argument
(`Promise.reject<string | undefined>(...)`) — a compile-only change with zero
effect on any assertion.

**A second gap, found by the orchestrator's own review before dispatching
phase 4b**: `staleBearerRetry.test.tsx`'s `Gate` component mirrors
`App.tsx:26-33` as a deliberate, living invariant (per the file's own doc
comment), not a frozen snapshot — so it would have failed to *compile* once
`App.tsx`'s renewer shape changed, and this file wasn't in `tasks.md`'s
exhaustive permitted-test-edits table. Resolved by explicitly authorizing
the implementing agent to make exactly this one line's mirror-sync, and
nothing else in the file — the file's own assertions are completely
unaffected by what shape the renewer's promise resolves.

After the fix, independently re-verified by the orchestrator — not trusting
the implementing agent's own report:

```
apps/shared:          410/410 passed
apps/kiosk-web:        167/167 passed (163 at this fix's own base; 4 more
                        landed with spec 204, merged during this delivery —
                        re-verified at the current rebased tip, not quoted
                        stale, per phase-6 review)
apps/management-web:   305/305 passed
```

`staleBearerRetry.test.tsx` specifically, run in isolation: **1/1 passed** —
the retry now carries `Bearer NEW-TOKEN`, and `onSessionExpired`/`expiredCalls`
is 0. `tsc --noEmit`: clean in all three workspaces. `eslint --max-warnings 0`:
clean in all three. `pnpm format:check`: clean.

## Counterfactual proof (prove a guard by counterfactual)

Both the implementing agent and the orchestrator independently confirmed the
guard fires on the exact defect it claims to catch: temporarily hard-coding
the retry's bearer to a literal stale string instead of the renewal's own
returned token made `gateway.test.ts` fail exactly the three header-assertion
facts that exercise the defect (never the others). Reverted; `git diff`
confirmed no residual change beyond the intended fix; suite back to green.

## Two of the issue's own claims, corrected rather than assumed

1. **"Read the token from a ref" does not work.** Disproved by
   counterfactual, not argument: the existing ref pattern
   (`kiosk-web/CellPage.tsx:48-53`) is written *during* the same render the
   retry races — reading it produces the identical stale-bearer failure. That
   pattern solves effect-dependency churn (a different, real problem), not
   this one.
2. **"No assertion in the repository inspects a request's `Authorization`
   header" is false**, though the true, narrower claim is still damning:
   `apps/shared/src/streaming/WhepClient.test.ts` asserts it three times
   already, including the exact analogue for a different client. Nothing on
   the **gateway/RTK Query** path did — the precedent for the right kind of
   test was one directory away, not a new pattern this fix invented.

The issue's own second suggested fix — `userManager.getUser()` inside
`prepareHeaders` — was investigated and found not reachable as written:
`useAuth()` exposes no manager reference in this app's actual usage.

## #2236 — confirmed not the same root cause

Five discriminators, all checked against #2236's own text: trigger is DCP
tunnel-proxy idle time, not a token renewal; the transport error is a clean
connection reset, not a 401; the code path
(`WhepClient.postOffer` → MediaMTX direct) never goes through
`gatewayBaseQuery` at all; it clears on retry, where this defect's retry is
the thing that fails; and it's scoped to the local run-mode tunnel
specifically. Mentioned in this PR without a closing keyword, per spec.md's
explicit instruction.

## A finding retracted during phase 6 — recorded so it isn't rediscovered

`verification.md`'s own first draft claimed `management-web`'s `oidcConfig`
setting no `automaticSilentRenew` meant the 401-retry path was its *only*
renewal mechanism, raising the defect's severity for that app specifically.
**This was false, and both phase-6 reviews caught it independently.**
`oidc-client-ts@3.5.0` defaults `automaticSilentRenew` to `true`
(`node_modules/.pnpm/oidc-client-ts@3.5.0/.../dist/umd/oidc-client-ts.js:2522`,
independently confirmed by the orchestrator) — management-web already runs
background renewal exactly like kiosk-web, it simply never states so.
`spec.md` and `plan.md` are corrected accordingly. Recorded here as a
retraction rather than silently edited away, per this repo's own standing
lesson that a record nobody re-checked against what the system actually does
is how false claims persist.

## Latency budget

**N/A** — `gateway.ts:14-15` (checked): REST calls through the gateway are
explicitly off constitution §IV's latency-budget path. Stated rather than
omitted.

## Phase 6

`frontend-reviewer` and `security-reviewer` ran in parallel — the latter
dispatched despite `tasks.md`'s task breakdown not naming an explicit
security-review task, since this changes how every bearer token reaches
every authenticated request and how session expiry is decided, squarely
security-adjacent regardless of the "internal contract" framing. **No
blockers from either.** Both independently re-ran the full gate (typecheck,
lint, format, all three workspaces' suites) and confirmed green before
reviewing.

### Should-fix items, applied

- **A false claim in this spec's own record, caught independently by both
  reviewers**: `spec.md`, `plan.md` and an earlier draft of this file all
  claimed management-web has no background token renewal
  (`automaticSilentRenew` unset), making the 401-retry path its *only*
  renewal mechanism. `oidc-client-ts@3.5.0` defaults that setting to `true`
  — management-web already runs background renewal exactly like kiosk-web,
  it simply never states so. Corrected in all three files, with the
  retraction recorded above rather than silently edited away.
- **A load-bearing detail had no test**: `gateway.ts:110`'s
  `() => accessTokenProvider()` thunk (rather than passing the binding
  directly) exists because every real RTK client constructs its
  `gatewayBaseQuery` at module scope, at import time, long before
  `AuthGate` registers the provider — passing the binding directly would
  capture the `() => undefined` default forever. Nothing tested this.
  Added a regression test constructing the client before registration,
  proven real by counterfactual: reverting the thunk to a direct binding
  fails exactly this one test, nothing else; reverted cleanly afterward.
- **The crown-jewel test asserted the contract but not the mechanism**:
  `staleBearerRetry.test.tsx` never asserted the first call carried the
  OLD token (so the second call's NEW token proved nothing by contrast),
  and never observed the DOM at the exact instant the retry was built —
  the one moment `spec.md`'s own acceptance scenario is actually about
  ("even though React has not yet re-rendered"). Both added: the first
  call's token is now asserted, and the rendered token is sampled from
  inside the second `fetch` call's own implementation, before the retry's
  `await` resolves.
- **Three test titles in `useSessionExpiry.test.ts`** stated the opposite
  of what they asserted after the contract changed from boolean to token
  (`tasks.md`'s permitted-edits table covered the assertions and doc
  comment but not the titles) — corrected to match.
- **Test hygiene in `staleBearerRetry.test.tsx`**: the real `UserManager`
  defaulted to `automaticSilentRenew: true`, arming a background timer
  the test never stopped — set explicitly to `false`. `gateway.ts`'s
  module singletons (`setSessionRenewer`, `setOnSessionExpired`) were
  reset in only one of the file's two `describe` blocks — now reset in
  both.
- **A deliberate behavior change (spec.md A2: a user with no
  `access_token` is a failed renewal) was untested at both sites that
  implement it** — `gateway.test.ts` proved the downstream escalation but
  not the mapping itself. Added one case at each of `useSessionExpiry.ts`
  and `App.tsx`'s own test files.

Final independent re-verification after the fix round:
`apps/shared` 411/411, `apps/kiosk-web` 168/168, `apps/management-web`
305/305, typecheck clean in all three, lint clean, format clean.

### Nits, not applied

- The other mirrored line in `staleBearerRetry.test.tsx`'s `Gate`
  (`setAccessTokenProvider`) has no equivalent guard against drift the way
  the renewer expression does via `App.test.tsx`'s explicit spread. Low
  risk; not worth a new test for one line that hasn't moved since ADR-0080.
- The tightened `renew-success`/`renew-failure` log semantics (a renewal
  with no usable token now logs failure, not success) have no direct
  assertion against the `console.info` spy — the downstream behavior they
  drive (no retry, one `expired` call) is fully tested; the log string
  itself is not. Accepted as a minor observability-only gap.
