# Verification — 052 a wall past its ceiling

Phase 5.

---

## 0. What shipped

A wall display signs in as an account nobody else is, holding a grant that
outlives the session ceiling which drops a screen roughly twice a day. That
grant is **read-only by construction**, because scopes belong to clients and a
wall display uses its own.

**The containment landed first.** The privilege is taken off every account this
system creates, so the widening is real rather than clerical. That ordering is
the only structural difference between this attempt and the one withdrawn.

---

## 1. Automated checks

| Check | Result |
|---|---|
| `pnpm format:check`, lint, typecheck | pass |
| `apps/shared` unit | **142 pass** |
| `apps/kiosk-web` unit | **130 pass** |
| `apps/management-web` unit | **215 pass** — verified separately; see §6 |
| Architecture tests | **101 pass** |
| Identity Application unit | **51 pass** |
| Integration (Aspire fixture) | **4 pass** — the containment, asked of the provider |
| Playwright `wall` | **10 pass** |
| Playwright `kiosk` | **16 pass** — the ordinary kiosk, unchanged |
| Full solution build | succeeds |

Read on exit codes, and the commit is gated on them. That distinction is here
because it was got wrong twice in recent features.

**Coverage gates apply here and may be cited** — Identity's Application layer is
touched, so ADR-0065's ≥80% Application threshold is live. The last two specs
correctly reported that no gate applied; this one does, and the difference is
worth stating so nobody copies the wrong line.

---

## 2. Demonstrated against the running provider

**Never against the realm file.** That distinction is the entire feature: spec
050's guard read the file, stayed green for its whole life, and the claim it
stood for was false throughout.

| Claim | Result |
|---|---|
| A kiosk enrolled at runtime holds no long-lived privilege | **pass** |
| **Control**: an account created directly *does* hold it | **pass** — without this the row above proves nothing |
| An operator holds none | **pass** |
| A wall display holds it | **pass** — or the feature would be impossible and every check green |
| The wall token names `kiosk-wall` | **pass** |
| It carries the five read scopes and **no write scope** | **pass** — read out of the token, not from a list |
| Its refresh token is type `Offline` with **no expiry** | **pass** — decoded, not counted |
| It opens a wall | **pass** — signing in and rendering are different claims |
| Every write it attempts is refused | **pass** — 401/403, never 404 |
| It cannot read another fab | **pass** |
| Withdrawing one screen leaves a sibling running | **pass**, with a control |
| The ordinary kiosk is unchanged | **pass** — 16 tests untouched |

---

## 3. What was *not* demonstrated

- **Twenty screens.** Four is the most ever exercised, once, in spec 051. The
  constitution's number is twenty.
- **A real power cut.** A reload is not a power cut.
- **Ten hours in production.** The grant's *type* is what was asserted — the
  property that makes ten hours survivable — not ten hours of elapsed time.
- **That an account created by hand is contained.** It is not, deliberately;
  see §5.
- **That anything rotates a wall-display credential.** Nothing does.
- **Anything about production.** There is none (ADR-0130), and `deploy/`
  provisions no realm — so whoever builds one must carry the wall accounts, the
  wall client **and** the containment. Having the client without the containment
  is worse than having neither.

**This does not discharge §Availability.**

---

## 4. Three defects only running it found, and one only booting found

Every one was invisible to a static check, and two made a working sign-in look
like a broken provider.

| # | What was wrong | How it presented |
|---|---|---|
| 1 | `vite.config` hardcoded the port with `strictPort`, ignoring the one the host injects | the second instance exited at once and was **reported as running**, because the process had started |
| 2 | The wall client did not name its own origin | with `webOrigins: "+"`, allowed origins come from redirect URIs — sign-in completed, then the token exchange was blocked by CORS. **A wildcard entry was present and did not help** |
| 3 | The gateway did not allow the new origin | a wall display signed in perfectly and could not load a single layout |
| 4 | A **733-character** client description | the provider stores it in a 255-character column, so the **entire realm import failed** and nothing started — the error naming a database column, not the file |

**Number four is the one worth dwelling on**: that limit is already written down
in this project's notes, and it still got past me, in a description written to
explain a design. There is now a test for it, confirmed to fail on exactly the
description that broke the import.

**Number one is the most instructive.** "Reported as running" is the failure
mode: the host saw a process start and said so, while nothing was listening. A
check that asked the orchestrator would have agreed with it.

---

## 5. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| An enrolled kiosk holds nothing | asking the provider | the architecture test, which reads the file |
| The provider grants it by default | the control | reasoning about defaults |
| The grant outlives a session | decoding its type | asserting a token exists |
| A wall display cannot write | the scope's **absence from the issued token** | three endpoints returning 403 |
| A strip failure cannot pass silently | inducing one | the happy path |
| The sweep is safe on people | asserting on the operator | the sweep not crashing |
| Operators gained nothing | signing one in | not having edited them |
| **Twenty screens** | **nothing** | four, once |
| **A real power cut** | **nothing** | a reload |
| **Ten hours in production** | **nothing** | a grant that carries no expiry |
| **A hand-created account** | **nothing — it is not covered** | this feature, which excludes it |

### The residual gap, priced

Closing it means narrowing the provider's default set, which needs
realm-management authority. Measured one permission at a time: nothing the
identity service holds is enough, and the permission that works is authority
over session lifetimes, roles and authentication flows alike — **broader than
the privilege it would contain**, for a case this system does not drive.

Filed as an issue, named in the spec (FR-002a), and the requirement was narrowed
**in the open**. Rewriting a requirement to match what was built is the move this
repository has an ADR against; the difference is that this gap is named, priced
and tracked rather than quietly disappearing.

---

## 6. The environment, and one thing that is not this feature's fault

- The realm changed, so the provider's volume was deleted and the realm
  re-imported from the file — a restarted provider keeps its old copy.
- Docker Desktop stopped mid-verification and was restarted; nothing was lost.
- The e2e seeds fail on a cold stack and pass on a warm one. Known, and it cost
  two runs here.

**A `management-web` unit test times out intermittently at its five-second
limit** (observed 5093 ms). It is **not** this feature's: it fails identically on
a stashed clean tree and passes on re-run and when its package runs alone. It is
filed rather than absorbed, and the timeout was **not** raised to make it green —
a test a few percent over its limit is a coin-flip in CI, and the natural
response is a re-run that erases the evidence.
