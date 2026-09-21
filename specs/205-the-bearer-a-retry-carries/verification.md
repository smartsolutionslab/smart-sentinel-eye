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
apps/kiosk-web:        163/163 passed
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

## A finding not in the original issue

`management-web`'s `oidcConfig` sets no `automaticSilentRenew` (unlike
`kiosk-web`'s, which does) — so for the **operator console**, the 401-retry
path this fix repairs was its *only* renewal mechanism at all, not one of
several. This raises the severity of the original defect for that
application specifically, beyond what the issue itself stated.

## Latency budget

**N/A** — `gateway.ts:14-15` (checked): REST calls through the gateway are
explicitly off constitution §IV's latency-budget path. Stated rather than
omitted.

## Phase 6 — pending

`/code-review`, with the scheduler-race reasoning (the new comment in
`gateway.ts`) and the `isUsable` boundary (empty-string vs. undefined vs.
rejection, all three now behave identically) as deliberate items to raise.

`/security-review` — **run, not skipped**, despite `tasks.md`'s task
breakdown not naming an explicit security-review task. This changes how
every bearer token reaches every authenticated request through the gateway,
and how session expiry is decided — squarely security-adjacent regardless of
whether the surface is framed as "internal." Dispatched per this session's
standing extra-rigor practice for authentication/session-handling changes.
