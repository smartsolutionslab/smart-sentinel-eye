# Verification: One way to say a version is stale

**Feature**: `031-stale-version-convention` · observed **2026-08-28**

**Status: every checklist item verified, all of it observed rather than inferred.**

The note was first written on 2026-08-28 with the two live steps deferred, because
Docker's daemon was wedged on this machine. Docker recovered the same day and both
were performed; §2 and §4 carry the results and a marker where the deferral stood.

**The by-hand provocation in §2 found a defect** — not in this feature, but in the
rules page it walks through: Automation's server refuses a stale publish correctly
and the app tells the operator nothing at all. Filed as issue 1952, and §2 sets
out why it is not a spec 031 regression.

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

**Done by hand, and it found something.** Quickstart §2 says to provoke one:
*"publish a rule from a stale version and read what the app says — must still say
reload to see their version, exactly as it did before."*

Two operators, both holding version 0 of the same rule; the first publishes and
wins; the second publishes from its now-stale version.

**Automation's server is correct:**

```
POST 409 :: {"title":"RULE_STALE","status":409,
  "detail":"Rule 'e2e-t018-…' has changed since version 0 (now 1).
            Re-read it and reapply the change."}
```

A `_STALE` code, and re-read wording. Exactly what ADR-0119 asks of it.

**The operator is told nothing.**

| | |
|---|---|
| Elements with `role="alert"` on the page | **0** |
| Page text mentions reload / changed / re-read | **false** |
| Page text mentions "try again" | **false** |

`RulesPage.tsx` discards both mutation results — `const [publishRule] = usePublishRuleMutation()`
and `const [archiveRule, { isLoading: archiving }] = useArchiveRuleMutation()`, each
invoked as `void …(…)`. Its only `role="alert"` belongs to the *list* query and
reads "Could not load rules."

Worse for publish specifically: the failed mutation still invalidates the list, so
the row refetches and flips to **Published** — by the other operator. The refusal
reads as success.

`LayoutsPage.tsx` already does this correctly, and its comment records that this
was a known class of bug: *"Every one of these used to discard its failure, so a
rejected publish or …"*, feeding a `mutationError` into a second `role="alert"`.
Rules never got that migration.

**This is not a spec 031 regression**, and the distinction matters for FR-006:
Automation is one of the six contexts spec 031 deliberately left alone, and its
server side is correct. What is wrong is a client page that never surfaced the
refusal in the first place. It surfaced now only because this quickstart step
assumes the app says something. Filed as issue 1952.

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

## 4. The stale refusal on the wire (FR-001, data-model) — **seen live**

> **Completed 2026-08-28.** This section first recorded both live steps as *not
> performed*, because Docker's daemon was wedged. Docker recovered; both were
> then done against a run-mode stack, and the results replace the deferral.

Quickstart §1, exactly as written. A camera read at version `0`, corrected once
with `If-Match: "0"` (→ **204**), then corrected again with the same, now stale
version:

```
HTTP 412
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.13",
 "title":"CAMERA_VERSION_STALE",
 "status":412,
 "detail":"Camera '01a046b7-…' is at version 1, not 0.
           Re-read it before reapplying your change."}
```

Three things at once, all as the quickstart demands:

- **`CAMERA_VERSION_STALE`**, not `CAMERA_VERSION_MISMATCH` — the string this
  feature removed does not appear on the wire.
- **412**, deliberately unchanged. Not 409, so nobody standardised the statuses
  instead of the codes — which the quickstart names as "the spec's central
  decision reversed".
- The detail says **"Re-read it before reapplying your change"**, not "try again".

`ChangeCameraAddressIntegrationTests` asserts both the status and the title over
real HTTP and runs in CI, so this was already covered. It has now also been
watched.

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
| The camera's stale refusal carries `CAMERA_VERSION_STALE` | FR-001 | **Seen live** — §4 |
| Its status is still 412 | data-model | **Seen live** — §4, and it is 412, not 409 |
| A client recognises a stale refusal without reading the status | FR-002 | **Verified** in `problemDetail.ts` |
| A terminal refusal is distinguishable from a lost update | FR-003 | **Verified** |
| No lost-update message anywhere says retry | FR-004 / SC-002 | **Verified** — and the rules page says nothing at all (issue 1952) |
| An unrecognised refusal still shows the server's message | FR-005 | **Verified** — `problemCode` falls through to the server detail |
| The six correct contexts' tests pass **unmodified** | FR-006 / SC-004 | **Verified by hand** — 299 tests, and no spec-031 commit touched them |
| A plausible wrong code fails the build | SC-001 | **Verified by hand** — the probe fired and was removed |
| The convention is recorded as an ADR, with the refused trade | FR-007 / SC-005 | **Verified** — ADR-0119 |
| No "provisional" note remains in shared code | FR-008 | **Verified** |

**Everything resting on a person was done**: the deliberately-broken
architecture test, the history check that the six untouched contexts really were
untouched, the stale PATCH on the wire, and the two-operator rule publish.

The last of those is the one that earned its keep. Every automated check in this
feature passes, and the rules page still leaves an operator with no idea their
publish was refused — which no test in the repo was asking about, because the
question only arises when a person follows the quickstart and looks at the
screen.
