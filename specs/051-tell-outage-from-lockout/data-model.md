# Data model — 051 tell an outage from a lockout

Phase 1. No persistence, no database, no message contract. These are the shapes
that live in a browser tab and one `sessionStorage` key that already exists.

---

## 1. Identity failure verdict

The single value everything else hangs off. **One verdict, two disjoint sources**
(research §R3), so nothing can disagree with itself.

| Verdict | Means | Screen | Retries |
|---|---|---|---|
| `recoverable` | the identity service could not answer, or answered "not now" | "Reconnecting" | **yes, unattended** |
| `refused` | the identity service answered and will not accept this screen | "This screen is no longer authorized" | no |
| `interactive` | the session ended; a person must sign in | "Session expired" — **unchanged** | no |

**`interactive` is deliberately not new.** It is the ten-hour ceiling arriving
(issue 1989) and it is the most frequent failure on this screen. Folding it into
`refused` would tell someone a screen had been revoked when it had merely reached
a time limit, sending them to re-commission hardware that needed a sign-in.

### How a cause becomes a verdict

Derived from what `signinSilent()` rejects with. **Ordered**, and the order is
load-bearing — the code check must come before any class check, or an overloaded
provider reads as a revoked screen.

| # | Cause | Verdict |
|---|---|---|
| 1 | answered, code ∈ {`server_error`, `temporarily_unavailable`} | `recoverable` |
| 2 | answered, code ∈ {`invalid_grant`, `invalid_client`, `unauthorized_client`, `access_denied`, `invalid_scope`} | `refused` |
| 3 | answered, **any other code** | `recoverable` (FR-005) |
| 4 | timed out | `recoverable` |
| 5 | never answered (network) | `recoverable` |
| 6 | no error object at all — a completed redirect that landed unauthenticated | `interactive` (the existing 60 s guard) |

Rows 1 and 3 are the ones a reviewer should look at hardest. Row 1 exists because
*the provider answering is not the same as the provider refusing*. Row 3 is the
asymmetric default: a wrong `recoverable` costs one screen a request every 30 s;
a wrong `refused` costs a wall its picture.

---

## 2. Retry schedule

Held in memory for the life of the tab. Nothing is persisted — a restarted
screen should try immediately, not resume a backoff it can no longer justify.

| Field | Value | Why |
|---|---|---|
| first delay | 2 s | fast enough that a blip is invisible |
| growth | ×2 | standard, and reaches the ceiling in four attempts |
| ceiling | **30 s** | **above ~60 s silently breaks SC-001's two-minute recovery** |
| jitter | ±30% | US3: twenty screens must not arrive together |
| bound | **none — retries forever at the ceiling** | nobody is standing at the wall; a screen that gives up needs a person, which is the failure being removed |

**Worst case against SC-001**: the provider recovers immediately after an attempt
fails, so the wall waits one full interval — `30 × 1.3 = 39 s` — plus one renewal
round-trip. Inside two minutes with room to spare.

### State transitions

```
                    renewal fails
   [showing wall] ─────────────────▶ (classify)
                                        │
              ┌─────────────────────────┼──────────────────────────┐
              ▼                         ▼                          ▼
       recoverable                   refused                  interactive
     [Reconnecting]          [No longer authorized]        [Session expired]
       │        ▲                      │                          │
       │        │ wait(delay)          │ (terminal)               │ (person acts)
       │        └──────────────┘       │                          │
       │ renewal succeeds              ▼                          ▼
       ▼                         stays put until               sign-in
   [showing wall]                 re-commissioned
```

**One-way** (FR-014): `refused` never returns to `recoverable` without a
successful sign-in. A revoked screen that retried its way back to "reconnecting"
would look like it might recover, and it will not.

**A screen shut out during an outage** walks the intended path on its own: it
cannot learn it is refused while nothing answers, so it stays `recoverable`,
retries, and moves to `refused` on the first real answer.

---

## 3. What a person reads

Two audiences, and conflating them is how the current screen ends up saying
"Failed to fetch" to a factory floor.

| | On the wall | Kept for whoever is debugging |
|---|---|---|
| `recoverable` | that it is reconnecting **and that no action is needed** | the cause, the attempt count, the next attempt |
| `refused` | that the screen is no longer authorized and needs re-commissioning | the OAuth code and the provider's description |
| `interactive` | unchanged | unchanged |

**FR-010 forbids the library's own text as the headline on any of these.**
"Failed to fetch" is a browser's phrase for "the request did not leave the
building" and means nothing to the person it is shown to.

**FR-003 forbids a credential prompt on the `refused` screen.** A wall-mounted
display soliciting a username and password from passers-by is what the system
does today, and it is the worst property of the current behaviour.

---

## 4. What is deliberately not modelled

- **The provider's health as a separate fact.** A second source of truth that can
  disagree with the renewal that actually matters.
- **Persisted retry state.** See above.
- **A per-screen identity.** Issues 1987 and 1988.
- **Anything about the session ceiling.** Issue 1989, blocked on issue 1992.
