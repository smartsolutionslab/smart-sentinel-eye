# Research — 052 a wall past its ceiling

Phase 0. Everything measured against a running Keycloak 26.6 or read in source.
Where a claim is inference, it says so.

---

## R0. Is there a locked decision contradicting this?

**Checked, and there is not.**

| Decision | Governs | Conflict? |
|---|---|---|
| ADR-0080 (browser auth) | the kiosk flow — already amended by ADR-0131 for this area | No |
| ADR-0131 (a kiosk keeps its grant) | restart recovery and the storage that buys it | No. It records the ceiling as **explicitly not fixed** and leaves it open. |
| ADR-0132 (a wall display is not a person) | **superseded before it shipped**; its corrected text is the closest prior art | No — it is a record of a withdrawn attempt, not a live constraint |
| ADR-0133 (spec 051) | what a screen does when identity fails | No, but see R5 — this feature reaches one of its branches |
| ADR-0113 | forbids automatic retry of **concurrency conflicts** | No. Different failure; noted because the words look relevant. |

**No amendment gate applies.** A new ADR is expected at Phase 5, and it should
supersede ADR-0132 properly rather than leaving two records of the same idea.

---

## R1. How a screen that should hold a long-lived grant is told from one that should not

**This is the decision spec 050 got wrong from the other direction, and the trap
is sharp.**

An **optional** scope does not lock anyone out — *provided they do not ask for
it*. Measured:

| | asks for the scope | does not |
|---|---|---|
| account **without** the role | **entire sign-in refused** (`not_allowed`) | normal session |
| account **with** the role | long-lived grant, no expiry | normal session |

So `kiosk-web` cannot simply add the scope to what it requests: every operator
signing in to a kiosk would be refused outright — spec 050's lockout by another
route. And the application cannot decide per account, because it does not know
who is signing in until after they have.

**Decision: a second client, `kiosk-wall`, selected by deployment configuration.**

A single flag decides both the client and the scope, so there is no half
configuration to get wrong:

| Mode | client | requests |
|---|---|---|
| default | `kiosk-web` | `openid` |
| wall | `kiosk-wall` | `openid offline_access` |

**Why a second client rather than a flag on the existing one** — and this is the
part that earns it: **scopes are per client, so a separate client is the only
place a wall display's authority can actually be narrowed.** `kiosk-web` carries
`sse.events.write`; `kiosk-wall` simply will not. That turns R4 from a paragraph
of prose about exposure into a property of the configuration. A flag on one
client cannot do this at all.

**Alternatives considered**: a flag on `kiosk-web` — rejected, cannot narrow
authority, and leaves the never-expiring grant carrying a write scope.
Per-account decision after sign-in — impossible; the scope is requested before
the account is known. Two builds of the application — worse than one build with
configuration, and this repository ships one bundle.

### What a misconfigured screen does, because both will happen

| Misconfiguration | What happens |
|---|---|
| **wall-mode screen, operator signs in** | the account lacks the role, so sign-in is refused with `not_allowed` |
| **default-mode screen, wall account signs in** | signs in normally and gets an ordinary session — it simply does not get the long grant, and drops out at the ceiling as today |

The second is benign. **The first is not, because of how spec 051 handles it** —
see R5.

---

## R2. Where the strip runs, and what happens when it fails

`EnrollKioskCommandHandler` creates a confidential client; the provider creates
the service-account user as a side effect, already holding the privilege. So
there is a window, and the question is how small and how honest.

**Decision: strip inside the enrolment, immediately after the client exists, and
fail the enrolment if the strip fails.**

- **The window is unavoidable but bounded** — the account cannot be created
  without the client, so it exists holding the privilege for the duration of one
  administrative call. What must not happen is an enrolment *reporting success*
  while leaving a privilege holder behind.
- **Failing the enrolment is the honest outcome.** The caller retries, and the
  strip is **idempotent** (measured: a second removal returns 204), so a retry
  is safe. The alternative — succeed and reconcile later — means the system says
  "enrolled" about a kiosk that holds a credential it should not.
- **A saga is not worth it here** (ADR-0072). Compensation would mean deleting
  the client, which is what failing the enrolment already invites the caller to
  redo; a saga adds a state machine to a two-call sequence.

**The shape matters and cost a cycle**: read the **direct** realm mappings with
`GET /users/{id}/role-mappings/realm` — there is exactly one,
`default-roles-<realm>` — then `DELETE` that same list. A role object obtained
any other way returns **404**, which reads like a permission problem and is not.

**Authority: none new.** Measured against the real `identity-admin` service
account: allowed, leaves the account holding nothing, leaves the kiosk still
able to obtain a token, idempotent.

---

## R3. Kiosks enrolled before this feature

**They still hold the privilege**, so US1's claim is false for them until
something acts.

**Decision: a sweep at enrolment-service startup, over kiosk clients only.**

- It uses the same call and the same authority as R2, so it is the same code
  reaching more accounts.
- It is **idempotent**, so running it every start is safe and it doubles as
  reconciliation against drift.
- It is bounded to accounts this system created — clients matching the enrolment
  naming — which is exactly the scope FR-002 claims and no more.

**This is what SC-004 may honestly claim**: among accounts this system creates or
declares, only wall displays hold the privilege. It says nothing about an account
created by hand in the provider's console — that is filed (FR-002a) and stays
open.

**Alternatives**: a one-time migration — same work, but silently does nothing
about drift; doing nothing and narrowing the claim — leaves every already-enrolled
kiosk holding it, which is the state this feature exists to end.

---

## R4. What a wall display may do

**Decided by construction rather than by prose.** `kiosk-wall` carries
`sse-identity`, `sse-groups` and the five read scopes. It does **not** carry
`sse.events.write`.

Spec 050 asserted "the account can change nothing", tested refusals on three
endpoints, and never attempted the one the account actually held. Here there is
nothing to attempt: the authority is not in the grant.

**Confirmed the kiosk does not need it** — no call to event ingestion appears
anywhere in `apps/kiosk-web` or the shared client; the matches for "event" are
DOM handlers and overlay rendering. **This must still be shown end to end**: a
wall must render with the wall client, not merely sign in.

**Enumeration, not a typed list** (US3, FR-009): the grant's authorities are
read **out of the token** the wall account actually receives, and every scope
found is exercised. A list somebody typed is how spec 050 missed the one that
mattered.

---

## R5. A cross-feature consequence that must not be discovered later

Spec 051 classifies an identity failure by its reported code, and treats
**unrecognised codes as recoverable** — a deliberate asymmetry, because a wrong
"terminal" darkens a wall.

`not_allowed` — what a wall-mode screen gets when an operator signs into it — is
**not** in the refused set. So today a misconfigured wall screen would sit on
*"Reconnecting"* and retry **forever**, telling whoever walks past that this
will clear. It will not.

**Decision: add `not_allowed` to the refused codes**, with the reason recorded.
It is a genuine refusal from a provider that answered, and no amount of retrying
changes it. This is a small amendment to ADR-0133's rule and belongs in this
feature, because this feature is what makes the code reachable.

---

## R6. Testing "past the ceiling"

Nothing runs for ten hours.

**Decision: assert the grant's *type*, and reuse the shortened ceiling only as a
secondary demonstration — never as the primary claim.**

- **Primary**: decode the refresh token a wall screen holds and assert it is an
  offline grant carrying **no expiry**. That is the property; it is exact, fast,
  and cannot pass by accident. Asserting "a token exists" passes today with the
  defect fully present.
- **Secondary**: shorten the ceiling on a test realm and watch a screen survive
  it. Spec 050 did this and **it broke the e2e seeds** — the seeds drive a long
  operator session that then expired mid-run, and it worked only because the dev
  database already held published layouts. So the shortened-ceiling run is
  **gated and run deliberately**, never as part of the default suite, and the
  task, the test and the verification note must each state that it demonstrates
  the mechanism and **not** the production configuration.

**One place saying so is not enough** — that instruction is repeated because
spec 050 wrote it in three places and the note still had to be corrected.

---

## R7. Where the code goes

| Change | Where |
|---|---|
| `kiosk-wall` client, wall accounts already present | `src/AppHost/Realms/smart-sentinel-eye-realm.json` — **edited line by line**, never reserialised |
| Strip on enrolment | Identity Application + Infrastructure |
| Startup sweep | Identity, alongside enrolment |
| Mode flag, client and scope selection | `apps/kiosk-web/src/app/auth.ts` |
| `not_allowed` as refused | `apps/kiosk-web/src/app/identityFailure.ts` |

House rules apply to the C#: `Ensure.That` guards (ADR-0105), `Result<T, Error>`
for expected failures, destructuring in handlers reading two or more fields,
collection expressions with explicit types, no leading underscore on fields, no
cross-context project references.

**Coverage gates do apply here** — unlike specs 051 and 050, this touches
Identity's Application layer, so ADR-0065's Application threshold is live.
