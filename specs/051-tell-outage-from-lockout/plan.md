# Implementation Plan: A dark wall says which failure it is, and comes back on its own when it can

**Branch**: `051-tell-outage-from-lockout` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: [spec.md](./spec.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/kiosk-failure-states.md](./contracts/kiosk-failure-states.md) · **Quickstart**: [quickstart.md](./quickstart.md)

---

## Summary

A kiosk that cannot renew its grant currently shows one of two unhelpful things:
*"Sign-in failed / Failed to fetch"* with a button nobody is there to press, or —
when its account has been shut out — **the identity provider's own login form**,
soliciting credentials from anyone walking past a factory wall.

The failure that resolves by itself is the one that never retries. Measured: 90
seconds of a healthy provider with the wall still dark.

**The approach**: classify the renewal failure at the point where its cause still
exists, then retry the recoverable case unattended and refuse to redirect the
terminal one. One line currently destroys the cause —
`.catch(() => false)` in `useSessionExpiry.ts` — and that line is the foundation
of the whole change.

**US3 is included rather than deferred.** Jitter is one multiplication inside a
schedule that has to exist anyway, and deferring it means knowingly shipping
twenty screens that reconnect in lockstep against a service that just came back.

---

## Technical Context

**Language/Version**: TypeScript 5.x, React 19

**Primary Dependencies**: `oidc-client-ts` 3.5.0, `react-oidc-context` 3.3.1 — both already present; **nothing new is added**

**Storage**: none. One existing `sessionStorage` key (the redirect guard); retry state is in memory and deliberately not persisted

**Testing**: Vitest for the classification rule and the schedule; Playwright for the screens and unattended recovery

**Target Platform**: `apps/kiosk-web` — a browser on a wall-mounted display, no keyboard, nobody in front of it

**Project Type**: frontend only. **No C#, no backend, no realm change, no scope change, no message contract**

**Performance Goals**: recovery within **2 minutes** of the provider becoming healthy (SC-001)

**Constraints**: retry ceiling **must** stay under ~60 s or SC-001 breaks silently; no credential prompt may appear on a refused screen

**Scale/Scope**: 20 screens per wall (constitution §Availability). **Twenty will not be exercised** — see quickstart

---

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **§Availability** — 24/7, a wall of 20 kiosks comes up unattended | This is the feature. It moves the target forward and **does not close it**: the ten-hour ceiling still drops a screen roughly twice a day (issue 1989, blocked on issue 1992). The record must not claim otherwise. |
| **§Security** — token-bound short-lived credentials, no long-lived secrets in browsers | Untouched. No scope widens, no grant lengthens, no credential is stored that was not already. |
| **§IV latency budget** | Not on the event-to-overlay path. No leg affected. |
| **§VII observability** | The classified cause is logged through the existing resilience log; no new sink. |
| **DDD / value objects / no cross-context references** | No domain code, no context boundary crossed. |
| **ADR-0065 coverage gates** | Cover Domain, Application and Shared. **None are touched, so there is no coverage gate to cite as evidence** — and citing one would mislead. |
| **ADR-0030 commits, ADR-0087 rebase-only** | Followed. |

**No ADR contradicts this feature** — checked explicitly (research §R0), including
the near-miss where ADR-0113 forbids automatic retry of *concurrency conflicts*,
which is a different failure with the opposite correct response. **No amendment
gate applies.** A new ADR is expected at Phase 5 to record the classification rule
and the retry bound.

---

## Project Structure

### Documentation

```text
specs/051-tell-outage-from-lockout/
├── spec.md
├── plan.md                            # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/kiosk-failure-states.md
└── checklists/requirements.md
```

### Source

```text
apps/kiosk-web/src/
  app/
    identityFailure.ts        NEW — the classification rule (data-model §1)
    retrySchedule.ts          NEW — delay, growth, ceiling, jitter (data-model §2)
    useSessionExpiry.ts       CHANGED — stop destroying the cause; own the verdict
  features/auth/
    ReconnectingScreen.tsx    NEW — C1
    NotAuthorizedScreen.tsx   NEW — C2
  App.tsx                     CHANGED — render by verdict
```

**`apps/kiosk-web`, not `apps/shared`** (research §R6). The management console has
a person in front of it; unattended retry is a property of an unattended screen.
One consumer, so moving it to shared would be speculative generality.

---

## Approach

### 1. Stop destroying the cause

```ts
setSessionRenewer(() => auth.signinSilent().then((u) => u !== null).catch(() => false));
//                                                                   ^^^^^^^^^^^^^^^^
```

The renewer must keep returning a boolean — the gateway depends on it — but the
rejection is classified before it is discarded. This is the smallest change that
makes everything else possible.

### 2. Classify by code, never by class

The ordering in data-model §1 is load-bearing: **the code check comes before any
class check**. Branching on `ErrorResponse` first turns an overloaded identity
service into a wall announcing it has been revoked — and it would pass every test
that only ever stops the provider.

### 3. Decide before redirecting

A `refused` verdict **skips `signinRedirect` entirely**, which is what keeps the
provider's login form off the wall. This is FR-007 met directly rather than
through its escape hatch, and it is possible only because the cause arrives at
`signinSilent`'s rejection (research §R1, verified against a running provider).

### 4. Keep the 60-second guard, narrowed

One verdict, two disjoint sources: a classified cause decides when it exists; the
guard decides only where no error object exists at all — a completed redirect
that landed unauthenticated. They cannot disagree because they never both speak.

`interactive` stays exactly as it is. It is the ten-hour ceiling arriving, and
announcing that as *revoked* would send someone to re-commission a screen that
needed a sign-in.

### 5. Retry, and keep retrying

2 s doubling to a **30 s ceiling**, ±30% jitter, **no bound**. A screen that gives
up needs a person, which is the failure being removed. Worst-case wait after the
provider recovers is `30 × 1.3 = 39 s` plus a round-trip — inside SC-001's two
minutes with room to spare.

---

## Done, per story — stated before any code

| Story | Verifiable criterion |
|---|---|
| **US1** | Provider stopped → screen shows *Reconnecting* → provider restarted → **the wall returns with nothing touched, within 2 minutes**, showing the layout it had. |
| **US2** | A disabled account produces a screen stating the screen is not authorized, with **no username or password field anywhere on it**, and no retry timer. |
| **US3** | Several screens recovering from one outage make their attempts at **measurably different times**. |

None of these is satisfied by "the screen shows a nicer message".

---

## What the checks will and will not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| A recoverable failure retries | a timer test | the screen saying "Reconnecting" |
| The wall comes back untouched | provider down → up, no interaction | a manual button working |
| A refused screen shows no prompt | asserting the **absence** of password fields | the app not erroring |
| An overloaded provider is recoverable | a stubbed `server_error` | stopping the provider, which exercises a different branch entirely |
| Screens do not arrive together | comparing attempt times | jitter existing in the code |
| **A wall stays up over days** | **nothing** | seconds of one screen |
| **Twenty screens** | **nothing** | a handful |
| **A real network partition** | **nothing** | an aborted request |
| **Anything in production** | **nothing** | there is no production deployment (ADR-0130) |

**The gap that matters**: unattended recovery is a property of twenty screens
over days, and every automated check here watches one screen for seconds. The
verification note must say so rather than let a green suite imply a wall was
watched.

---

## Risks

1. **Classifying on the error's class instead of its code.** Turns the most
   likely real outage — an overloaded provider — into a wall of screens
   announcing revocation. Mitigated by ordering the rule, and by a test that
   stubs `server_error` specifically rather than only stopping the provider.

2. **A route interception that matches nothing.** `signinSilent` may run in a
   hidden iframe. A test that intercepts nothing looks exactly like a test that
   passes. Every interception must assert it fired.

3. **The ceiling drifting above SC-001.** The retry ceiling and the two-minute
   criterion live in different documents. A later "let's be gentler, make it 90
   seconds" breaks SC-001 with every test still green. The link is stated in
   research §R4, data-model §2 and here.

4. **`refused` swallowing `interactive`.** Conflating them makes a twice-daily
   ceiling drop-out look like a revoked screen.

5. **Reading this feature as closing §Availability.** It does not. The ceiling is
   the more frequent failure and is untouched.

---

## Out of scope, filed rather than implied

The session ceiling (issue 1989, blocked on issue 1992); per-device identity
(issues 1987, 1988); crash recovery; stream and wall-content outages;
`management-web`.
