# Plan 160 — A disable that keeps its focus

**Phase:** 2 (Plan) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Issue:** #2387
**Base:** `develop` at `b7c8332b`. **Engineer:** `frontend-engineer`.

---

## 1. Context and layers

**Frontend only. There is no bounded context in this change.**

Nothing in `src/` is touched: no Domain, no Application, no Infrastructure, no
Api, no `Shared.Kernel`, no `Shared.Contracts`, no migration, no Wolverine
message, no domain or integration event. No .NET project changes, so
**NetArchTest's cross-context boundary rules are not engaged** and no
`PrimitiveBoundaryTests` / `HandlerDeconstructionTests` obligation arises. No
value object, no `Result<T, Error>`, no `Option<T>`, no `Ensure.That` — none of
constitution §II's surface is reached.

The whole change lives in three React files across two packages:

```
apps/management-web/src/features/overlays/OverlayEditorDialog.tsx   US1 + US2 + US3 wiring
apps/management-web/src/features/layouts/LayoutEditorDialog.tsx     US1 + US2 + US3 wiring + FR-011 comment
apps/shared/src/ui/composites/ChainRecoveryNotice.tsx               US3 only
```

`apps/kiosk-web` is untouched. `apps/shared/src/ui/primitives/Button.tsx` is
untouched (FR-006) and `Dialog.tsx` is untouched.

**Why `apps/shared` is in scope at all**, given spec 158's plan deliberately kept
it out: US3's defect *is* in `ChainRecoveryNotice`. Its announcement is written
inside `activate()` (`:165`), which only a click reaches. No amount of wiring in
the dialogs can make an unrequested re-read announce itself without the component
reacting to `reReading` on a path where `origin === null`.

## 2. The state this change reasons about

There is no aggregate and no invariant in the DDD sense. There is a **gate** — a
derived boolean — and the whole plan is about making it single-sourced and making
both of its consumers honour it.

| Term | Source | Meaning | Story |
|---|---|---|---|
| `isLoading` | the edit/create mutation (`OverlayEditorDialog.tsx:95`) | a write is in flight | US1 (pre-existing) |
| `currentChain === undefined` | `useGetOverlayQuery(...).currentData` | no version has been read for this target | pre-existing |
| `chainFetching` | `.isFetching` | a read of the same target is in flight, so the held version is known-stale | pre-existing (spec 153/158) |
| `chainFailed` | `.isError` | the last read settled and was **refused**; the held version is stale and cannot be replaced without a Retry | **US2, new** |
| `knownCameras.size === 0` | layout dialog only | nothing to assign | pre-existing |

**The invariant this gate exists to hold** (ADR-0113, client half): *the only
version ever submitted as `If-Match` is one that was read from the server and has
not since been superseded or invalidated.* `chainFailed` is the missing term:
today a refused re-read leaves a version that is known-superseded and cannot be
confirmed, and the gate opens on it.

## 3. The change, per file

### 3.1 `OverlayEditorDialog.tsx`

**a. One named local, replacing the inline predicate** (FR-002, FR-007). Placed
next to `saveRef` (`:240`), after `offerReload` (`:228`):

```
saveBlocked = isLoading || (isEdit && (currentChain === undefined || chainFetching || chainFailed))
```

`chainFailed` is **already bound** at `:91`. No new hook, no new state, no new
import for US1/US2.

**b. The button** (`:310-316`) — `disabled={...}` becomes `aria-disabled={saveBlocked}`,
plus the `className` from FR-005. `Button` spreads `...rest` onto the native
element (`Button.tsx:36-40`) and its props are `ComponentPropsWithRef<'button'>`
(`:11`), so `aria-disabled` passes through with no primitive change.

**c. The submit guard** (FR-003). The form (`:258`) currently wires
`onSubmit={onSubmit}` directly to `handleSubmit(...)`. It gains a wrapper that
short-circuits **before** `handleSubmit` runs:

```
if (saveBlocked) { event.preventDefault(); return; }
```

This is the only defence once the button is clickable, and it covers both the
click and the Enter-in-a-field route in one place.

> **Type trap, already paid for twice in this repo.** Naming `HTMLFormElement`
> trips `no-undef` — this app's eslint config has no per-tag DOM lib globals, the
> exact reason `saveRef` is `ComponentRef<'button'>` and not `HTMLButtonElement`
> (`OverlayEditorDialog.tsx:235-239`, and spec 154's own e2e fix `bc30486f`).
> Type the handler through `ComponentProps<'form'>['onSubmit']` or
> `FormEvent<ComponentRef<'form'>>`. **Widening the eslint config is the
> gate-weakening ADR-0144 rules out** — do not.

**d. `onSubmit`'s existing `currentChain === undefined` guard** (`:189`) stays.
Its comment says it is *"defensive rather than reachable through the UI"*; that
stops being true when the button is clickable, so the comment is corrected while
the guard is kept. Two guards is right here: **c** is the gate, **d** is the type
narrowing `currentChain.version` needs at `:194` regardless.

**e. `previouslyRead={currentChain !== undefined}`** onto `ChainRecoveryNotice`
(`:291-298`). US3 only.

### 3.2 `LayoutEditorDialog.tsx`

The same five changes, at `:93`/`:89` (bindings), `:261` (local), `:322` (form),
`:376-384` (notice), `:418-424` (button), with two differences:

- the extra `knownCameras.size === 0` term joins `saveBlocked` (spec §11 A1);
- the `onSubmit` guard at `:221` is a silent `return` today with no explanation;
  keep it, and keep it silent — improving it is a different change.

Plus **FR-011**: the comment block at `:389-417`, which the button change already
disturbs, has its two `412`s corrected to `409`. The block is also the right place
for the one-line `why` on the new mechanism; it must get **shorter**, not longer —
it is 29 lines of comment above a 6-line button and the file is under a 300-LOC
cap (ADR-0084). Point at `ChainRecoveryNotice.tsx:31-33` for the `aria-disabled`
reasoning rather than restating it.

### 3.3 `ChainRecoveryNotice.tsx` (US3 only)

**a. One new prop**, documented in `ChainRecoveryNoticeProps` alongside the other
six:

```
/** `currentChain !== undefined` — the chain has been read at least once, so a
 *  fetch in flight is a RE-read and not this dialog's first read (FR-010). */
previouslyRead: boolean;
```

**b. The rising edge.** Today nothing reacts to `reReading` going false→true.
The existing settle effect (`:88-132`) already keeps `wasReReadingRef` and
already computes the edge; the rising-edge branch belongs in that same effect,
not in a second one — two effects racing the same ref is how the ordering bug in
`CameraViewer`'s camera swap was introduced. Concretely, before the `:91` bail-out:

- `!wasReReading && reReading && origin === null && previouslyRead`
  → write `Re-reading the {noun}…` (the same string `:165` writes; extract it to
  one helper so the two cannot drift).

**c. The falling edge.** The `:91` bail-out currently returns on `origin === null`.
It becomes: on `origin === null`, act **only if `previouslyRead`**, and act with
the announcement half **only** —

- `readFailed` → clear the region (`text: ''`), exactly as `:107` does. The chain
  arm's own `role="alert"` insertion is the announcement.
- otherwise → `The {noun} was read. Save is available.`
- **never** `setOrigin(null)` (it is already `null`), **never** `setRetryFailureKey`
  (that key belongs to the Retry `<p>`'s remount and bumping it here would move
  focus), **never** `onReadRecovered()` (FR-009).

**d. What must not change.** `chainArmActive` (`:177`), the `retryFailureKey`
remount, the refocus effect (`:139-145`), `activate()`'s early return at `:151`,
and both arms' JSX. US3 adds a path; it does not alter one. The spec-156
invariants those lines encode (blocker 1 and blocker 2 in its phase-6 review) are
load-bearing and their existing tests must stay green unmodified.

## 4. Boundary and convention rules that apply

- **`ChainRecoveryNotice` stays presentational.** It receives booleans and
  callbacks; it does not import an RTK Query hook, does not know about
  `currentData`, and does not learn the word "overlay" or "layout" (`noun` is a
  prop, spec 156 FR-012). `previouslyRead` respects that: a boolean, computed by
  the caller.
- **No new shared abstraction.** Two dialogs sharing a five-term boolean is not a
  hook. Extracting `useSaveGate` would be the speculative generality ADR-0036
  rules out — the two predicates already differ (`knownCameras`).
- **No `Button.tsx` change** (FR-006). Adding `aria-disabled:` classes to the
  primitive's base would silently change every button in both apps.
- **ADR-0084 metrics** — both dialog files are near the 300-LOC cap. The net line
  change should be close to zero: the inline predicate moves into a local, and the
  layout comment block shrinks. If either file crosses 300, the answer is a shorter
  comment, not a suppression.
- **No `data-testid` is added.** Every element these tests need is already
  addressable by role and accessible name, and the status region already has
  `data-testid={`${noun}-chain-recovery-status`}` (`:188`).

## 5. Test strategy

### 5.1 Where the new tests go

**New files, not appends**, for the new stories:

```
apps/management-web/src/features/overlays/OverlayEditorDialogSaveGate.test.tsx
apps/management-web/src/features/layouts/LayoutEditorDialogSaveGate.test.tsx
```

Two reasons. (1) The existing `*ChainRecovery.test.tsx` files are the ones whose
**existing** assertions this change rewrites; mixing a rewrite and a new red test
in one file makes the phase-4a evidence unreadable — the red output must be
attributable to the new behaviour alone. (2) It is already house convention (five
`OverlayEditorDialog*.test.tsx` files), and it keeps the two feature directories
disjoint for `[P]`.

### 5.2 The harness gap, and why it is called out here

The existing chain harness (`OverlayEditorDialogChainRecovery.test.tsx:31-114`)
can step the chain query but **cannot make a mutation pend**: the mock hard-codes
`isLoading: false` (`:74-79`) and `editDraftMock` resolves immediately. §1.1 of
the spec shows `isLoading` is the dominant cause of the focus loss, so a test that
cannot drive it cannot observe the defect at its first moment.

The new files therefore need the mutation state to be steppable the same way the
chain query already is — a second `useSyncExternalStore` source, plus a deferred
promise for the trigger. This is stated at plan level because discovering it
mid-phase-4 is how a red test quietly becomes a weaker test that only drives
`chainFetching`.

### 5.3 The assertion rules (restating spec §7 where the engineer will look)

- Focus: `expect(document.activeElement).toBe(saveButton)`, never
  `.not.toBe(document.body)`. Capture the button reference **before** the
  transition.
- Gate closed: the `aria-disabled` attribute **and** a mutation-call-count
  assertion after an attempted submit. Never the attribute alone.
- Gate open: the attribute **and** a click that actually calls the mutation with
  the expected version. Without this a gate stuck closed passes.

### 5.4 A green first run is a phase-4 failure

All three stories are RED (spec §7). If a new test passes on unmodified
`develop`, the harness is wrong, not the code — the most likely cause is a mock
that never renders the in-flight state, which is the exact gap spec 156's plan §6
warned about and spec 158 inherited.

### 5.5 Counterfactual for US2

`chainFailed` is a single new term and this repo has twice been wrong about
whether a guard guards anything (#2371's `createState.reset()`;
`LayoutEditorDialogChainRetention.test.tsx:33-38`). After US2 is green, delete
the term locally and confirm the new test fails; restore it. T008 in `tasks.md`.

### 5.6 e2e

No new e2e spec. One defensive change: `e2e/overlays.spec.ts:145` clicks Save in
edit mode with no wait for availability, and whether Playwright 1.62's
actionability treats `aria-disabled="true"` as "not enabled" is unverified (spec
§11 A3). Making the wait explicit costs one line and removes the dependency on
the answer.

## 6. Messaging and events

**None.** No domain event, no integration event, no `Shared.Contracts` message,
no RabbitMQ queue, no outbox. The only wire traffic involved — the PATCH and the
GET — is unchanged in shape, headers and endpoint. The change is entirely about
*when the client is willing to send the PATCH it already sends*.

## 7. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | `aria-disabled` is cosmetic and a blocked click submits anyway — **strictly worse than `develop`** | FR-003 guard on the form's submit event, before `handleSubmit`; every gate-closed assertion paired with a call-count assertion (§5.3); the Enter-key scenario (spec §5.3) is its own test |
| R2 | Rewriting 17 existing assertions reads as gate-weakening in phase 6 | spec §5.4 enumerates every one by file and line; each rewrite is strictly stronger; the "same claim, new mechanism" rule is written down |
| R3 | US3 announces on a dialog's **first** read, making every open noisy | FR-010's `previouslyRead`; the negative test (spec §5.6) is mandatory, not optional, and phase 5 step 10 observes it |
| R4 | US3 disturbs spec 156's `origin` / `retryFailureKey` machinery and breaks the Retry/Reload focus behaviour | §3.3d names exactly what must not change; the existing `*ChainRecovery.test.tsx` focus assertions (`:206`, `:233`, `:256`, `:377`, `:388`, `:422`, `:435`) must stay green **unmodified** — they are not in spec §5.4's rewrite list |
| R5 | `e2e/overlays.spec.ts` silently clicks a closed gate and still passes | §5.6; T009 |
| R6 | Either dialog file crosses ADR-0084's 300-LOC cap | Net-zero line budget (§4); shrink the layout comment block rather than suppress |

## 8. What this plan explicitly does not do

- Change `Button.tsx`, or add an eslint rule, or write the cross-cutting
  `aria-disabled` decision (spec §6 — that is an ADR, and the lane may not write
  one).
- Extract a shared save-gate hook.
- Touch `invalidatesTags`, the endpoints, or anything in `src/`.
- Move focus anywhere (FR-004).
- Act on ADR-0149 (Proposed).
