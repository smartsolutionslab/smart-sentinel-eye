# Quickstart: auditing one claim, and checking one

**Feature**: `047-the-decisions-we-made` · **Plan**: [plan.md](./plan.md)

Two procedures. The first is how the audit is performed; the second is how a
reviewer — or a reader in a year — confirms a verdict without redoing the work.

---

## Read this first: the way this goes wrong

**The failures are more interesting than the passes.** Twenty-seven decisions,
most of them probably fine, and every hour spent recording "holds" feels wasted
next to finding another StreamKeeper.

That feeling is the risk. An audit that records only its discoveries **cannot be
distinguished from one that stopped when it got bored** — and the next reader has
no way to tell which they are holding. FR-004 exists for this: a row that holds
is recorded as *checked*, with its evidence, exactly like one that fails.

---

## 1. Auditing one claim

**Step 1 — split the row into claims.** One assertion each. Decision 005 is three
claims (RTSP; ONVIF; vendor adapter pattern), not one, and they took three
different verdicts.

**Step 2 — search for the name.** The component, interface or technology exactly
as the decision writes it.

```sh
grep -ril "IRuleEngine" src/ tests/
```

**Step 3 — if absent, search for the job.** Different terms: what would this look
like if someone built it and called it something else?

```sh
# not "IRuleEngine" but the shape of a pluggable engine
grep -ril "RuleEngine\|IRuleStrategy\|EngineTag" src/ tests/
```

**Both must fail before recording *not built*.** One search answers "is this
string present", which is a different question from "does this exist". This repo
has a standing rule about it, learned from a miss.

**Step 4 — choose a verdict** from the four in [data-model.md](./data-model.md).
The one that takes care is **diverges vs not built**: is the job being done under
another name, or not at all?

**Step 5 — record the command and its output.** Not your conclusion. The claim
you are auditing *is* somebody's undocumented conclusion, which is why this
feature exists.

**Step 6 — give it a disposition** if it does not hold: an ADR to legitimise, or
an issue to correct. **If it is not obvious, write the issue** — the cheaper
mistake. An unnecessary issue gets closed; an ADR that legitimises a mistake
makes it policy.

---

## 2. Checking somebody's verdict

**Pick a row at random.** Not one of the interesting ones — the point is to test
whether the boring rows were really done.

1. Re-run the recorded commands. Do they still return what the audit says?
2. For any *not built*: **is there a second search?** If there is only one, the
   verdict is unsupported regardless of whether it is correct.
3. For any *diverges*: does the record say what the system does *instead*? A
   divergence without the alternative named is half a finding.
4. For any *holds*: is there evidence, or just an assertion? A row marked holding
   with no command recorded is the failure mode this whole procedure is about.

---

## 3. Checking the guard

The guard protects **corrected** claims, not all 27 rows.

- Restore a corrected claim to its old wording. **The build must fail**, and the
  message must say what is wrong and why it matters — not that a string is
  missing.
- Then make a *legitimate* edit to the same row — updating it because the system
  changed. **The build must pass.** A guard that blocks real updates gets deleted,
  and takes the useful part with it.

---

## 4. What this cannot tell you

- **Whether a decision was right.** The audit records that AEL exists where CEL
  was decided. It says nothing about which is better, and FR-008 forbids it
  from trying.
- **Whether a fab is wired as decision 013 says.** Unverifiable from this
  repository, and recorded as such rather than guessed.
- **Whether the other ~100 ADRs are accurate.** Out of scope, and the method
  here would apply to them. ADR-0117's table and ADR-0026's abandoned stack
  suggest the problem is not confined to ADR-0000.
