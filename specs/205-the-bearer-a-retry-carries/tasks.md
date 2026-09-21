# Tasks — Spec 205, the bearer a retry carries

**Spec:** `specs/205-the-bearer-a-retry-carries/spec.md` ·
**Plan:** `specs/205-the-bearer-a-retry-carries/plan.md` ·
**Issue:** #2301 · **Branch:** `2301-stale-bearer-retry` · **Base:** `origin/develop` @ `1b4b9956`

**Phase-4a colour: RED for every task below.** Both stories change behaviour, and
both new assertions were **observed failing at phase 1** (transcripts in `spec.md`
§*Observed*). A test that arrives green is a phase-4 failure, not a shortcut
(ADR-0139, CLAUDE.md §*Phase 4a has two colours*). `test-writer` runs the tests,
returns the **verbatim** failure output, and that output is quoted in the PR body.
The engineer receives it as its brief and **may not edit the tests to pass** —
except as scoped in the next section.

**No new ADR, no constitution amendment, no task issues.** Per CLAUDE.md
§Workflow the phase-3 gate is *the feature's issue on Project #13* — #2301 is
already there, status **Todo**, labelled `agent:ready`, without `agent:blocked`
(verified at dispatch, 2026-09-21). `/speckit-taskstoissues` is **not** to be run.

**#2236 must not be referenced with a closing keyword.** `spec.md` §*Out of scope*
gives five discriminators showing it is a different defect. Mention it, do not
close it.

---

## Which test edits are permitted

Three existing assertions encode the **old** contract (`SessionRenewer` resolves a
boolean). Changing them is a contract change decided here at phase 3 — **not** an
adjustment to make a red test pass. They are listed exhaustively so the boundary
is not a judgement call:

| File:line | Today | After |
|---|---|---|
| `apps/kiosk-web/src/app/useSessionExpiry.test.ts:66` | `.resolves.toBe(false)` | `.resolves.toBeUndefined()` |
| `apps/kiosk-web/src/app/useSessionExpiry.test.ts:76` | `.resolves.toBe(true)` | `.resolves.toBe('a')` — the token its own `signinSilent` stub already returns (`:71`) |
| `apps/kiosk-web/src/app/useSessionExpiry.test.ts:82` | `.resolves.toBe(false)` | `.resolves.toBeUndefined()` |
| `apps/management-web/src/App.test.tsx:213` | `.resolves.toBe(true)` | `.resolves.toBe('fresh')` — the token its own stub already returns (`:212`) |
| `apps/management-web/src/App.test.tsx:216` | `.resolves.toBe(false)` | `.resolves.toBeUndefined()` |

Plus the two type annotations that name the old shape
(`useSessionExpiry.test.ts:6,9` and `App.test.tsx:14,22`), and the doc comment at
`useSessionExpiry.test.ts:46-57`, which says *"The gateway's 401 path depends on
this boolean"* and after this change depends on the token.

**Any other edit to an existing assertion is a block, not a fix.**

---

## Parallelism (ADR-0109)

| Group | Tasks | Why |
|---|---|---|
| **Foundational** | **T001** | `apps/shared/src/api/gateway.ts`'s `SessionRenewer` type is what every other task is shaped by. It blocks the rest: until its red tests exist, the call-site tasks have nothing to satisfy. |
| A | T002, T003, T004 | Disjoint files: `apps/shared/src/api/gateway.test.ts` (T002) vs `apps/management-web/src/app/staleBearerRetry.test.tsx` + `package.json` (T003) vs `apps/kiosk-web/src/app/useSessionExpiry.test.ts` + `apps/management-web/src/App.test.tsx` (T004). `[P]` — safe concurrently. |
| B | T005 | `apps/shared/src/api/gateway.ts` alone. Must land before T006/T007 typecheck. |
| C | T006, T007 | Disjoint files: `apps/management-web/src/App.tsx` (T006) vs `apps/kiosk-web/src/app/useSessionExpiry.ts` (T007). `[P]` once T005 exists. |
| D | T008 | Whole-workspace verification. Last. |

**Recommended dispatch:** T001 → (T002 ∥ T003 ∥ T004) → T005 → (T006 ∥ T007) → T008.

**US1 is not splittable.** `SessionRenewer`'s type change breaks both registration
sites at compile time, so T005, T006 and T007 land together or the workspace does
not typecheck. US2 (T003) *is* separable — if the slice has to narrow, drop T003
and file it as its own issue rather than half-doing it. Dropping it costs the
proof that the mechanism is the one we think it is, which is the whole reason
#2301 was filed with its own uncertainty label; prefer not to.

---

## US1 (P1) — A renewal that succeeds keeps the session

### Phase 4a — RED (`test-writer`)

**[T001] [US1] Restate the renewal contract in `gateway.test.ts`** — *foundational*
`apps/shared/src/api/gateway.test.ts`

The existing six tests register `vi.fn(() => Promise.resolve(true|false))`
(`:49, 64, 79, 94, 108, 120-125`). Change each to resolve a **token string** or
`undefined`, matching the new `SessionRenewer`. Keep every existing assertion
about call counts, result payloads and `onSessionExpired` exactly as it is —
those are behaviour this change must not move.

This task is red for a mundane reason and that is fine: the module still types
`SessionRenewer` as `() => Promise<boolean>`, so the file will not compile. Report
the verbatim failure.

---

**[T002] [P] [US1] The header assertions — the tests that would have caught this**
`apps/shared/src/api/gateway.test.ts`

Add a helper next to the existing `ok`/`unauthorized`/`serverError` factories:

```ts
const authorizationOf = (call: unknown[]): string | null =>
  (call[0] as Request).headers.get('authorization');
```

**#2301's suggested `fetchMock.mock.calls[1][1].headers` would throw**, and this
was checked rather than assumed: RTK Query 2.12.0 builds a `Request` and calls
`fetchFn(request)` with **one** argument
(`@reduxjs/toolkit/dist/query/rtk-query.modern.mjs:226,233`), so `calls[n][1]` is
`undefined` and the header lives on `calls[n][0]`. Assert the shape of the first
recorded call once, so a future RTK upgrade that changes it fails loudly rather
than silently reading `null` from the wrong place.

Then, in the file's existing style (Vitest, sentence-style `it(...)` descriptions
with the story in prose):

1. **`Renews once on 401 and retries with the token the renewal minted`** — extend
   the existing test at `:47-60`. Register an access-token provider returning
   `'old-token'`, a renewer resolving `'new-token'`, `fetch` 401-then-200. Assert
   `authorizationOf(fetchMock.mock.calls[0])` is `'Bearer old-token'` **and**
   `authorizationOf(fetchMock.mock.calls[1])` is `'Bearer new-token'`. Keep the
   existing `renew`/`fetch` call counts, `result.data` and `expired` assertions.
   **This is the assertion #2301 asks for, and it is red today.**
2. **`Shares one renewal between concurrent 401s and retries both with the same
   new token`** — extend `:118-145`. Both retries (`calls[2]`, `calls[3]`) carry
   `'Bearer new-token'`. The existing test already waits on a condition with
   `vi.waitFor` (ADR-0150) — keep that; do not introduce a yield count.
3. **`A renewal that yields no token is a failed renewal`** — renewer resolves
   `undefined`; assert `fetch` called **once**, `expired` once, result 401.
4. **`A renewal that yields an empty token is a failed renewal`** — renewer
   resolves `''`; same assertions. (Guards the `isUsable` boundary, not just the
   `undefined` one.)
5. **`A renewal that rejects is a failed renewal`** — renewer rejects; assert
   `fetch` called once, `expired` once. This also pins that `renewalInFlight` is
   cleared, so a later 401 renews again rather than reusing a settled promise.
6. **`A retry the server still refuses expires the session`** — extend `:77-90`
   with the header assertion that the retry carried `'Bearer new-token'`, so the
   escalation is provably *not* caused by a stale credential.
7. **`A request with no registered token carries no Authorization header`** —
   assert `authorizationOf(calls[0])` is `null`.

`setAccessTokenProvider` is currently not imported by this file (`:8`) — add it to
the destructured import.

**Red expected on 1, 2, 6 for the defect; on 3, 4, 5 for the missing behaviour.**

---

**[T004] [P] [US1] Move the two registration sites' own tests onto the new contract**
`apps/kiosk-web/src/app/useSessionExpiry.test.ts`, `apps/management-web/src/App.test.tsx`

Apply exactly the table in §*Which test edits are permitted*, and nothing else.
Update the two `() => Promise<boolean>` annotations in each file and the
`useSessionExpiry.test.ts:46-57` doc comment, which should now say the gateway's
401 path depends on the **token** this resolves, and why (one sentence, citing
#2301 — CLAUDE.md §*No drive-by comments*: the why, not the what).

These two files are the only place the **real** `AuthGate` / `useSessionExpiry`
registration is observed, so they are the guard against the US2 mirror drifting
from the original. Say so in the comment.

Red expected: the annotations will not compile against the old module type until
T005, and the value assertions fail until T006/T007.

---

### Phase 4b — implementation (`frontend-engineer`)

**[T005] [US1] The gateway retries with the token the renewal returned** — *blocks T006, T007*
`apps/shared/src/api/gateway.ts`

Apply `plan.md` §*US1 — the change* verbatim: `SessionRenewer` resolves
`string | undefined`; `renewalInFlight` and `renewSessionOnce` carry the token;
`isUsable` decides both the header and the `renew-success` / `renew-failure`
spelling; `gatewayQueryFor(route, bearer)` factors `prepareHeaders`; the retry is
built against the renewed token.

Constraints:

- **Do not touch `setAccessTokenProvider` or its type.** The first attempt keeps
  reading the render-written slot; only the retry names its own token.
- **Do not add a `logResilienceEvent` category or event name.** Only what
  `renew-success` means is tightened.
- Keep the existing comment block at `:52-56` (spec 011 FR-011/012) and extend it
  with the scheduler reasoning from `plan.md` — *why* a render-written slot cannot
  serve the retry. That is the non-obvious why this file needs on record.
- No `ConfigureAwait`-equivalent churn, no reordering of the non-401 fast paths,
  no change to what the caller receives in any existing scenario.

**Done when:** T001 and T002 are green and every previously-existing assertion in
`gateway.test.ts` still holds.

---

**[T006] [P] [US1] management-web's renewer returns its token**
`apps/management-web/src/App.tsx`

`:27-32` — `.then((user) => user?.access_token)`, `.catch(() => undefined)`.
Nothing else in the file changes; the comment at `:20-25` still describes why
registration happens during render and stays.

**Done when:** `App.test.tsx:205-217` is green with the token assertions from T004.

---

**[T007] [P] [US1] kiosk-web's renewer returns its token**
`apps/kiosk-web/src/app/useSessionExpiry.ts`

`:177-190` — map to `user?.access_token` / `undefined`, per `plan.md`.

Constraints:

- **`renewalInFlight.current = false` must stay on both branches, in the same
  positions.** It is bookkeeping other screens read; the mapping change must not
  move either assignment.
- `beginReauthentication(cause)` on rejection stays.
- Record in one line, at the `.then`, that a user without an `access_token` now
  counts as a failed renewal (spec.md A2) — a deliberate change, not a slip.

**Done when:** `useSessionExpiry.test.ts:59-83` is green with the token assertions
from T004.

---

## US2 (P2) — The race stays fixed, proven against the real provider

### Phase 4a — RED (`test-writer`)

**[T003] [P] [US2] The ordering test, against the real `AuthProvider`**
`apps/management-web/src/app/staleBearerRetry.test.tsx` (new) ·
`apps/management-web/package.json`

Add `"oidc-client-ts": "3.5.0"` to management-web's `devDependencies` — the exact
string kiosk-web uses — and run `pnpm install` so `pnpm-lock.yaml` is committed
with the change.

Write **one** test, shaped exactly as `plan.md` §*US2* describes, which was run at
phase 1 and observed failing:

- Real `UserManager` (`authority`/`client_id`/`redirect_uri` may be fictional —
  no network is reached), `WebStorageStateStore` over `window.sessionStorage`,
  pre-loaded with a user carrying `OLD-TOKEN`.
- `manager.signinSilent` replaced on the instance by
  `await manager.storeUser(newUser); await manager.events.load(newUser); return newUser;`.
  **Comment must cite `oidc-client-ts@3.5.0` `_useRefreshToken` (dist
  `:3208-3211`) and `_signin` (`:3376-3378`) as the two library paths this
  mirrors**, and state that the *only* reason it is stubbed is the network round
  trip — so a reader can check the fidelity claim rather than take it.
- A gate component calling the same three setters with the same expressions as
  `App.tsx:26-33`, rendered inside the real `<AuthProvider userManager={manager}>`.
  Comment must name `App.tsx:26-33` as the original and `App.test.tsx:205-217` as
  the guard that keeps the mirror honest.
- **This file must not mock `react-oidc-context`.** `App.test.tsx:32-42` does, and
  that is precisely why it cannot see this ordering — say so in a comment.
- `fetch` stubbed 401-then-200; read the header off the recorded `Request` as in
  T002.
- Assert: the retry carries `Bearer NEW-TOKEN`; `onSessionExpired` was not called.
- Settle the initial render by **waiting on a condition** — `waitFor` until the
  rendered token reads `OLD-TOKEN` — never a fixed number of yields (ADR-0150,
  constitution §Testing). The phase-1 experiment used two `Promise.resolve()`
  yields; that is a defect to fix on the way in, not a pattern to copy.

**Verbatim failure observed at phase 1, to be reproduced:**

```
FIRST  AUTH Bearer OLD-TOKEN
RETRY  AUTH Bearer OLD-TOKEN
RENDERED AFTER NEW-TOKEN
AssertionError: expected 'Bearer OLD-TOKEN' to be 'Bearer NEW-TOKEN'
```

**Goes green with T005 + T006. No separate implementation task.**

---

## Phase 5 / verification

**[T008] [US1+US2] Workspace verification and the counterfactual**

1. `pnpm -r --filter "./apps/**" test` · `pnpm lint` · `pnpm typecheck` ·
   `pnpm format:check` — all clean.
2. **Re-run #2301's own counterfactual against the fixed tree.** Replace
   `gateway.ts`'s first-attempt token with a hard-coded
   `'STALE-TOKEN-FROM-BEFORE-RENEWAL'` and confirm `gateway.test.ts` now **fails**
   — the blindness is closed, proved by constructing what the new tests claim to
   catch (MEMORY *"prove a guard by counterfactual"*). Revert.
3. Record in the verification note: which legs of §IV were touched (**none** —
   `gateway.ts:14-15`), the counterfactual result, and the verbatim red output
   from T002 and T003.
4. Phase 5 may additionally observe the path against the running AppHost
   (`spec.md` §*Independent end-to-end test procedure*, step 5). It is worth doing
   for the *frequency* figure; it is **not** a gate, because the mechanism is
   client-side scheduler ordering and is already observed.

---

## Traceability

| Requirement | Scenario | Tasks |
|---|---|---|
| Retry carries the renewal's token | US1 happy | T002.1, T005, T006, T007, T003 |
| One renewal shared by concurrent 401s, all retried with it | US1 concurrency | T002.2, T005 |
| A renewal with no usable token is a failure | US1 degenerate ×2 | T002.3, T002.4, T005, T007 |
| A rejected renewal is a failure and does not wedge the in-flight promise | US1 degenerate | T002.5, T005 |
| A genuinely refused retry still expires the session | US1 auth | T002.6, T005 |
| No token registered ⇒ no `Authorization` header | US1 auth | T002.7, T005 |
| The React re-render being late does not reach the retry | US2 | T003, T005, T006 |
| Registration sites observed on the real contract | US1 | T004, T006, T007 |
