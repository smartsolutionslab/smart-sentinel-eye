# Phase 0 Research: Rename a camera from the management app

**Feature**: `035-rename-camera-ui` · 2026-08-25

Five questions. **The first was answered by rendering the thing rather than
reading the code**, which is the only reason its answer is trustworthy — and it
turned up one gap the spec's assumption had missed.

---

## 1. FR-008 — what an operator actually reads

The spec left this deliberately open: *does a taken name need new client
handling, or does the existing fall-through cover it?*

**Answered by running it.** All three refusals a rename can produce, through
`RefusalBanner`'s exact branching:

| Refusal | Rendered text |
|---|---|
| `CAMERA_NAME_TAKEN` | *"Another camera in fab 'munich' is already called 'line-4-inlet'. Names are unique per fab, ignoring case."* |
| `CAMERA_VERSION_STALE` | *"Someone else changed this while you were working. Reload to see their version, then reapply your change."* |
| `CAMERA_RETIRED` | *"This camera is retired. Retired cameras keep their record but cannot be changed."* |

**Decision**: **no new shared predicate.** The three are already distinct, and
each names its own remedy. The spec's assumption holds and FR-008's default
stands.

### The gap the assumption missed

Read FR-005 against that first row: *"a taken name MUST be reported in words that
say to choose a different one."*

The server's detail says the name is taken and states the rule. **It never says
what to do.** An operator infers the action; they are not told it. Compare the
overlay editor, which does say it: *"That overlay name is already taken. Choose a
different one."*

**Decision**: recognise `CAMERA_NAME_TAKEN` **at the call site** — following
`OverlayEditorDialog`'s precedent exactly, `problemCode(error) === '…'`, no
shared predicate — and render the server's detail **plus** the missing action.

**Rationale**: the server's sentence is *better* than a generic one, because it
names which name and which fab. Replacing it with *"That camera name is already
taken. Choose a different one."* would lose that. Appending the action keeps
both.

**Alternatives considered:**

- **Take the server's detail as-is.** Satisfies FR-008, fails FR-005 on a strict
  reading. Rejected — the requirement says *say*, not *imply*.
- **Use our own wording, as this dialog does for its two known refusals.**
  Consistent, and it throws away the specific name and fab the server supplied.
  The existing divergence exists because the *stale* detail was unusable
  (`"Camera '<guid>' is at version 9, not 7"`); this detail is not.
- **A fourth predicate in `problemDetail.ts`.** What issue 1873 floated.
  Unnecessary — one call site, and `isTerminalRefusal` only became shared
  because two dialogs needed it.

---

## 2. Mirror, do not extract

Two dialogs on one page with the same If-Match / RHF / Zod / refusal shape is
where extraction becomes tempting.

| File | Lines |
|---|---|
| `EditCameraAddressDialog.tsx` | 154 |
| `RegisterCameraDialog.tsx` | 124 |
| `RetireCameraDialog.tsx` | 79 |

**Decision**: mirror `EditCameraAddressDialog`. Do **not** extract a shared
edit-dialog.

**Rationale**: what looks shared is a *shape*, not a *behaviour*. The two dialogs
differ in their field, their schema, their mutation, and — after §1 — their
refusal branching. What would be left to extract is the Radix wrapper and a
submit button, and `Dialog` already is that.

Spec 032 built `ConfirmDialog` shared, and the difference is instructive: there
the **behaviour** was shared (a confirmation is the same interaction whatever it
confirms) and there was **no precedent to follow**. Here the behaviour differs
and the precedent exists.

**Alternatives considered**: a generic `EditFieldDialog<T>` parameterised over
schema, mutation and field. Rejected as speculative generality — it would be
built for two callers, and constitution §IX is explicit. Revisit at a third.

**Noted for the plan**: `RefusalBanner` is local to `EditCameraAddressDialog`
(not exported). The rename dialog needs its own, differing in the taken-name
branch. Two similar local functions is the honest cost of the decision above.

---

## 3. The name schema derives; it must not be restated

`apps/shared/src/api/cameras.schema.ts` already establishes the pattern, and
says why:

```ts
export const changeCameraAddressSchema = registerCameraSchema.pick({ rtspUrl: true });
```

> *"Derived from `registerCameraSchema` rather than restated. The rule … must not
> be able to differ between registering a camera and correcting one; picking the
> field out keeps a single definition instead of a second opinion that drifts."*

**Decision**: `renameCameraSchema = registerCameraSchema.pick({ name: true })`.
One line, established pattern.

### Two things this settles about FR-010

The existing name rule is
`z.string().trim().min(1, …).max(200, …)`.

- **`.trim()` is fine.** FR-010 forbids silent alteration *"beyond removing
  surrounding whitespace"*, which is exactly what it does.
- **There is no case normalisation anywhere in it**, and none must be added.
  A case-only correction is a real change that normalises identically; spec 033
  found that trap in the repository predicate, the aggregate's guard, **and** EF's
  change tracker. A client that helpfully lower-cased before sending would make
  it a fourth, and the symptom would be a rename that silently does nothing.

### A stale comment to fix

That file's doc comment says:

> *"No `name`: it is not editable (spec 029 FR-012, tracked as #1850), so there
> is nothing for a correction to carry."*

Spec 033 made it editable. Left as-is it tells the next reader the opposite of
the truth.

---

## 4. The header is getting crowded, and that is a real question

After spec 032 it holds *Correct the address*, *Retire camera*, and a *Back to
cameras* link. A rename makes four items.

**Decision**: a third button in the same row, ordered **Rename · Correct the
address · Retire camera · Back**, with the destructive one last before the link.

**Rationale**: consistency with the two controls already there, and three
buttons is not yet a menu. The alternative was better on its own terms and worse
on the repo's.

**Alternatives considered:**

- **An edit affordance on the name itself** — a pencil beside the heading. Genuinely
  better discoverability: an operator noticing the wrong name finds the control
  *on* the wrong name, rather than in a row of unrelated actions. **Rejected as a
  new interaction pattern**: this app has no inline-edit affordance anywhere, and
  introducing one for a single field is the kind of invention CLAUDE.md's "mirror
  existing patterns" rule exists to prevent. Worth revisiting deliberately.
- **A dropdown menu.** `@radix-ui/react-dropdown-menu` is available. Right answer
  at five or six actions; premature at three, and it hides them behind a click.

**Noted for the plan**: at a fourth control this row needs rethinking rather than
a fourth button. Recorded so the next person inherits the decision rather than
the pattern.

---

## 5. The e2e seam is ready

`e2e/camera-detail.spec.ts` has a local `registerCamera(page)` that arranges
state **entirely through the app**, plus four tests — including spec 032's
retire test, written after spec 030 *removed* one that reached around the UI to
call the API.

**Decision**: extend it with a rename test using the same helper. **No `fetch` to
the API**, per SC-005 and the recorded lesson.

---

## Summary of decisions

| # | Question | Decision |
|---|---|---|
| 1 | FR-008 | No shared predicate. Recognise the code at the call site and **append the missing action** to the server's detail |
| 2 | Extract? | **Mirror.** The shape is shared; the behaviour is not. Revisit at a third caller |
| 3 | Schema | `registerCameraSchema.pick({ name: true })`. No case normalisation, ever |
| 4 | Header | Third button, destructive last. A fourth needs a menu, not a fourth button |
| 5 | e2e | Extend the existing spec, drive the app |

**No backend change, no new dependency, no migration.**
