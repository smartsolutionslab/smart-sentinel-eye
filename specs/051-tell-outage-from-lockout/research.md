# Research — 051 tell an outage from a lockout

Phase 0. Everything here was **observed or read in source**. Where a claim is
inference, it says so.

---

## R0. Is there a locked decision contradicting this feature?

**Checked, and there is not.** Recording it because three of the last five specs
found a locked ADR describing a system nobody built, and "we looked" is worth
more written down than assumed.

| ADR | Governs | Conflict? |
|---|---|---|
| ADR-0080 (browser auth) | The kiosk flow — but **already amended by ADR-0131** for exactly this area. Its `client_credentials` kiosk was never built. | No. The half this feature touches is the half ADR-0131 replaced. |
| ADR-0131 (a kiosk keeps its grant) | Restart recovery, storage of the grant, the exposure that buys. | No. It is silent on *what happens when renewal fails*, and explicitly leaves the ceiling out. |
| ADR-0108 (Playwright e2e) | How this gets tested. | No. |

**One near-miss worth naming, because it reads like a conflict and is not.**
ADR-0113 says *"Automatic retry is forbidden"* — of **optimistic-concurrency
conflicts**, where a write lost a race and a human must choose what to keep.
This feature retries a *read-only identity renewal* against a service that was
briefly absent. Nothing is overwritten and no decision is taken on a person's
behalf. Different failure, opposite correct response.

**No ADR amendment is a gate for this feature.** A new ADR is still expected at
Phase 5 to record the classification rule and the retry bound, because those are
decisions a future reader will otherwise have to reverse-engineer.

---

## R1. Can *terminal* be known before the wall is handed to the provider's login page?

**FR-007 depends entirely on this, and the answer is yes.** Verified against a
running provider, not inferred.

Exercising the refresh-token grant the way renewal does:

| Condition | What the provider does |
|---|---|
| Healthy, valid grant | answers `200` |
| **Account disabled** | **answers `400`** with `{"error":"invalid_grant","error_description":"User disabled"}` |
| **Provider stopped** | **the request throws** `TypeError: Failed to fetch` |

And the library preserves the difference — read in
`oidc-client-ts@3.5.0/dist/esm/oidc-client-ts.js`, not assumed:

```js
// postForm — the token endpoint path
try { response = await this.fetchWithTimeout(...) }
catch (err) { logger.error("Network error"); throw err; }   // ← raw TypeError, unwrapped
...
if (!response.ok) {
  if (json.error) { throw new ErrorResponse(json, body); }  // ← carries .error
}
```

So at the moment `signinSilent()` rejects, the cause is fully present:

- **`ErrorResponse`** → the provider answered; `.error` is the OAuth code.
- **`ErrorTimeout`** → `fetchWithTimeout` gave up.
- **anything else** (a raw `TypeError`) → it never answered.

**Decision**: classify at the silent-renew failure, **before** deciding whether
to redirect. A terminal verdict skips the redirect, so the wall never reaches the
provider's login form and FR-007 is met directly rather than through its escape
hatch.

**Where the cause is destroyed today** is one line in `useSessionExpiry.ts`:

```ts
setSessionRenewer(() => auth.signinSilent().then((user) => user !== null).catch(() => false));
```

That `.catch(() => false)` discards the only object that knows why. It is the
smallest possible place to make this change, and it is the whole of the fix's
foundation.

**Alternatives considered**: classifying at `auth.error` in `App.tsx` — rejected,
it is downstream of the redirect, so the shut-out case has already left the app.
Probing the provider's health endpoint separately — rejected as a second source
of truth that can disagree with the actual renewal.

---

## R2. What the classification rule is, and which way the default falls

**By reported cause, never by "did the provider answer"** (FR-004). Answering is
not the same as refusing permanently.

| Verdict | Reached by | What happens |
|---|---|---|
| **Recoverable** | no answer (`TypeError`), `ErrorTimeout`, or an answer of `server_error` / `temporarily_unavailable` | retry unattended (US1) |
| **Refused** | `invalid_grant`, `invalid_client`, `unauthorized_client`, `access_denied`, `invalid_scope` | terminal; say so, no prompt, no retry (US2) |
| **Interactive** | the provider wants a human, and no error object exists to inspect | the existing "Session expired" screen, unchanged |

**Unrecognised codes are recoverable** (FR-005). The asymmetry is the reason and
it is not close: a wrong *recoverable* verdict costs one screen a pointless
request every 30 seconds; a wrong *refused* verdict costs a whole wall its
picture through an outage it would have survived.

**A trap that is easy to fall into and would be invisible in review**: a
reachable provider returning `server_error` is an overloaded identity service —
the single most likely real outage on a fab. Classifying on the *class*
(`ErrorResponse` ⇒ terminal) rather than the *code* would leave every screen dark
through exactly that. Written into the rule as a named case, with a test.

**One more code path worth knowing about.** `postForm` throws a plain `Error` for
an unexpected `Content-Type` — a captive portal or a proxy answering with HTML.
That is the spec's "reachable but wrong" edge case; it carries no OAuth code, so
it lands in the unrecognised bucket and is treated as recoverable. Correct: a
proxy in the way is exactly the sort of thing that clears.

---

## R3. Reconciling the 60-second redirect guard (FR-012)

**Kept, and narrowed to the case only it can see.** Deleting it would lose real
coverage: a redirect that *completes* and still lands back unauthenticated
produces **no error object at all**, so the classifier is blind to it. That is
the ten-hour session ceiling arriving (issue 1989), and it is the most frequent
failure on this screen.

**How they are made unable to disagree**: they do not both decide. There is one
verdict, and it has a precedence:

1. If a classified cause exists, it decides — **recoverable** or **refused**.
2. If there is no cause because the redirect simply came back unauthenticated,
   the guard decides — **interactive**.

The guard never overrides a classified cause, and the classifier never speaks
about a failure that produced no error. One state, two disjoint sources.

**A distinction this forces into the open, and it matters.** "The session ended
and a person must sign in" is **not** "this screen has been shut out". Today both
render "Session expired". Conflating them in the other direction would be worse:
a wall dropping out at its ten-hour ceiling would announce itself as *revoked*,
sending someone to re-commission a screen that only needed a sign-in. So
**interactive** stays exactly as it is, and only **refused** gets US2's new
wording.

---

## R4. The retry schedule, and what happens at its bound (FR-006)

**Exponential from 2 s, doubling, ceiling 30 s, with ±30% jitter.**

**Justified against SC-001**, which is the constraint that actually binds:
recovery within **2 minutes** of the provider becoming healthy. Worst case, the
provider recovers immediately after an attempt fails, so the wait is one full
interval: `30 s × 1.3 = 39 s`, plus one renewal round-trip. Comfortably inside
two minutes, with room for the round-trip to be slow.

**A ceiling above ~60 s would silently break SC-001** — this is the interaction
worth stating out loud, because the ceiling and the success criterion live in
different documents and nothing would connect them at review time.

| Attempt | Base delay | With jitter |
|---|---|---|
| 1 | 2 s | 1.4–2.6 s |
| 2 | 4 s | 2.8–5.2 s |
| 3 | 8 s | 5.6–10.4 s |
| 4 | 16 s | 11.2–20.8 s |
| 5+ | 30 s (ceiling) | 21–39 s |

**At the bound it keeps retrying, forever, at the ceiling.** Justification: there
is nobody standing at the wall. A screen that gives up is a screen that needs a
person, which is the precise failure this feature exists to remove — stopping
would reintroduce it after a delay instead of at once. The cost is one request
every ~30 s from a screen whose provider is permanently gone, which is
negligible next to a dark wall, and the screen says it is still trying so a
passer-by is not misled.

**Jitter is US3**, and it is why US3 is *in* this spec rather than deferred: it
is one multiplication inside a schedule that has to exist anyway. Deferring it
would mean knowingly shipping twenty screens that reconnect in lockstep against
a service that just came back — a self-inflicted second outage, filed for later.
**Included.**

---

## R5. How an outage is induced in an automated test

**Route interception in the browser for CI; a real container stop for Phase 5
verification.** Both, because neither alone is honest.

| | Route interception | Stopping the provider |
|---|---|---|
| Runs in CI | **yes** | needs container control from the test |
| Deterministic | yes | timing-dependent |
| What it proves | the app's reaction to a **simulated** provider | the app's reaction to a provider **being down** |

`route.abort('failed')` produces the same `TypeError: Failed to fetch` observed
from a stopped provider, so the classifier sees what it would really see.

**Where the two can diverge, stated because a stubbed outage has hidden a defect
in three of the last five specs here:**

- Interception **cannot** reproduce DNS failure, TLS failure, or a connection
  that hangs rather than refusing. A hang exercises `ErrorTimeout`; an abort
  never will, so **`ErrorTimeout` needs its own simulated case** or it ships
  untested.
- Interception only catches what the **page** requests. `signinSilent` may run
  in a hidden iframe, whose requests are a different frame — a pattern registered
  on the page alone can silently match nothing, and **a test that intercepts
  nothing looks exactly like a test that passes**. The interception must be
  asserted to have fired, not assumed.
- A real provider can answer slowly rather than not at all, which is the
  overload case R2 cares most about. Interception fulfils it with a stubbed
  `server_error` body — fine, but it is a stub of the response, not of the load.

**Consequence for the plan**: every interception-based test asserts that its
route handler actually fired. The container-stop run is recorded in
`verification.md` as a manual observation, and the verification note must not
let a green CI suite read as "a wall was watched through a real outage".

---

## R6. Where the code lives

**`apps/kiosk-web`, not `apps/shared`.** The management console has a person in
front of it who can read an error and act; unattended retry is a property of an
unattended screen. There is no second consumer, so moving it to `shared` would be
speculative generality of exactly the kind the guidelines forbid. If
`management-web` ever wants it, moving it then is a smaller change than
maintaining a shared abstraction with one caller.

**No C# is involved.** No backend, no contract, no realm change, no scope change.
The coverage gates in ADR-0065 cover Domain, Application and Shared assemblies —
none are touched, so **there is no coverage gate to cite as evidence here**, and
citing one would be misleading rather than reassuring.
