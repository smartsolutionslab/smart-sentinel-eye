# Verification — 048 the camera picker finds every camera

Phase 5.

---

## 0. What this changes, in one line

A camera that exists is now offered, and when it cannot be, the picker says so.

---

## 1. Automated checks — run the way CI runs them

Not a subset. Spec 045 shipped a green subset and CI caught an architecture test
that had never been run locally.

| Check | Command | Result |
|---|---|---|
| Format | `pnpm format:check` | pass |
| Lint | `pnpm -r --filter "./apps/**" lint` | pass |
| Typecheck | `pnpm -r --filter "./apps/**" typecheck` | pass |
| Test | `pnpm -r --filter "./apps/**" test` | **412 pass** — shared 139, kiosk 62, management 211 |
| Backend | — | **not run, and not needed**: no C# was touched (research R7) |

**Lint failed first, and a grep told me it had passed.** I counted matching
output lines instead of reading the exit code, and the count looked plausible
while eslint was failing on two undefined DOM types in a test file. Re-run on
exit codes, which is what CI uses. Recorded because a check that reports success
while failing is worse than one that fails loudly, and I built that
false green myself.

---

## 2. Mutation testing — every guard, before trusting it

Twelve mutations across the two layers. Each had to kill at least one test; any
that did not would mean the test was decoration.

**Paging** (`apps/shared`)

| Mutation | Killed |
|---|---|
| Stop after the first page | 5 tests |
| Drop the de-duplication | 1 |
| Remove the page bound | 3 |
| Always report the list complete | 2 |
| Report rows gathered instead of the source's total | 3 |
| Drop the name ordering | 1 |
| Never stop early on a short page | 2 |

**The picker** (`apps/management-web`)

| Mutation | Killed |
|---|---|
| Render the notice unconditionally | 2 |
| Drop the `aria-describedby` association | 1 |
| Collapse the three empty states into one | 1 |
| Omit the total from the notice | 2 |
| Slice the list back to 50 — *the original defect* | 4 |

---

## 3. What mutation testing found — a real defect at the exact bound

**One mutation survived, and it was right.**

Completeness originally required the paging loop to have *seen a short page*
before it would call anything complete. That is wrong at exactly the bound: a
fab of 1000 cameras arrives as five full pages, holds every camera, and was
reported as **incomplete**. The operator would have been shown a truncation
notice for a list that was not truncated.

Removing the extra condition passed all eleven tests at the time, which is how
it surfaced — the mutation was a simplification, and the simplification was
correct.

Nothing else in the suite covered it: 250 cameras end on a short page and 1200
are genuinely incomplete, so **both agreed with the broken rule**. The case has
its own test now.

This was the first of the three named risks in the task list — *an off-by-one at
a page boundary, hidden by a test that only counts*. It is worth noting the risk
being named in advance did not prevent it; only the mutation did.

**A rationale was corrected too.** The task and the data model both claimed
completeness was carried because the producer knew *why* the loop stopped and a
consumer could not see it. That was the reasoning the defect disproved.
Completeness is exactly `items.length >= count`, and nothing is hidden. It stays
carried so the rule lives in one place — a weaker reason, and a true one.

---

## 4. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| All 250 cameras of a full fab are gathered | paging tests | any single-page fixture |
| A camera delivered twice at a boundary is offered once | paging tests + mutation | a test that counts options |
| The bound stops at 5 requests and is reported | paging tests + mutation | reading the constant |
| A fab sitting exactly on the bound is complete | the test the surviving mutation forced | the 250 and 1200 cases, which both agreed with the bug |
| The notice states both numbers | dialog tests | — |
| **The notice is absent when the list is complete** | dialog tests + mutation | assuming it is conditional |
| The notice is announced, not merely painted | `aria-describedby` test | a screenshot |
| The three empty states are distinguishable | dialog test + mutation | — |
| A selection survives the list growing | dialog test | — |
| **That a 250-option dropdown is usable** | **nothing** | **every test above** |
| **That two round trips feel acceptable** | **nothing** | **every test above** |
| **That 1000 is the right bound** | **nothing — chosen, not measured** | — |

---

## 5. Live check — against a populated fab

Run mode, against the dev stack whose catalogue had accumulated real cameras.
A temporary Playwright instrument opened the layout editor and read the picker;
it was deleted afterwards.

**The fab held 70 cameras**, read off the cameras page's own total rather than
assumed. That is above the old fifty-row request, so this is the defect
scenario — **20 cameras were unreachable before this change**.

| Reading | Result |
|---|---|
| Cameras page total | `Showing 1–50 of 70` |
| Options in the picker | **71** — all 70 cameras plus the empty-cell placeholder |
| Alphabetical order | **true**, verified by comparing against a sorted copy |
| First / last option | `After-drying Group` … `t014-probe-camera` |
| Truncation notice | **absent**, correctly — 70 is complete |
| `aria-describedby` | **null**, correctly — nothing to point at |

**Two requests went out, and the pair is the interesting part:**

```
registeredAt desc  offset=0  limit=50    ← the cameras page, untouched
name asc           offset=0  limit=200   ← the picker
```

The first is `CamerasPage` still using `listCameras` for its own paging, exactly
as before. That was a stated constraint — the new endpoint goes *alongside* the
old one — and this is it observed rather than asserted.

### What the live check still did not show

**The truncation notice, in a browser.** It needs a fab above the 1000-camera
bound and this one holds 70, so the notice and its `aria-describedby`
association remain proven only at fixture level. Their *absence* was confirmed
live, which is the half that was observable here.

**Whether a 250-option dropdown is usable.** 70 options is not 250, and nobody
was asked. This stays unestablished, as §4's last rows say.

---

## 6. What is still not established

- **Whether a 250-option dropdown is usable** rather than merely correct. The
  live fab held 70, and nobody was asked.
- **The truncation notice in a browser.** It needs a fab above the 1000-camera
  bound; only its absence was confirmed live.
- **That 1000 is the right bound.** Four times the constitution target, chosen
  and not measured. What makes it safe is not the number but that reaching it
  is reported rather than hidden.

**Prefix type-ahead is a mitigation, not a solution.** Sorting alphabetically
— confirmed live — makes a native select's built-in keyboard jump genuinely
useful, which is why search could be deferred. It matches the *start* of a name
only, so a camera called `Line 2 Furnace` is not reachable by typing `furn`.
Tracked as its own issue.
