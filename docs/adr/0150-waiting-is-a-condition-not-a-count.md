# ADR-0150: Waiting is a condition, not a count

**Status:** **Accepted**
**Date:** 2026-09-15
**Extends:** ADR-0139 (rules that fail the build, not the review)
**Amends:** Constitution §Testing
**Supersedes:** —
**Superseded by:** —

## Context

On 2026-09-15 a single test file blocked every frontend pull request for most of
a working day. `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`
passed locally, 7 of 7, and failed on CI — four runs, three of them red, once
with four failing assertions. Two deliveries that could not possibly have caused
it (#2382 via PR #2384, #2379 via PR #2390) sat parked behind it. One of them was
wrongly labelled `agent:blocked` on the evidence available at the time.

The cause was `flushConnect()`: a fixed count of ten macrotask yields and five
microtask drains, used as a **synchronisation primitive** before a synchronous
assertion.

```ts
await flushConnect();                                  // N yields, fixed
expect(screen.queryByText('Connecting…')).toBeNull();  // assumes N was enough
```

Under CI contention the count ran out before React committed the effect that
cleared the placeholder. Spec 159 established this by reproduction rather than
argument: changing only that loop bound from `i < 10` to `i < 1` reproduced CI's
exact four failures — same names, same order, same messages — with no production
file touched. The budget ladder (1 → four failures deterministically; 2 → nought
to two, varying; 3 and above → always green) showed a latency threshold, not a
race.

**Three facts make this a governance question rather than a bug that was fixed.**

**It had already been fixed once, in the same file, and the lesson did not
generalise.** Commit `44ee5737` replaced exactly this pattern in `goLive` with
`waitForNewPeerConnection`, a deadline poll on the real condition, and wrote the
reasoning into the file. The remaining call sites in that same file were left on
the fixed count. The correct idiom was present, adjacent, and documented — and
the next author still reached for the wrong one.

**Nothing detects it before CI does.** The pattern is invisible locally by
construction: the budget is tuned until the suite is green on the author's
machine, which is the fastest machine the test will ever run on. Review is not a
reliable net either. Spec 159's own fix went through a dedicated reviewer, which
cleared the wait machinery as sound; a second review then found that one
converted assertion had silently given away a same-commit ordering guarantee —
the test had been made more patient and *less* discriminating, which is this
defect's mirror image. The first review was not wrong about what it examined; it
did not examine that. One more pass found one more thing, and nothing suggests
the sequence had terminated.

**The legitimate and illegitimate uses look identical.** A settle that *drives*
fakes forward is sound. A settle that *synchronises an assertion* is the defect.
Both spell `await flushConnect()`. Spec 159 deliberately kept five call sites on
the fixed count for the driving role.

There is also an uncomfortable fact about those five, which this ADR records
rather than hides: with the loop reduced to `i < 0` — an empty `act`, zero yields,
zero drains — the suite still passes 7 of 7. The kept sites contribute nothing
beyond a single act flush. Their stated justification (that they precede only
negative assertions) was also wrong when written, and was corrected during
review: four of the six precede positive assertions, which survive because
`rerender` is act-wrapped and React flushes cleanup synchronously. So the
population this rule must protect is smaller and less well understood than it
first appeared.

## Decision

Accepted 2026-09-15. The §Testing amendment in §4 and the rule in §2 bind from
this date; the ESLint rule itself is tracked by issue #2392.

### 1. The sanctioned idiom is a deadline poll on the condition

A test waits for a state by polling the condition against a wall-clock deadline,
not by yielding a fixed number of times. `waitUntil(condition, description,
timeoutMs)` in `CameraViewerCameraSwap.test.tsx` is the reference implementation;
`findBy*` / `waitFor` from Testing Library are equivalent and preferred where the
condition is a DOM query.

The deadline is a **failure bound**, not a wait: a correct implementation reaches
the state on the first poll on a fast machine and the hundredth on a loaded one,
and passes in both.

### 2. A fixed-count settle may not immediately precede an assertion

This is the enforceable half, and it is deliberately syntactic: a call to a
fixed-count settle helper may not be immediately followed by an assertion in the
same block. Driving fakes forward remains legal; synchronising an assertion does
not.

Enforced by an ESLint `no-restricted-syntax` rule over `**/*.test.{ts,tsx}`,
failing the build in the `frontend` bucket rather than warning.

**Amended 2026-09-15, on measurement taken while implementing this ADR (spec
161, issue #2392).** The adjacency rule above is kept, but it is not sufficient
on its own, and the reason is that the premise behind it was wrong. **This
amendment was a human decision, not the autonomous lane's** (ADR-0144
forbids the lane from amending an ADR): the architect escalated the choice
between shipping §2 as literally accepted and widening it as a blocking
decision at spec 161's phase-3 gate, and Heiko chose the widened selector
(option A+B) over shipping §2 as written.

`flushConnect` was assumed to be one helper. It is **one name with two opposite
semantics across six files**: a macrotask settle in
`CameraViewerCameraSwap.test.tsx` (the defect) and a microtask drain in five
sibling suites (sound). A name-keyed adjacency rule run over the real tree
produces **26 errors, 22 of them the sound drains** — precisely the outcome this
ADR rejected the blanket ban for. That collision is also the likeliest reason the
defect spread: a reader copying `flushConnect` from a neighbouring suite cannot
tell which one they copied.

Worse, once the defect is cleaned up the adjacency rule's population is **zero**,
and it is evaded by naming the helper anything else. It is a reserved-name guard,
not a bound.

So a second, **shape-based** selector is added — a `for` loop with a literal
bound containing `await new Promise(...)`:

```
ForStatement[test.right.type='Literal'] AwaitExpression > NewExpression[callee.name='Promise']
```

Measured population over the whole tree: **exactly one**, the broken helper. It
does not match the microtask drains (`Promise.resolve()` is a `CallExpression`,
not a `NewExpression`), nor `waitUntil`'s deadline poll (a `WhileStatement`), nor
`realWait` (no loop), nor `WhepClient.test.ts`'s bare timer awaits (no loop), nor
counted `fireEvent` loops (no `await`).

**This supersedes this ADR's own rejection of "ban it outright."** That rejection
rested on the claim that a ban "would condemn correct code" — the microtask
drains. Measured, it does not: the shape selector draws its line exactly where
the measurement draws it, without a name list. The probe that settles it is in
this ADR's Implementation Notes: with the macrotask helper's body emptied
entirely — no loop, no `act` — the suite still passes 7/7, while blanking the
sound helpers' bodies fails 5 tests. The instrument this bans is inert; the one
it spares is load-bearing.

### 3. What the rule cannot see is written down, not assumed away

The rule is **necessary, not sufficient**. It cannot see through a helper that
wraps the settle, and it cannot tell a condition that must arrive from one that
is already true — spec 159 shipped, and review caught, a `waitUntil` whose
condition held on entry, so the loop never iterated. That is a settle that cannot
fail wearing the sanctioned idiom's clothes, and no syntactic rule will find it.

Reviewers keep that obligation explicitly. Recording the limit is the point:
§II drifted twice, the Phase 3 board gate drifted for sixteen specs, and §IV
recorded a built leg as unbuilt — each time because a rule's scope was assumed
rather than stated.

### 4. §Testing gains one sentence

> A test waits for a condition, never for a count. A fixed number of yields is
> not a bound on work that advances in another phase of the event loop.

## Consequences

**A class of CI-only failure becomes a local build failure.** The defect's
signature is that it is invisible on the author's machine; the lint rule moves
detection to the place the author already looks.

**Some legitimate driving code must be rewritten or annotated.** The five kept
sites in `CameraViewerCameraSwap.test.tsx` are the known population. Given the
`i < 0` result, the likeliest outcome is that most simply disappear.

**The rule can be satisfied without being obeyed** — via a wrapping helper, or a
condition that is already true. This is stated in §3 rather than left for
discovery, and it is the reason the CI-budget job stays on the table.

**A false positive is cheap; a false negative costs a day.** That asymmetry is
why this proposes enforcement over advice, and it is measured, not assumed: one
day of blocked frontend delivery, four CI runs, two parked PRs, one incorrect
`agent:blocked` label, and three separate wrong diagnoses before the reproduction
settled it.

## Alternatives Considered

**Advise it, with a measured baseline** — the `Option<T>` treatment (ADR-0141):
document the rule, record the count, re-measure before claiming it holds. This
is the option with the worst track record in this repository. Every rule that has
drifted here was advisory, and this one has already failed the weaker test: it
was documented *in the file it governs*, by the commit that fixed its first
instance, and the very next call site ignored it. An advisory rule assumes the
next author reads the neighbouring code. That assumption is already falsified.

**Run the suite in CI at a reduced settle budget**, permanently — a job that
executes the frontend tests with every fixed-count settle forced to 1. This is
strictly stronger than the lint rule: it catches the defect *class* by behaviour
rather than the *shape* by syntax, including through helper indirection, and it
is exactly the harness that proved the diagnosis. Rejected for now on cost and
blast radius: it needs the budget to be injectable across every suite, it doubles
the frontend bucket's runtime, and a single genuinely slow-but-correct test would
red the build for a reason nobody could act on. **Worth revisiting** if the lint
rule proves insufficient — it is the honest upgrade path, not a discarded idea.

**Ban the fixed-count settle outright.** Tempting given that the five kept sites
demonstrably do nothing (`i < 0` passes). Rejected because "they do nothing in
*this* file today" is not "no suite ever needs to drive a fake forward", and
because the six sibling suites in `apps/shared` settle with microtask-only drains
that are genuinely sound — N microtask rounds *do* bound an N-deep microtask
chain, with no wall-clock dependence. A blanket ban would condemn correct code.

**Overturned by the amendment in §2 (2026-09-15).** This rejection was right
about the microtask drains and wrong about the inference: a ban keyed on the
*shape* of the broken instrument spares them, because they are a different shape.
The ban stands for `await new Promise(...)` inside a literal-bound loop — one
site, measured — and not for the drains. Left here rather than rewritten, because
what this ADR got wrong before it was implemented is the useful part of the
record.

**Do nothing; the tests are fixed.** Rejected: the same defect has now been fixed
twice in one file, by two different commits, with the second fix arriving only
after it had blocked unrelated delivery.

## Implementation Notes

- The rule belongs in the frontend ESLint config, scoped to test files, failing
  the `frontend` bucket. It is not a `dotnet` analyzer question.
- ~~`CameraViewerCameraSwap.test.tsx` is the reference for both halves: `waitUntil`
  for the sanctioned idiom, and the corrected `flushConnect` docblock for the
  honest statement of why a driving settle is safe.~~ Stale (#2392 phase 6
  finding 5): commit `e1d07122` deleted `flushConnect`, its docblock, and all
  six call sites — the shape it fixed turned out to be inert (see the `i < 0`
  observation above). `waitUntil` in that file is still the reference for the
  sanctioned idiom; there is no longer a corrected `flushConnect` docblock to
  point at. Struck rather than rewritten, in keeping with this ADR's own
  practice of leaving a wrong record visible (see "Overturned by the
  amendment in §2" above).
- Do **not** implement this before the ADR is accepted. Issue #2392 tracks it,
  and was deliberately filed rather than implemented because ADR-0144 forbids the
  autonomous lane from making architectural decisions.
- The `i < 0` observation should be re-run before implementation. If the five
  kept sites still contribute nothing, delete them and the rule's exception
  population is empty — which would make "ban it outright" viable after all.
