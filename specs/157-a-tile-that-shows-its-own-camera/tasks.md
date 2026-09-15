# Tasks — Spec 157, a tile that shows its own camera

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2370](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2370) · **Branch:** `fix/2370-a-tile-that-shows-its-own-camera`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `frontend-engineer` (TS/TSX only, `apps/shared` + eight test files
across the three apps). **Reviewer:** `frontend-reviewer`.
**Phase 4a colour: RED**, with T001 and T003 deliberately characterisation-green.
See §Phase 4a.
**Latency (§IV):** three legs cited, no cell moves, no measurement owed — spec
§*Latency-budget impact*. The §VII/#1940 condition raised there is a **gate
item** for the human, not a task.
**Delivery: one PR.** See §One PR, not a split.

`[P]` = disjoint files, safe to run concurrently (ADR-0109).

---

## Foundational — this blocks everything

### T001 — [Characterisation, GREEN] Teach every `useGetStreamQuery` mock to answer `currentData`

**Files** (eight; every mock of this hook in the repo):

| # | File |
|---|---|
| 1 | `apps/shared/src/ui/composites/CameraViewer.test.tsx` (`:7-9`, `:115-119`) |
| 2 | `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx` (`:186-190`) |
| 3 | `apps/shared/src/ui/composites/CameraViewerMedia.test.tsx` (`:220-224`) |
| 4 | `apps/shared/src/ui/composites/FrameCapture.test.tsx` (`:199-203`) |
| 5 | `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx` (`:166-177`) |
| 6 | `apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx` (`:20-30`) |
| 7 | `apps/management-web/src/features/cameras/CameraViewerAlignment.test.tsx` (`:52-62`) |
| 8 | `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` (`:550-561`) |

Each returns `{ data, isLoading, error }`. Add `currentData`, mirroring whatever
that mock's `data` is. Nothing else in these files may change.

**Do this first and capture the output twice** — every one of the eight suites
green *before* the edit and green *after* it. That pair is the characterisation
evidence; quote it in the PR.

**No assertion, fixture, helper or test name may be edited.** If one has to be,
behaviour moved: **stop and report**, do not adjust (§Testing, CLAUDE.md
Phase 4a).

Suites that already return `data: undefined` need nothing — they are already
`currentData`-equivalent: `apps/management-web/src/features/cameras/CameraViewer.test.tsx`,
`.../App.test.tsx:80`, `.../CamerasPage.test.tsx:34`,
`apps/shared/src/ui/composites/OverlayLabelCharacterisation.test.tsx:32`,
`.../OverlayLabelParity.test.tsx:46`,
`apps/management-web/src/features/overlays/OverlayEditorDialog{ChainRecovery,ChainRetention,ResolvePreview}.test.tsx`.

**Done when:** all eight suites pass unmodified-in-assertions before and after;
`pnpm -r --filter "./apps/**" test` is green; the two captured outputs are in
hand.

**Blocks:** T002, T003, T004, T005, T006, T007.

---

## US1 (P1) — an operator never sees a picture the wall cannot vouch for

### T002 — [RED] The discriminating test: a re-propped tile stops being camera A

**File (new):** `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`
**Agent:** `test-writer` (phase 4a). **Writes tests only.** Returns the verbatim
failing output, which becomes the engineer's brief and is quoted in the PR.

Harness, per plan §5 (do not invent a third shape — both halves already exist in
this repo):

- `// @vitest-environment jsdom` first line; `apps/shared`'s vitest default is
  `node`.
- `vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test')` **before** any
  import reaches `gateway.ts`; then dynamic-import `streamsApi` and
  `CameraViewer`. Copy the header form from
  `apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx:1-33`,
  including a comment saying why a mocked hook could not catch this.
- **Real `streamsApi`**, store built by the test:
  `configureStore({ reducer: { [streamsApi.reducerPath]: streamsApi.reducer },
  middleware: (g) => g().concat(streamsApi.middleware) })`, wrapped in
  `<Provider>`. The composite gains no knowledge of Redux (#2374, plan
  invariant 7).
- **Real `WhepClient`** with `FakePeerConnection` on `globalThis.RTCPeerConnection`
  and a duck-typed `fetch` stub — the pattern already at
  `apps/shared/src/ui/composites/CameraViewer.test.tsx:13-57`. Extend the stub to
  dispatch on URL: JSON for `…/streams/{id}`, SDP for the WHEP `POST`; and to
  hold or fail the `GET` for a named camera on command.

**The premise assertion, which is not decoration:** capture the `<video>` element
before the swap and assert it is the **same node** after. A test that remounts
proves nothing — the defect is that the tile is not remounted.

The scenario:

1. Mount on `cam-a`; `GET cam-a` answers a Healthy stream with
   `whepUrl: 'http://sfu/whep/cam-a'`; drive `FakePeerConnection` to `connected`
   and tick a frame → the tile is **Live**, one WHEP POST, to `cam-a`'s URL.
2. `rerender` with `cameraIdentifier="cam-b"` and **nothing else changed**; hold
   `GET cam-b` unresolved.
3. Assert, in the gap:
   - the `<video>` node is the same node (premise);
   - `FakePeerConnection.instances[0].closed === true`;
   - **no WHEP POST to `cam-a`'s URL beyond the first** — *red today*;
   - `videoEl.srcObject === null` — *red today*;
   - the tile is **not Live** and a state overlay is rendered — *red today*.
4. Resolve `GET cam-b` with `cam-b`'s `whepUrl`; assert **exactly one** WHEP POST
   to `cam-b`'s URL, and that Live returns only after a frame ticks.

**Done when:** the file exists, runs, and fails on the three assertions above
for the stated reasons — not on a harness error. **A green arrival is a phase-4
failure, not a shortcut.** Capture `pnpm --filter shared test -- CameraViewerCameraSwap`
verbatim.

**Depends on:** T001. **Blocks:** T004, T007.

### T003 — [P] [Characterisation, GREEN] Pin `FrameGrabber`'s covering behaviour

**File:** `apps/shared/src/ui/composites/FrameCapture.test.tsx` (existing; **not
edited** — captured).

`FrameCapture.test.tsx` is `FrameGrabber`'s covering suite. Run it and capture it
green *before* T006 touches `FrameGrabber.tsx:37`. It must pass **unmodified**
after. FR-002 is behaviour-preserving (a `FrameGrabber` is mounted per capture
and never re-propped, so `data` and `currentData` are indistinguishable there) —
so an assertion that has to be edited is evidence behaviour moved: **block, do
not adjust.**

If the suite is found to have no case covering a capture that goes Live, write
one first — a refactor with no covering test is a rewrite.

**Done when:** two captured green runs, before and after T006, with no diff to
the file.

**Depends on:** T001. **Disjoint from T002** (different file) — `[P]`.

### T004 — [RED→GREEN] `CameraViewer` reads for the camera it was asked about

**File:** `apps/shared/src/ui/composites/CameraViewer.tsx`

1. `:100` — `const { data: stream, … }` → `const { currentData: stream, … }`
   (FR-001). Keep `error: queryError` as it is.
2. FR-005 — a read that has failed with no stream for the current camera reads as
   an **error**, not as "Connecting…" or "Idle": give `labelFor` and the tone in
   `ViewerOverlay` the query error. Smallest expression that satisfies the
   Gherkin; do **not** add a status value (plan §2a).

**Do not touch** the two sampler effects, the three refs, their comment blocks,
or `OverlayLabel`. Nothing else in this file changes.

**Done when:** T002's `currentData` half behaves (`whepUrl` clears on the swap);
the failed-read scenario shows an error; `pnpm --filter shared lint typecheck
test` clean. T002 will still be partly red until T005 — that is expected and
must be stated, not worked around.

**Depends on:** T002 (its verbatim output is this task's brief). **Blocks:** T007.

### T005 — [RED→GREEN] A camera change ends the previous camera's session and its picture

**File:** `apps/shared/src/ui/composites/useWhepSession.ts`

**(a)** Add a `previousCameraRef`, mirroring `previousStreamStateRef` (`:123`).

**(b)** Add an effect keyed `[cameraIdentifier, transitionTo]`, **declared above
the session effect at `:148`** — ordering is load-bearing, but not because it
keeps this effect's setup clear of the session effect's cleanup: React runs
*all* cleanups before *all* setups regardless of declaration order, so that
race cannot occur either way. The real reason is that both effects' setups
call `transitionTo`, and on a warm-cache swap (the new camera's data already
in the RTK Query cache — a camera permuted between tiles, or shown anywhere in
the last `keepUnusedDataFor` window) both fire in the same commit; whichever
setup runs last wins. Declared first, this effect's `'connecting'` runs before
the session effect's `'offline'`, so `'offline'` — the correct state — is the
last writer. Declared second, `'connecting'` would win instead, and an offline
new camera would read "Connecting…" forever under its own name. Carry a
comment naming the warm-cache condition and pointing at the guarding test
(`CameraViewerCameraSwap.test.tsx`'s "Reads Stream is offline, not Connecting
forever, when camera B is already warm in the cache"). No-op on first mount
(that is what the ref is for). On a change (FR-003, FR-006):

- `videoRef.current.srcObject = null`;
- `transitionTo('connecting')`;
- `attemptRef.current = 0`.

`transitionTo` inside an effect *may* trip `react-hooks/set-state-in-effect` at
`--max-warnings 0` — check `pnpm lint` before reaching for the disable form at
`:159`; it did not fire for this effect in practice, likely because this
effect also does other work (`srcObject`, `attemptRef`) and is not a pure
state-derivation effect, so no disable was needed. **Do not** disable
`exhaustive-deps`.

**(c)** FR-004 — add `cameraIdentifier` to the session effect's dep array at
`:304`, and make the reference real with `void cameraIdentifier;` beside the
existing `void retryNonce;` at `:149`. **This documents intent; it is not a
lint requirement** — `exhaustive-deps` only fires on a *missing* dependency,
not an unused one, so omitting `cameraIdentifier` entirely would not fail
`--max-warnings 0`. The reason to add it anyway: the teardown currently rides
on `transitionTo`'s `[cameraIdentifier]`, and a future refactor holding the
camera behind a ref — which this file already does for `getToken` at
`:130-133` — would silently delete it, and only a human reading the comment,
or a merge conflict, would catch that. **Mirror the existing idiom; do not add
an eslint-disable.**

**Scope the `srcObject` clear to the camera-change path only.** Clearing on every
teardown inverts the `mediaBaseline` reasoning at `:227-233` that #2111 paid for.

**Do not touch:** `scheduleRetry` (`:184-195`), the `connect().catch` at
`:290-292`, the `'error'` union member at `:8`, `armMediaWatch`, `pollForMedia`,
`confirmMedia`, `onConnectionStateChange`, the Degraded→Healthy effect
(`:306-318`), `stats`, `setPlayoutTarget`, or any constant. Those are #2355's
and plan §3's.

**Done when:** T002 is fully green for the stated reasons; every suite from T001
still passes with **no assertion edited**; `pnpm --filter shared lint` clean at
`--max-warnings 0`.

**Depends on:** T002, T004.

### T006 — [P] [Characterisation] Close the same shape in `FrameGrabber`

**File:** `apps/shared/src/ui/composites/FrameGrabber.tsx`

`:37` — `const { data: stream }` → `const { currentData: stream }`. One word.
Nothing else in the file changes; the `'error'`-arm comment at `:84-92` stays.

**Done when:** T003's captured suite passes **unmodified**; `pnpm --filter shared
lint typecheck test` clean.

**Depends on:** T003. **Disjoint from T004/T005** — `[P]` alongside them.

### T007 — [RED] The two remaining Gherkin arms

**File:** `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` (the
T002 file, extended). **Agent:** `test-writer`, then `frontend-engineer`.

Add, using the same harness:

- **Conflict / failed read** — every `GET cam-b` fails: an explicit error, never
  Live, `srcObject === null`, and no further WHEP POST to `cam-a`'s URL even
  after the 5 s poll has re-errored (advance timers).
- **Authorisation** — the gateway answers 403 for `GET cam-b`: same outcome.
  (A WHEP `POST` 401/403 is #2355 and is **not** tested here.)
- **Offline** — `GET cam-b` answers `state: 'Offline'` with a reason: the tile
  reads "Stream is offline" with that reason and opens no session for either
  camera.
- **Regression** — a re-render with a new `getToken` closure and the **same**
  camera closes nothing, clears nothing and stays Live. This is the guard that
  FR-003 did not become "tear down on every render";
  `apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx:55`
  already covers the mocked form, so state the overlap and keep this one because
  it runs against the real hook.

**Done when:** each arm is observed red before its production behaviour exists
(the offline and regression arms may arrive green — they are existing behaviour
being pinned, and must be labelled as such, not presented as red).

**Depends on:** T002. **Blocks:** nothing.

---

## Verification (phase 5) — beyond tests green

### T008 — Observe the swap on a real wall

Run the spec's *Independent end-to-end test procedure* (spec §US1) against a
booted Aspire stack, both halves: the successful read and the failed read. Watch
cell (0,0) continuously across the revision; it may show a black tile reading
"Connecting…" and must never show camera A's picture while the overlay names
camera B.

**One machine, one Aspire stack** — stop any running host first, or
`FailedToStart` will read exactly like a code defect.

**Cite in the verification note:** the legs (spec §*Latency-budget impact*), and
that no §IV cell moved. No new latency figure is produced or claimed.

**Note the delivery mechanism honestly:** the revision reaches the mounted wall
only on a hub reconnect, because `CellPage` wires no `onPublished`. If forcing a
reconnect proves impractical, fall back to driving the swap through the running
kiosk's React tree and say in the note that the browser-level swap was observed
and the hub delivery was not — do not claim the end-to-end path if only half was
watched.

**Depends on:** T005, T006.

---

## Follow-ups to file (not implemented here)

### T009 — File the RTK-census issue that does not exist

`CellPage.tsx:435,469` and `CameraDetailPage.tsx:42` are the remaining
`data:`-with-a-variable-argument sites. **#2378 does not own them** — it is the
accessibility issue about focusable controls in conditionally rendered live
regions, sixteen sites, none in `apps/shared/src/ui/composites`. The census lives
in #2370's own body, so after this spec closes #2370 the remaining sites are
owned by nothing, and the adopted ESLint rule has no tracked precondition list.

File one issue covering both sites, referencing #2370, #2368/PR #2373 and #2341,
and have it recount the census (#2370 claims six hazards and names five sites;
the sixth is probably the already-fixed `LayoutEditorDialog.tsx:65`).

### T010 — Comment on #2355 with its two factual drifts

It says there is no `'error'` status at all — the union at `useWhepSession.ts:8`
has one, it is simply never produced. It cites `FrameGrabber.tsx:84` as a dead
`status === 'error'` branch, which no longer exists (the file now carries a
comment explaining the arm was dropped). Worth saying before someone implements
from its text. Also note the adjacency: if #2355 lands a terminal refusal state,
a camera change must clear it.

### T011 — File the stale-wall issue

`CellPage` wires no `onPublished`, and there is no `LayoutPublished` handler
anywhere under `apps/`. The kiosk's `useGetLayoutQuery` provides
`{ type: 'Layout', id }`, invalidated only by management-web mutations in a
different store. A published revision therefore reaches a mounted wall only via
`onReconnected → refetch()` (`CellPage.tsx:299-301`) — a hub reconnect. A wall
that keeps showing a retired layout until the network happens to blip is a
separate defect with a separate fix.

---

## Dependencies and parallelism

```
T001 (mocks, green)  ──┬── T002 (RED test) ──┬── T004 (CameraViewer) ── T005 (useWhepSession) ──┬── T008 (verify)
                       │                     └── T007 (remaining arms)                           │
                       └── T003 [P] (FrameGrabber characterisation) ── T006 [P] (FrameGrabber) ──┘

T009, T010, T011 — issue-filing, no code, independent of all the above.
```

**T001 is the only true blocker.** It touches eight files across all three apps
and every other task's suite runs through them, so it is not `[P]` with
anything.

**`[P]` pairs:** T003 with T002; T006 with T004/T005. Both are the
`FrameGrabber`/`FrameCapture` file pair against the
`CameraViewer`/`useWhepSession`/`CameraViewerCameraSwap` trio — disjoint files,
ADR-0109.

Nothing else parallelises: T004 and T005 are two halves of one behaviour in two
files that the same test exercises, and either alone produces a worse wall than
today (plan §6).

---

## Sites this spec touches, and sites it does not

**Touches** — six production/test files plus one new:

- `apps/shared/src/ui/composites/CameraViewer.tsx` (`:100`, `ViewerOverlay`)
- `apps/shared/src/ui/composites/useWhepSession.ts` (a new effect; `:149`, `:304`)
- `apps/shared/src/ui/composites/FrameGrabber.tsx` (`:37`)
- `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` (**new**)
- the eight mock files of T001 (mock objects only)

**Does not touch** — stated so the boundary is checkable:

- `apps/kiosk-web/src/features/cell/CellPage.tsx` — **nothing**, including
  `:435` and `:469`. See T009.
- `apps/management-web/src/features/cameras/CameraDetailPage.tsx:42`. See T009.
- `useWhepSession.ts:290-292`, `scheduleRetry`, the `'error'` union member — #2355.
- Any list page's `data:` destructure — retaining the previous list across a
  filter change is desirable there and is not a defect.
- The ESLint rule — ADR-class, lane-forbidden, and it lands red until T009's
  sites are fixed.
- **Anything under `src/**`** — no C#, no domain, no migration, no contract.

---

## Phase 4a

**Colour: RED.** T002 and T007 are the phase-4a tests; `test-writer` writes them,
runs them, and returns the **verbatim** failure, which is the engineer's brief
and goes in the PR body (ADR-0139). The engineer may not edit them to pass.

**Two tasks are deliberately the other colour**, because §Testing has two
obligations and this spec engages both:

- **T001** (mock widening) and **T003/T006** (`FrameGrabber`) are
  behaviour-preserving. Their covering tests are captured **green before** the
  change and must pass **unmodified** after. An assertion that has to be edited
  is evidence behaviour moved: **block, do not adjust.**

The ambiguity-resolves-to-red rule is not engaged. FR-002 is demonstrably a
no-op — a `FrameGrabber` is mounted per capture and its argument cannot change on
a mounted instance — and T001 edits only mock return objects.

**The discriminating assertion, in one sentence:** with the `<video>` node proved
identical across the swap, no second WHEP POST goes to camera A's URL, the
element carries no media stream, and the tile is not Live.

---

## One PR, not a split

One PR, for three reasons that each hold alone:

1. **T004 and T005 are two halves of one behaviour.** Either alone leaves the
   wall worse than today: `currentData` alone freezes camera A's last frame under
   a Live label (the explicitly rejected shape), and the session change alone
   still rebuilds against camera A's stale URL. A PR that shipped half would have
   to be described as a fix and would not be one.
2. **T001 is a precondition for observing any of it and proves nothing on its
   own.** A PR containing only widened mocks changes no behaviour and tests no
   claim.
3. **`FrameGrabber` is one word in the same file family, in scope by the brief's
   own reasoning** — splitting would leave a known instance of the shape live.

**Board gate (CLAUDE.md phase 3):** #2370 is already on Project #13 with status
*In Progress*. No per-task issues — that stopped after spec 028. Nothing to add.

```sh
gh project item-list 13 --owner smartsolutionslab --limit 2000   # verify by content.url
```
