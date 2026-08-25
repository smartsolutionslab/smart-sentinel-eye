# Phase 0 — Research: recovering an archived layout or overlay

**Feature**: `037-recover-archived-revision` · **Spec**: [spec.md](./spec.md)

Seven questions the plan refused to assume. Two answers make the work smaller
than the spec expected, one makes it larger, and one turned up a defect that is
**adjacent, not in scope** — raised rather than absorbed.

---

## 1. FR-009's name check needs no `excluding` parameter

**Decision**: call the existing `GetByNameAsync` unchanged, from inside the
fully-archived branch only. Any hit is necessarily a different chain.

**Rationale**: both repositories filter
`.Where(c => c.Revisions.Any(r => r.State != Archived))`. The chain being
recovered is, at the instant of the check, fully archived — so it is **excluded
from its own lookup by construction**. There is nothing to exclude by parameter.

This is the opposite of spec 033, where `ExistsByNameAsync` needed an
`Option<CameraIdentifier> excluding` because the camera being renamed was itself
live and would match its own name. That spec deliberately took **one** method
with a parameter rather than two methods, because the predicate had already
drifted once. Here the equivalent temptation — adding an `excluding` parameter
"for symmetry" — would add a parameter that is always ignorable, and a parameter
that can be passed wrong is worse than one that does not exist.

**The caveat is load-bearing and must survive into the code.** The exclusion is
structural only while the chain is fully archived. If this check were ever hoisted
out of the fully-archived branch and run on the ordinary published-branch path, it
would match the chain against itself and refuse every branch. The check belongs
*inside* the recovery branch, and the reason belongs in a comment there.

**Alternatives considered**: adding `excluding` to both repositories (rejected —
a parameter with exactly one correct value at every call site); checking the name
in the domain (rejected — the aggregate cannot see its siblings, which is why
uniqueness has always been an application-level rule here).

---

## 2. FR-009 guards a hole this feature opens — it is not reachable today

**Finding**: the sequence is *archive the sole published revision* → chain is
stranded and its name goes free → *another chain claims the name* → *recover the
first*. The final step is refused today by the very guard this feature removes, so
the duplicate cannot currently be produced.

**Consequence**: FR-009 is not a bug fix. It is a guard on surface this feature
creates, and the tests for it are new-behaviour tests rather than regression
tests. Worth knowing before anyone goes looking for the existing defect it
supposedly fixes.

**It is genuinely reachable once shipped**, and by an ordinary sequence: an
operator archives a wall, a colleague rebuilds it under the same name not knowing
the original could be recovered, and the original is then recovered. Nothing
downstream would catch it — uniqueness is enforced only on create, and the
database index over the name is **not** unique in either context
(`ix_layouts_fab_name`, `ix_overlays_name`; the layout configuration's own comment
records that promoting it is a separate decision, on data that may already violate
it).

---

## 3. The two scopes are different, and the spec's "same scope" resolves to each

| Aggregate | Name scope | Lookup |
|---|---|---|
| Layout | **Per fab** (spec 017 FR-019) | `GetByNameAsync(fab, name)` — the fab is not optional, because answering for another fab's layout would confirm it exists |
| Overlay | **Global** | `GetByNameAsync(name)` — no fab parameter exists |

**Decision**: FR-009's check uses each context's own lookup as-is. The layout's
check passes the recovering chain's own `Fab`. This is not a divergence between
the twins — it is the twins faithfully reflecting a difference that already
exists in their name rules.

---

## 4. `newest.state === 'Archived'` is **not** a sound test for stranded

**Decision**: the frontend predicate is `revisions.every(r => r.state === 'Archived')`.

**Rationale**: `newest` is the highest-numbered revision, and a chain can hold a
Published revision under an archived newer one. Concretely: publish r1, branch r2
as a draft, abandon r2. Now `newest` is r2 (Archived) while r1 is still Published
and still on kiosks. That chain is **not** stranded, and offering it a recovery
would branch from an abandoned draft while a published revision sits underneath.

The domain-side predicate is the same shape and for the same reason: *no
Published and no Draft*, which the spec establishes is equivalent to *every
revision Archived*. Both ends of the stack test the chain, not its last row.

---

## 5. Adjacent defect: that same chain shape offers **no actions at all** — raise, don't absorb

While establishing #4, the counter-example turned out to be a live dead-end in the
management app, independent of this feature.

`LayoutsPage` and `OverlaysPage` gate every row action on `newest.state`:

| Condition | Action |
|---|---|
| `newest.state === 'Draft'` | Publish |
| `newest.state === 'Published'` | Edit (new draft), Revert |
| `newest.state !== 'Archived'` | Archive |

A chain with a **Published r1 and an abandoned Draft r2** has `newest.state ===
'Archived'`, so it matches none of them. The row offers nothing. The layout is
live on kiosks and cannot be edited, reverted or archived from the app at all —
and it is reachable in two clicks: archive the draft the app itself offers to
archive.

**Not in scope for 037.** It is not a stranding — the domain will still act on
that chain perfectly well; only the UI hides the door. Fixing it means the row's
actions keying off the chain's whole revision set rather than its last row, which
is a different change with its own decisions about what a row should offer when
several states coexist. **Filed separately as issue 1879.** This spec's SC-001 is about
stranding, and stretching it to cover this would widen the feature by stealth.

---

## 6. The frontend edit path is **smaller** than the plan predicted

**Finding**: `LayoutsPage` already calls `onEdit(chain, newest)` — it passes the
newest revision, not "the published one". The parameter is merely *named*
`published`, and its type is `LayoutRevision` either way. `LayoutEditTarget`
wants `{ layoutIdentifier, revisionNumber, name, grid, tiles }`, and an archived
revision carries a grid and tiles exactly as a published one does.

So the recovery path needs the gate changed and the parameter renamed to say what
it is. No new dialog, no new prop, no second code path. The plan's expectation
that "the frontend edit path takes a published revision as an argument it will not
have" was **wrong** — it takes the newest revision and always did.

`OverlaysPage` is simpler still: its Edit button calls `branchDraft` directly and
opens no editor with a baseline, so only its gate changes.

The dialog itself needs nothing. It re-reads the chain's current version after the
branch rather than inferring it, so it is already indifferent to what the branch
was taken from.

---

## 7. Exactly two existing tests cannot pass, and both are spec 036's

**Decision**: change these two, and only these two, on the frontend:

| File | Test | Why it must change |
|---|---|---|
| `apps/management-web/src/features/layouts/LayoutsPage.test.tsx:319` | *Says the layout can never be edited or published again* | Asserts `/never be edited or published again/i`, which FR-011 makes false |
| `apps/management-web/src/features/overlays/OverlaysPage.test.tsx:228` | *Says the overlay can never be edited or published again* | Same assertion, same reason |

Both are spec 036's T014 — the assertion that spec deliberately proved fires, by
softening the wording and watching exactly one test go red. **They are being
changed because the truth changed, not because they are inconvenient**, and the
replacement must assert the new claim as specifically as the old one asserted the
old. Softening either into "cannot be undone" would satisfy the removal and lose
the check, which is precisely the failure spec 036 built T018 to prevent.

**The four backend refusal tests named in SC-005 stay untouched.** All four build
on draft-only chains, which this feature deliberately leaves refused. That they
pass unchanged is the evidence the narrowing is right; if implementation finds
itself editing one, the change went wider than intended and that is a finding, not
a fix.

Nothing in `e2e/` asserts anything about archiving or the edit action — `grep -i`
over both spec files returns nothing.

---

## 8. An integration test over real SQL is required

**Decision**: one recovery test per aggregate in
`tests/Integration.Tests/LayoutComposition/LayoutLifecycleIntegrationTests.cs` and
`tests/Integration.Tests/OverlayDesigner/OverlayRevisionLifecycleIntegrationTests.cs`,
driving archive → branch → edit → publish through the API against the Aspire
stack.

**Rationale**: the recovered draft is built by **cloning an archived revision's
EF-owned entities** and saving them under a new owner in the same change-tracker.
`Revision.NewDraft` carries a comment explaining that this cloning exists because
reusing the instances makes EF see one owned entity under two owners and throw on
save — written for the published-source case. Whether it holds when the source
revision is archived, in the same unit of work that just loaded it, is exactly the
class of question a hand-written fake repository answers by construction and
therefore cannot answer at all.

Spec 033 is the precedent and the warning: its normalisation trap appeared in
three layers where its plan predicted one, and the third — an EF `ValueComparer`
— was caught only by the integration test over real SQL. Handler tests over a
fake repository were green throughout.

**Alternatives considered**: handler-level tests only (rejected — the risk is
specifically in EF's ownership tracking, which the fakes do not model);
a persistence-layer test without the API (rejected — the API path is where the
three guard layers are exercised in sequence, and exercising them in sequence is
the point).

---

## 9. ADR-0121 is the next free number, and the record is warranted

**Decision**: write `docs/adr/0121-archived-is-out-of-service-not-unreachable.md`.

`docs/adr/` runs to 0120 (`0120-name-mutability.md`). 0121 is free.

**Rationale**: this settles what *archived* means for a revisioned aggregate, and
that meaning outlives the four lines of code enforcing it. It also needs to be
findable by whoever adds the third revisioned aggregate, because ADR-0104's
rule-of-three revisit trigger points at exactly that moment.

It must be consistent with two existing records:

- **ADR-0104** keeps `Layout` and `Overlay` as deliberate twins and instructs that
  a lifecycle change in one be checked against the sibling. This change lands in
  both, and the ADR should say the twin rule was honoured rather than leave a
  reader to check.
- **ADR-0120** reasons about when a thing may change versus when it is addressed
  by what it holds. The parallel worth drawing: archived is a *state*, not an
  *address*, so nothing identifies the chain by its archived-ness and nothing
  breaks when it stops being archived.

The record should also state the thing that is easy to lose: the existing
precedent that `Revert` raises the archived event **without archiving anything**,
purely to send kiosks away. That event has always meant *stop showing this*, never
*this is dead* — so this decision reads the existing design rather than
overturning it.
