# Spec 142 — Plan

**Phase:** 2 (Plan) · **Date:** 2026-09-13 · **Spec:** `./spec.md` · **Issue:** #2198
**ADRs:** ADR-0074 (the shared package and the two apps), ADR-0076 (the
resilience channel is transport-agnostic), ADR-0109 (disjoint files and `[P]`),
ADR-0117 + constitution §IV/§VII (an implemented leg is subject; a discharge is
earned), ADR-0128 (what `setPlayoutTarget` actuates), ADR-0139 + §Testing (new
behaviour starts red), ADR-0143 (retrying needs justifying), ADR-0144 (the lane
writes no ADR, weakens no gate).

---

## Bounded context and layers

**No backend bounded context is involved and no `src/` file is opened.** This is
entirely browser-side, entirely inside `apps/shared`, with two consequences
worth stating rather than assuming:

- **`NetArchTest` has nothing to say about it.** No cross-context project
  reference exists to break; `Shared.Contracts` is not read or written.
- **Constitution §II's primitive ban does not apply.** It scopes to domain models
  in `src/`. `PlayoutTargetOutcome` is a TypeScript union in a browser package,
  not a value object, and `PrimitiveBoundaryTests` does not scan `apps/`.

`src/AppHost/AppHost.cs` and `src/AppHost/Resources/mediamtx.yml` are **read as
evidence for the measurement section and not modified.** Stated because spec 142's
severity ranking depends on them and a later reader will want to know whether the
spec changed what it cited.

| Layer | File | What it owns after this change |
|---|---|---|
| Transport client | `apps/shared/src/streaming/WhepClient.ts` | the WHEP POST, the session URL, the DELETE — **and now** the release outcome and the playout outcome |
| Reporting channel | `apps/shared/src/observability/resilienceLog.ts` | **unchanged.** One new `transition` string; no new code, no widened `ResilienceSubsystem` union |
| Session hook | `apps/shared/src/ui/composites/useWhepSession.ts` | **two lines only** — the seam's return type and its absent-client default. The state machine is untouched (see below) |
| Tile composite | `apps/shared/src/ui/composites/CameraViewer.tsx` | the alignment effect's report condition (FR-010) |

---

## The two lines in `useWhepSession.ts`, and why they are not #2157

The issue says `useWhepSession.ts` state-machine changes are out of scope, and
they are. **But `useWhepSession.ts` cannot be left byte-identical**, and pretending
otherwise would be the kind of quiet assumption this whole issue series is about.

`useWhepSession` is the **only** path from `CameraViewer` to `WhepClient`. It
re-declares the seam at `:48` and supplies the absent-client default at `:332`:

```ts
:48    setPlayoutTarget: (milliseconds: number) => boolean;
:331   const setPlayoutTarget = useCallback(
:332     (milliseconds: number) => clientRef.current?.setPlayoutTarget(milliseconds) ?? false,
:333     [],
:334   );
```

Two edits, both mechanical:

1. `:48` — `=> boolean` becomes `=> PlayoutTargetOutcome`.
2. `:332` — `?? false` becomes `?? 'not-connected'`.

**Edit 2 is the fix, not an incidental.** The `?? false` is the *fourth* producer
of the collapsed boolean and the exact one spec 095 recorded as its residual: a
null `clientRef.current` answering `false` latches `playout-target-unsupported`
on a healthy engine. The tri-state in `WhepClient` alone would not close it.

**What #2157 owns, and what is not touched here:** `transitionTo`, `statusRef`,
`status`, the connection effect and its early returns, `scheduleRetry`, the grace
and media timers, the cleanup at `:293-301`, and the `set-state-in-effect`
suppression. **Zero of those are read or written by this change.** The
`useCallback` at `:331` is a pass-through memo with an empty dependency array,
deliberately identity-stable for the reason `:324-330` gives (issue #1889); its
identity, its dependencies and its stability are all unchanged.

**If review disagrees with this split, it is a block, not a nit** — the two edits
are a type and a default, or they are a state-machine change; they cannot be
half of each. The claim to check is: does the diff touch any line that writes
`status` or `statusRef`? It must not.

---

## Types and invariants

### `PlayoutTargetOutcome`

```ts
export type PlayoutTargetOutcome = 'applied' | 'not-connected' | 'unsupported' | 'refused';
```

Exported from `apps/shared/src/streaming/WhepClient.ts`, beside `WhepErrorKind`
(`:3`), which is the file's own precedent for a string-literal union naming a
cause. Not a discriminated object union: there is no payload to carry, and
`unknown extra field` is not a problem this has.

### `setPlayoutTarget`'s shape after the change

Monotonic escalation over the receiver loop, which is what keeps FR-009 true:

```ts
setPlayoutTarget(milliseconds: number): PlayoutTargetOutcome {
  const pc = this.pc;
  if (pc === null || typeof pc.getReceivers !== 'function') {
    return 'not-connected';
  }

  let outcome: PlayoutTargetOutcome = 'not-connected';
  for (const receiver of pc.getReceivers()) {
    if (receiver.track?.kind !== 'video') continue;
    if (outcome === 'not-connected') outcome = 'unsupported';
    if (!('jitterBufferTarget' in receiver)) continue;
    try {
      (receiver as unknown as { jitterBufferTarget: number | null }).jitterBufferTarget = milliseconds;
      outcome = 'applied';
    } catch {
      if (outcome !== 'applied') outcome = 'refused';
    }
  }
  return outcome;
}
```

**Invariants this must preserve, each one a thing the existing suite or spec 045
holds down:**

1. **`outcome === 'applied'` ⟺ today's `applied === true`.** Same condition, same
   receivers, same value.
2. **The assignment is still attempted on every qualifying video receiver.** The
   loop does not short-circuit on the first success. Written this way
   deliberately: returning early would change what reaches the receivers, which
   is the one thing FR-009 forbids.
3. **`'applied'` outranks `'refused'`.** Two video receivers, one accepting and
   one throwing, answer `'applied'` — exactly as today's OR-ed boolean answers
   `true`. The `if (outcome !== 'applied')` guard in the `catch` is what enforces
   it, and it is the line a reviewer should check first.
4. **The `catch` stays empty of any escape.** No rethrow, no report, no
   `console`. Spec 045 FR-013, spec 142 FR-008.
5. **Non-video receivers are never written to and never escalate the outcome.**
   An audio receiver carrying `jitterBufferTarget` leaves `'not-connected'`
   standing.
6. **No `await`, no allocation per receiver, no statistics call.** The method is
   still synchronous and still write-only (`:170-176` says why, and that comment
   stays).

### `releaseSession`'s shape after the change

```ts
void this.opts
  .getToken()
  .then((token) => { /* headers, as today */ return fetch(sessionUrl, { method: 'DELETE', headers, keepalive: true }); })
  .then((response) => {
    if (!response.ok) {
      logResilienceEvent('stream', 'session-release-failed', { status: response.status });
    }
  })
  .catch((cause: unknown) => {
    logResilienceEvent('stream', 'session-release-failed', { error: String(cause) });
  });
```

**Invariants:**

1. **`keepalive: true` stays.** Spec §"Correction"; FR-003 states its cost.
2. **Still `void`-ed and still fire-and-forget.** `releaseSession()` returns
   `void` synchronously; `close()` calls `teardownLocally()` immediately after,
   unchanged. Teardown never waits on the network (FR-005).
3. **One line per failed release, never two.** The `.catch` is downstream of the
   status `.then`, so a `logResilienceEvent` that itself threw would be caught —
   it cannot, `console.info` does not throw, and no assertion depends on it — but
   a non-2xx cannot produce both lines because the status branch does not throw.
   Recorded because "a `.catch` after a `.then` that logs" is a shape that
   double-reports if the `.then` is made to throw later.
4. **A 2xx is silent.** `response.ok` covers 200 and 204, which are the two
   answers `draft-ietf-wish-whep` allows for a session DELETE.
5. **The `sessionUrl === null` early return is untouched**, so
   `WhepClient.test.ts:320-334` passes unmodified.
6. **`getToken()` is still resolved at release time**, so
   `WhepClient.test.ts:296` passes unmodified. A `getToken()` that rejects now
   lands in the same `.catch` and is reported under `{ error }` — previously it
   was swallowed identically to everything else.

### Why two payload shapes under one transition

`{ status: 401 }` and `{ error: 'TypeError: Failed to fetch' }`. One transition,
because the operational fact is one fact: *this session was not released*. Two
shapes, because each carries only what it has. The alternative — a single shape
padded with `status: null` or a magic `status: 0` — invents a value the failure
did not produce, which is the exact defect class #2109 filed nine times.

`resilienceLines(calls, transition)` (`WhepClient.test.ts:130`) filters on
`transition` alone, so both shapes are assertable through the existing helper
with no change to it.

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` message,
no RabbitMQ queue, no Wolverine handler. The `[resilience]` line is a browser
console line on a channel that already exists (spec 011 FR-017), and ADR-0076's
replaceable-transport rule is not engaged because nothing is pushed anywhere.

---

## Boundary rules

- **No cross-context project reference is created or broken** — no `src/` project
  is opened for writing.
- **`apps/shared` keeps its direction of dependency.** `WhepClient.ts` already
  imports `../observability/resilienceLog.js` (`:1`); the new calls use the
  import that is there. No new edge between modules, and in particular **no edge
  from `streaming/` to `ui/`**.
- **`apps/management-web` and `apps/kiosk-web` consume `apps/shared`, never the
  reverse.** The only file this change touches outside `apps/shared` is
  `apps/management-web/src/features/cameras/CameraViewerAlignment.test.tsx`, and
  only to retype a double whose declared return type stops compiling.

---

## File ownership and the `[P]` boundary

| File | Change | Owner task group |
|---|---|---|
| `apps/shared/src/streaming/WhepClient.test.ts` | harness widening + 10 new cases | 4a, serial |
| `apps/shared/src/streaming/WhepClient.ts` | both fixes + the new exported type | 4b, serial |
| `apps/shared/src/ui/composites/useWhepSession.ts` | 2 lines (`:48`, `:332`) | 4b, after the type exists |
| `apps/shared/src/ui/composites/CameraViewer.tsx` | the FR-010 report condition | 4b, after the type exists |
| `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx` | 3 doubles retyped + 1 new case | 4a/4b |
| `apps/management-web/src/features/cameras/CameraViewerAlignment.test.tsx` | 1 double retyped | 4b, `[P]` |

**Within this spec, almost nothing is `[P]`** — `WhepClient.ts` and
`WhepClient.test.ts` carry both items, so items 1 and 2 are serial with each
other despite being independent defects. The only genuine `[P]` pair is the
management-web double against the shared `ui/composites` edits: disjoint files,
disjoint apps.

**Across specs, this spec is `[P]` with spec 141** (#2197, PR #2322, parked).
141 writes only `apps/kiosk-web/src/features/cell/CellPage.{tsx,test.tsx}`; this
spec opens no file under `apps/kiosk-web`. `CellPage.tsx` imports `CameraViewer`
but never calls `setPlayoutTarget` (verified: `grep -rn setPlayoutTarget
apps/kiosk-web` is empty), so the signature change reaches it only as a type it
does not name. 141's own `tasks.md` already asserts this pairing.

---

## Risks

- **R1 — a double retyped is mistaken for an assertion edited.** Three
  `vi.fn(() => false)` / `vi.fn(() => true)` doubles must change spelling because
  the seam's type changed. **That is not an assertion edit and must not be
  treated as one**, but the distinction has to be policed: the `expect(...)`
  lines in those cases stay byte-identical, and a diff that changes one is a
  block. Called out because §Testing's "an assertion that has to be edited is
  evidence the behaviour moved" is exactly right and exactly the rule that a
  mechanical retype can be smuggled past.
  *Mitigation:* T007 states the three doubles and their new values explicitly, and
  the phase-5 note quotes the diff of the `expect` lines as empty.
- **R2 — mapping `false` doubles to the wrong new member.** The two
  `vi.fn(() => false)` doubles at `CameraViewerAlignment.test.tsx:344` and `:455`
  are documented in their own comments as *"no video receiver carries
  `jitterBufferTarget`"* — so they become `'unsupported'`, not `'not-connected'`.
  Mapping them to `'not-connected'` would make both cases silently vacuous: they
  would assert one `playout-target-unsupported` line against a condition that is
  no longer reported, and go red — which at least fails loudly. The worse
  direction is mapping the **new** `'not-connected'` case to `'unsupported'`,
  which passes and tests nothing.
  *Mitigation:* T004's counterfactual is stated as a one-token change.
- **R3 — the quiet cases pass against a component that never ran.** The failure
  mode 095 was reviewed for. Every quiet case asserts a positive **first**:
  `toHaveBeenCalledWith(120)` for the actuator, `deleteCalls).toHaveLength(1)`
  for the release.
  *Mitigation:* T003, T005 and T006 each name the counterfactual that must fail
  them, and phase 5 runs each counterfactual and quotes the failure count.
- **R4 — `Response` with status 401 and a `keepalive` init in jsdom.** The
  existing harness already constructs `new Response(body, { status: 401 })`
  (`WhepClient.test.ts:195-201`) and already asserts `init.keepalive === true`
  (`:293`), so both are known to work in this environment. No new capability is
  required.
  *Residual:* `fetchMock` is a single `vi.fn` serving both the POST and the
  DELETE. The new cases must sequence answers (`mockResolvedValueOnce` for the
  offer, then the DELETE's answer) rather than `mockResolvedValue`, or the POST
  will receive the 401 and the test will fail during `connect()` for an unrelated
  reason. T002 says so.
- **R5 — the 30 s figure is a derivation, not an observation.** Spec A1/A2.
  *Mitigation:* phase 5 observes it, or the verification note says it did not and
  the spec's wording ("derived at the pinned tag") stands unamended. **It must
  not be upgraded to "measured" by anyone who has not read it off a running
  MediaMTX** — §IV's leg table went wrong exactly this way.

---

## What "done" means, stated before any code

1. `pnpm --filter @smart-sentinel-eye/shared test` green, with the new
   `WhepClient.test.ts` cases counted;
2. `pnpm typecheck` rc=0 across all workspaces including `e2e/` — the signature
   change must not leave a `boolean` anywhere;
3. `pnpm lint --max-warnings 0` and `pnpm format:check` clean;
4. the four counterfactuals in `tasks.md` each fail **exactly** the case they
   name, re-run after the fix;
5. `apps/management-web` and `apps/kiosk-web` suites green **unmodified** apart
   from the one retyped double in R1.
