# Phase 0 — Research: deriving the row from its chain

**Feature**: `038-chain-derived-row-actions` · **Spec**: [spec.md](./spec.md)

Seven questions the plan refused to assume. The first answer is the one that
matters: **the spec's five chain shapes are not all of them.**

---

## 1. The shape space is larger than five, and the extra shapes were reachable all along

**Finding**: enumerating by construction rather than by inspection gives **eight**
reachable shapes, not five. Two of the three extras have no Published revision at
all, and one holds **two open drafts**.

The operations and their preconditions:

| Operation | Requires | Effect |
|---|---|---|
| Create | — | new chain, r1 Draft |
| Branch | a Published revision, **or** a fully-archived chain | new Draft at max+1 |
| Publish(n) | n is Draft | n → Published; **the prior Published** → Archived |
| Revert(n) | n is Published | n → Draft |
| Archive(n) | n exists | n → Archived |

**`Publish` archives only the prior *Published* revision.** Other drafts are
untouched — verified in `Layout.Publish`, which takes `CurrentPublishedOrNull()`
and archives that one alone. So drafts accumulate rather than being collapsed.

Writing shapes as multisets over {Draft, Published, Archived}:

| # | Shape | Reached by | Today's row |
|---|---|---|---|
| 1 | `{D}` | create | Publish, Archive |
| 2 | `{P}` | 1 → publish | Edit, Revert, Archive |
| 3 | `{A}` | 1 → archive | Edit *(spec 037)* |
| 4 | `{P, D}` | 2 → branch | Publish, **Archive → the draft** |
| 5 | `{P, A}` | 4 → archive the draft | **nothing** |
| 6 | `{A, D}` | 4 → archive the published; or 3 → branch | Publish, Archive |
| 7 | `{D, D}` | 4 → **revert the published** | Publish, Archive |
| 8 | `{P, D, D}` | 7 → publish one → branch | Publish, **Archive → a draft** |

Shapes **6, 7 and 8** are new to this analysis. Shape 7 is the sharp one: from a
published chain it is *branch, then revert* — two clicks, both offered by the
row — and it leaves a chain with **two open drafts and nothing published**.

**Consequence for the design**: a chain-derived model must not assume at most one
draft, and must not assume a chain has a Published revision merely because it has
history. Both assumptions are easy to write and both are false.

**Alternatives considered**: trusting the spec's five (rejected — the defect being
fixed is precisely a shape nobody enumerated, and repeating that method would
have shipped shapes 6–8 unexamined).

---

## 2. The model: one descriptor per chain, not per-action predicates

**Decision**: a single helper per page returning a small descriptor:

```ts
{ live, draft, newest, summarised }
```

- **`live`** — the Published revision, or undefined. At most one, enforced by the
  aggregate.
- **`draft`** — the **newest** Draft, or undefined. Newest rather than first,
  because it is the one the operator most recently worked on, and because it
  matches what the row does today for the single-draft case.
- **`newest`** — the highest-numbered revision. Needed only as the branch source
  for a fully-archived chain, which is what the service branches from.
- **`summarised`** — `live ?? draft ?? newest`. The revision the row *describes*
  (badge, tile summary, label preview), which is a different question from what
  each action *targets*.

Every action then reads off the descriptor:

| Action | Offered when | Targets |
|---|---|---|
| Publish | `draft` | `draft` |
| Discard draft | `draft` | `draft` |
| Edit (new draft) | `live` **or** fully archived | branches; opens from `live ?? newest` |
| Revert | `live` | `live` |
| Archive | `live` | `live` |

**Fully archived** is `!live && !draft` — no separate predicate. Spec 037's
`isFullyArchived` disappears into the model rather than sitting beside it, which
is FR-012.

**Checking FR-001 against §1's eight shapes** — every one offers at least one
action:

| Shape | Offers |
|---|---|
| `{D}` | Publish, Discard draft |
| `{P}` | Edit, Revert, Archive |
| `{A}` | Edit |
| `{P, D}` | Publish, Discard draft, Edit, Revert, Archive |
| `{P, A}` | Edit, Revert, Archive ← **the filed defect, fixed** |
| `{A, D}` | Publish, Discard draft |
| `{D, D}` | Publish, Discard draft |
| `{P, D, D}` | all five |

**Alternatives considered**: per-action predicates like `canRevert(chain)`
(rejected — five near-identical scans per row, and five places for the
live-revision rule to drift, which is the class of bug being fixed); keeping
`newest` and adding conditions (rejected — that is what produced the defect).

---

## 3. Extraction is allowed here, and advisable — but the reasoning is not ADR-0104's

**Decision**: extract the descriptor into
`apps/management-web/src/features/chainView.ts`, generic over a structural
`{ revisionNumber, state }`, used by both pages.

**ADR-0104 does not apply.** It governs the *backend* bounded contexts —
LayoutComposition and OverlayDesigner are separate deployables with a
no-cross-reference rule, and duplication there buys isolation. These are two
components inside one frontend app with no boundary between them, and
`apps/management-web/src/features/ArchiveConfirmation.tsx` is already a
cross-feature file in exactly this position. So the twin rule neither forbids nor
requires this.

**Whether it is advisable is the spec-035-versus-036 question**, and this is
firmly the 036 case. Spec 035 rejected extraction for two dialogs that shared a
*shape* — a form, a schema, a mutation — while their behaviours differed. Spec
036 extracted for four callers whose *behaviour* was identical and only whose
words differed.

Here the two pages share the **rule**, not the shape: which revision is live,
which draft is current, what the row describes. If the two copies drift, one page
misidentifies the live revision — which is the exact defect this feature exists
to remove. FR-014 asks for identical behaviour across the twins; extracting makes
that true by construction rather than by discipline.

The payload types differ (`LayoutRevision` carries a grid and tiles, the overlay's
carries a label), which is why the helper is generic over the two fields it
actually reads and returns the caller's own revision type.

---

## 4. The word is **"Discard draft"**, and the domain's own word was rejected on purpose

**Decision**: the action reads **Discard draft**; the confirm button reads
**Discard**.

**Rationale**:

- It says what the operator is doing without claiming more than happens. Nothing
  is deleted — the revision persists as Archived — so *Delete* would be false.
- It pairs cleanly against **Archive**, which is now unambiguously about the live
  revision. Two words, two subjects, no overlap. FR-005 exists because one word
  did two jobs.

**"Abandon" was the obvious candidate and is rejected.** Spec 004 FR-003 names the
`Draft → Archived` transition *Abandon*, so it is the domain's own word and would
normally win. Against it: `Abandoned` is already a distinct concept in this
codebase — `IIngestCompletion.AbandonedAsync` is MQTT delivery abandonment in
EventIngestion — and the word is stilted in a button. The transition keeps its
domain name; the button gets the operator's.

Recorded here so a later reader does not read the mismatch as an oversight.

**The confirmation component takes a `verb`.** `ArchiveConfirmation` hardcodes
`title={`Archive ${subject}?`}` and `confirmLabel="Archive"`. It gains an optional
`verb` defaulting to `'Archive'`, used for both.

That is not a misnomer creeping in: **both actions archive a revision**
server-side. The component confirms archiving a revision; the verb names it in
the operator's terms. Renaming it to something like `DestructiveConfirmation` was
considered and rejected as scope — it would reach into the rules and system-variable
callers this feature does not touch. Worth revisiting if a third verb appears.

---

## 5. The badge, per shape — and it keeps the two e2e assertions matching

**Decision**:

| Shape | Badge |
|---|---|
| Has a live revision, no draft | `v{live} · Published` |
| Has a live revision **and** a draft | `v{live} · Published · draft v{draft}` |
| No live revision, has a draft | `v{draft} · Draft` |
| Fully archived | `v{newest} · Archived` |

**Two e2e assertions read this text** — `e2e/layouts.spec.ts:56` and `:130`, both
`getByText(/Published/)` on a row after publishing. Both chains are shape `{P}` at
that moment, whose badge is unchanged, so both keep matching. The badge change
also makes them *more* honest: today a chain with a live revision under an
archived newer one renders `Archived`, and an assertion like those would have been
reading a false state.

**No unit test reads the badge.** Grepping both page test files for assertions
containing `Published`, `Draft`, `Archived` or a `v{n}` string returns nothing —
every existing state assertion is about a confirmation's text or a button's
presence.

The layout's tile summary and the overlay's label preview both switch to
`summarised`. Their existing tests use single-revision chains, where
`summarised === newest`, so they are unaffected.

---

## 6. The confirmation's `published` flag becomes a constant, and is removed

**Finding**: both pages carry `published: newest.state === 'Published'` in the
pending-archive state, and render the kiosk sentence only when it is true. Once
Archive targets the live revision, it is offered **only** when a live revision
exists, so the flag is true every time the confirmation opens.

**Decision**: remove it and render the kiosk sentence unconditionally in the
archive confirmation. A flag that is now always true is worse than no flag — it
reads as a live condition and invites a future caller to set it false.

The conditional does not disappear from the feature; it moves to being a
difference between **two confirmations**. Archive always warns about kiosks;
discard-draft never does, because no kiosk is showing a draft. Spec 036's FR-008
asked for the warning not to fire when it does not apply, and this satisfies it
structurally rather than by a branch.

---

## 7. Exactly two existing tests cannot pass, and both anticipated this

| File | Test | Why |
|---|---|---|
| `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` | *Does not treat a published revision under an abandoned draft as recoverable* | Asserts shape `{P, A}` offers **no** edit button. It now does. |
| `apps/management-web/src/features/overlays/OverlaysPage.test.tsx` | *Does not treat a published revision under an abandoned draft as recoverable* | Same. |

Both are spec 037's, and both carry a comment saying issue 1879 covers the
separate problem — the change was foreseen when they were written.

**They are rewritten, not deleted, and the substantive claim survives.** What they
exist to prevent is branching from the *abandoned draft* instead of the published
wall. Asserting a button's absence was the only way to say that while the button
did not exist. Now the stronger form is available: assert that editing that chain
opens from the **published** revision's configuration. That is the claim; the
absence was a proxy for it.

Nothing else in either suite asserts anything this changes.

---

## 8. FR-003 keeps Edit offered while a draft is open, and that is the app's route to two drafts

**Observed, not fixed.** FR-003 says a draft revision above the live one must not
remove Edit. Follow it — but the consequence should be visible rather than
discovered: offering Edit on shape `{P, D}` is how an operator reaches shape
`{P, D, D}`.

Suppressing Edit while a draft is open would remove the app's route to multiple
drafts without changing anything the service permits, and it has an argument in
its favour: the open draft *is* the edit in progress. It is not done here because
the spec decided the opposite explicitly, and quietly reversing a stated
requirement during implementation is worse than an extra button.

If it turns out to matter in use, it is a **finding to raise, not absorb**.

---

## 9. No ADR, stated rather than assumed

This decides no domain question, reverses no recorded decision, changes no
contract, and touches no service. It corrects an app that was asking the wrong
question of data it already had.

ADR-0121 already settled what an archived chain means, and §2's model reads that
decision rather than revisiting it — the fully-archived branch of the model *is*
ADR-0121's rule, expressed as an absence of live and draft revisions instead of a
special case.

Recorded here and in the spec's Assumptions so a later reader does not go looking
for the record that would explain the change.
