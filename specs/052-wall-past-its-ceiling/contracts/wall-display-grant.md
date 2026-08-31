# Contract — what a wall display holds, and who may hold it

Written as properties a test can assert. Every one is asserted **against a
running provider** unless it says otherwise, because the file has been right
while the system was wrong for this problem's whole life.

---

## C1. Who holds the privilege

| Must | |
|---|---|
| A kiosk enrolled at runtime | holds **no** long-lived-credential privilege |
| An operator account | the same, **and** an attempt to mint such a credential is refused |
| A wall-display account | holds it, and can mint one |
| The count of holders | equals the number of fabs, **among accounts this system creates or declares** |

**Must not**: be asserted by reading the realm file. A file-reading check may
confirm what is *declared*; it cannot confirm what is *held*, and the difference
is the entire defect.

**Explicitly not covered**: an account created by hand in the provider's console.
Filed (FR-002a). The contract states the boundary rather than implying totality.

---

## C2. Containment during enrolment

| Must | |
|---|---|
| After a successful enrolment | the new account holds nothing |
| If the removal fails | **the enrolment fails** — no success is reported over a privilege holder left behind |
| Run twice | idempotent; a retry after a partial failure is safe |
| Reach | only accounts enrolment created |

**Must not**: be applied to a human account. It removes every direct realm
mapping.

---

## C3. Containment of what already exists

| Must | |
|---|---|
| At startup | accounts enrolment created previously hold nothing |
| Repeated starts | no error, no change after the first |
| Definition of "a kiosk account" | derived from what enrolment creates, not a pattern typed a second time |

---

## C4. A wall stays up

| Must | |
|---|---|
| The grant a wall screen holds | is an **offline** grant carrying **no expiry** — decoded, not counted |
| After a restart outlasting an ordinary session | the screen returns without a person |
| An operator's session | **unchanged** |
| Any account that could sign in before | still can |

**Must not**: be demonstrated by asserting a token exists. That passes today with
the defect fully present.

---

## C5. A wall display may only show a wall

| Must | |
|---|---|
| The scopes in the token the wall account **actually receives** | enumerated from the token, and every one exercised |
| `sse.events.write` | **absent** |
| A write attempt | refused |
| A read of another fab | refused, or empty with a control proving the query works |

**Must not**: assert refusals on a chosen handful. Spec 050 tested three
endpoints and never attempted the authority the account held — which is how
"the account can change nothing" was recorded while it was false.

---

## C6. A misconfigured screen says something true

| Must | |
|---|---|
| A wall-mode screen signed in as an operator | shows the **terminal** state — it is refused, and retrying cannot help |
| A default-mode screen signed in as a wall account | works normally, without a long grant |

**Must not**: retry forever behind "Reconnecting". That is what happens today,
because the refusal code is unrecognised and spec 051 defaults the unrecognised
to recoverable.

---

## C7. What the record must not claim

| Must | |
|---|---|
| Twenty screens | stated as **unmeasured** |
| A real power cut | stated as **unmeasured** |
| A ten-hour ceiling in production | stated as **unmeasured**; a shortened ceiling shows the mechanism only |
| §Availability | **not discharged** |

This is a contract because the failure it guards against is a documentation
failure, and this repository has had three of them.
