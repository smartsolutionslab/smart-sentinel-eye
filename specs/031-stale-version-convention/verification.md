# Verification: One way to say a version is stale

**Feature**: `031-stale-version-convention` · observed **2026-08-28**

**Status: nine of the ten checklist items verified here. One could not be
performed locally** — the two live-UI/curl steps — because Docker is wedged on
this machine, and that is stated rather than glossed. What those steps assert is
covered by integration tests that ran green in CI within the hour; the difference
between "a test asserts it" and "I saw it" is the whole point of a Phase-5 note,
so it is spelled out per item below.

The two things T018 names explicitly — **the deliberately-broken architecture
test** and **the empty diff over the six untouched contexts** — were both done by
hand, and both are the checks most likely to be faked by a green suite.

---

## 1. The convention is enforced, not just written (SC-001)

**Done by hand, and it fired.** A plausible wrong code was added to a real errors
file — `ChangeCameraAddressErrors.cs` — exactly as quickstart §3 instructs:

```csharp
public sealed record WidgetStale(Guid Camera)
    : ChangeCameraAddressError(
        "WIDGET_VERSION_MISMATCH", ..., HttpStatusCode.Conflict);
```

`dotnet test tests/Architecture.Tests --filter StaleCodeConvention` then failed,
naming the file and the code:

```
["src\CameraCatalog\Application\Commands\ChangeCameraAddressErrors.cs: WIDGET_VERSION_MISMATCH"]
ADR-0119: a refusal because the caller's version is no longer current must carry a
code ending '_STALE'. … Rename the code(s) above to end '_STALE'. The HTTP status
is free — 409 and 412 are both in use and neither is authoritative.
```

The probe was removed and the suite went green again (2 passed).

**Why this mattered rather than being ceremony.** The quickstart warns that a
check which only looks for the exact removed string passes forever and catches
nothing. It does not: `MeansStale` carries six phrases a future context might
plausibly invent — `VERSION_MISMATCH`, `VERSION_CONFLICT`, `VERSION_OUTDATED`,
`STALE_VERSION`, `REVISION_MISMATCH`, `CONCURRENCY_CONFLICT`. `WIDGET_VERSION_MISMATCH`
is not the code this feature removed, and it was still caught.

---

## 2. The six that were already right did not move (FR-006 / SC-004)

**Done, and checked against history rather than the working tree.** A working-tree
`git diff` is trivially empty on merged work and proves nothing. The real claim is
that this feature never edited those files, so the check is what its commits
touched.

Since spec 031 began (`64d7e89`), exactly **one** commit has touched
`tests/{LayoutComposition,OverlayDesigner,SystemVariables,Automation}.Application.Tests`:

```
52b5dd2 test(037): recovery asserted on its payload, and the guards proved to hold
```

That is **spec 037**, not this feature. No spec-031 commit modified any of them.

And they pass unmodified:

| Suite | Result |
|---|---|
| `LayoutComposition.Application.Tests` | 76 passed |
| `OverlayDesigner.Application.Tests` | 41 passed |
| `SystemVariables.Application.Tests` | 74 passed |
| `Automation.Application.Tests` | 108 passed |
| `management-web` (vitest) | **191 passed**, 20 files |

**Not done locally**: provoking one by hand through the UI — publishing a rule
from a stale version and reading the words. That needs a running stack. What it
would confirm is asserted in code and by the 191 frontend tests: `CONFLICT_FALLBACK`
still reads *"Someone else changed this while you were working. **Reload to see
their version**, then reapply your change."*

---

## 3. The code is what identifies a lost update (FR-002, FR-003, FR-004)

Read in `apps/shared/src/api/problemDetail.ts`:

- **FR-002 — recognised without reading the status.** `isStaleConflict` is
  `problemCode(error)?.endsWith('_STALE')`. The status is not consulted at all,
  and the comment says why: 409 also carries `LAYOUT_NAME_TAKEN` and
  `CAMERA_RETIRED`, 412 also carries `WEBHOOK_CLIENT_ALREADY_EXISTS`. Neither
  status answers the question, so neither is asked.
- **FR-003 — a terminal refusal is distinguishable.** `isTerminalRefusal` keys on
  `CAMERA_RETIRED`, which is a 409 and would otherwise inherit the lost-update
  wording — telling an operator to reload a camera that no version of can be
  corrected.
- **FR-004 / SC-002 — no lost-update message says retry.** The only occurrences
  of "try again" in that file are in comments explaining why it must never be
  said: *"Offering 'try again' to someone whose version moved makes them resubmit
  unchanged, replaying their edit over the other writer's."*

---

## 4. The stale refusal on the wire (FR-001, data-model)

**Not performed locally.** Quickstart §1's `curl` needs a running stack, and the
stack will not start on this machine — Docker's daemon is wedged (`docker info`
hangs; the AppHost reports *"Container runtime 'docker' was found but appears to
be unhealthy"*), most likely a consequence of the system disk having filled to
zero bytes earlier in the day. Recorded rather than worked around.

**What covers it**, and it covers exactly the two assertions §1 makes —
`ChangeCameraAddressIntegrationTests`, over real HTTP through the real
ProblemDetails mapping:

```csharp
replayed.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);   // still 412
problem.GetProperty("title").GetString().ShouldBe("CAMERA_VERSION_STALE");
```

Its own comment states the reason it asserts the title rather than the status:
*"a test asserting only the status now asserts the part that no longer decides
anything… if that mapping broke, the handler unit test would still pass and every
operator would still be told the wrong thing."*

That suite runs in CI's **integration tests (Docker)** job, green on every PR
merged today, most recently #1950. So the behaviour is proven by a test that
exercises the wire — but **nobody watched it happen today**, and this note does
not claim otherwise.

---

## 5. No provisional note remains (FR-008)

```
$ grep -rn "Provisional, pending #1857" apps/
(no output)
```

Clean. This feature exists because a deferred decision became a comment in shared
code; leaving the comment behind would have been the same failure in miniature.

---

## 6. The decision is written down, with the refused trade (FR-007 / SC-005)

`docs/adr/0119-stale-version-vocabulary.md` exists and says the **code** is
authoritative: *"The HTTP status is not authoritative and MUST NOT be used to
identify one."*

It records the alternative that was refused, which is what stops the decision
being reversed by someone who rediscovers the correctness argument:

> ### Standardise the sixteen onto `412` instead
> **This is the more HTTP-correct end state, and it was rejected.** … so "make
> the outlier match the majority" makes the *newest and most correct* one wrong.
> It was rejected on cost: changing sixteen declaration sites across six contexts.

---

## Checklist

| | | |
|---|---|---|
| The camera's stale refusal carries `CAMERA_VERSION_STALE` | FR-001 | Integration test, CI-green — not seen live |
| Its status is still 412 | data-model | Integration test, CI-green — not seen live |
| A client recognises a stale refusal without reading the status | FR-002 | **Verified** in `problemDetail.ts` |
| A terminal refusal is distinguishable from a lost update | FR-003 | **Verified** |
| No lost-update message anywhere says retry | FR-004 / SC-002 | **Verified** |
| An unrecognised refusal still shows the server's message | FR-005 | **Verified** — `problemCode` falls through to the server detail |
| The six correct contexts' tests pass **unmodified** | FR-006 / SC-004 | **Verified by hand** — 299 tests, and no spec-031 commit touched them |
| A plausible wrong code fails the build | SC-001 | **Verified by hand** — the probe fired and was removed |
| The convention is recorded as an ADR, with the refused trade | FR-007 / SC-005 | **Verified** — ADR-0119 |
| No "provisional" note remains in shared code | FR-008 | **Verified** |

**What rests on a person and was done:** the deliberately-broken architecture
test, and the history check that the six untouched contexts really were untouched.
**What rests on a person and was not done:** the two steps needing a live stack,
blocked by Docker and covered by CI-green integration tests. Neither gap is
hidden behind a green tick.
