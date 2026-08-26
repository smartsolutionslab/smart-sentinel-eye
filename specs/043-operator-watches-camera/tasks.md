# Tasks: An operator can watch a camera

**Feature**: `043-operator-watches-camera` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1886 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**12 tasks across five phases.** The smallest feature in a while: mount a
component that already exists and hand it a real token.

**The checks are the substance, not the code.** `CameraViewer` works — real video
was watched through it on a four-tile kiosk wall this week. What has been missing
for four specs is a page that renders it, and the reason nobody noticed is that
**three unit tests passed on the component that would have**. A feature that
fixed the mount and left that style of check would fix the instance and keep the
mechanism.

**Nothing here proves a picture appears.** The page test stubs `CameraViewer`
because jsdom has no `RTCPeerConnection`; CI produces no video because
`camera-sim` and `scenario-simulator` sit inside `if (isRunMode && !isE2ETests)`.
The suite proves the viewer is **reached** and **credentialled**. This is the
third feature running to carry that row, and it is stated rather than implied.

---

## Do not

- **Do not touch `CameraViewer`, `useWhepSession` or `WhepClient`.** They work.
  This feature reaches them.
- **Do not add a stream-health indicator to the page.** `CameraViewer` reports
  its own state — Connecting…, Live, Reconnecting…, Stream is offline, Viewer
  error. A second indicator is a second source of one fact.
- **Do not add a viewer to the camera list** (FR-009). One place shows a camera.
- **Do not export a token getter from `apps/shared/src/api/gateway.ts`.**
  Considered and rejected in research R1: public surface on a contention file
  (ADR-0109) for a single caller.
- **Do not keep the panel's caption** — *"Live feed served via WebRTC (WHEP).
  Reconnects automatically on transient outages."* The component reports its
  state in situ, and the WHEP half is an implementation note an operator cannot
  act on.
- **Do not let `getToken` be a fresh function each render.** Spec 042 spent a PR
  on exactly this in `CellPage` (issues 1888/1889): a new identity rebuilt the
  effects underneath `CameraViewer` and silently killed a measurement.
- **Do not render the viewer for a retired camera.** Retirement stops the stream
  deliberately, so a viewer reporting "Stream is offline" would describe an
  intended outcome as a fault.
- **Do not create `data-model.md`.** Nothing persists.
- **Do not write `#1886` in any committed document.** A bare mention auto-closes
  the issue on merge.

---

## Phase 1: The page shows the picture

**Goal**: An operator opens a camera and the live picture is there.

- [ ] T001 [US1] In `apps/management-web/src/features/cameras/CameraDetailPage.tsx`, add `useAuth()` from `react-oidc-context` and a **stable** token getter: a `useRef` holding `auth.user?.access_token`, reassigned on each render, wrapped in `useCallback(() => Promise.resolve(ref.current ?? null), [])`. Mirror `apps/kiosk-web/src/features/cell/CellPage.tsx`, including a comment saying why the identity must not change — a fresh function each render tears down and rebuilds the peer connection inside `useWhepSession`, which is the bug spec 042 fixed.
- [ ] T002 [US1] In the same file, render `<CameraViewer cameraIdentifier={camera.cameraIdentifier} getToken={getToken} />` **beneath the header and above the `<dl>`**, inside a width-bounded container (`max-w-3xl` or similar). `CameraViewer` already carries `relative aspect-video w-full` — measured from its class list — so the page supplies a maximum width and **nothing else**. Do not add an aspect or height constraint; it would fight the component. Leave the header, the three dialogs and the `<dl>` exactly where they are.

**Checkpoint**: an active camera's page shows its picture. Nothing else moved.

---

## Phase 2: A retired camera says why there is nothing to watch

**Goal**: The absence is deliberate and explained, not discovered.

- [ ] T003 [US2] In `apps/management-web/src/features/cameras/CameraDetailPage.tsx`, gate the viewer on `!retired` — the page's existing `const retired = camera.status === RETIRED` (`RETIRED = 'Decommissioned'`). Same file as Phase 1, so **not parallel with it**. This follows the page's existing rule rather than inventing one: every other control is already hidden for a retired camera.
- [ ] T004 [US2] Extend the existing `role="status"` retired notice in the same file so it covers the stream as well as the record. Today it reads *"This camera is retired. Its record is kept, but it can no longer be changed."* — which says nothing about an absent picture, leaving a reader free to conclude the video is broken. The retire confirmation already promises the stream will stop; the page should say it has.

**Checkpoint**: US2 complete.

---

## Phase 3: Delete the panel

**Goal**: Nothing is left looking supported that nobody can reach.

- [ ] T005 [P] [US1] Delete `apps/management-web/src/features/cameras/CameraViewerPanel.tsx`. With the dialog framing gone (FR-002) its whole body is one `<CameraViewer>` and a caption; a component that wraps a single child on a page that already supplies its context is a layer that only has to be read. Its placeholder `getKeycloakAccessToken` goes with it — a getter reading `sessionStorage.getItem('keycloak:access_token')`, a key nothing in the product writes.
- [ ] T006 [P] [US1] Delete `apps/management-web/src/features/cameras/CameraViewerPanel.test.tsx`. Its three tests describe a panel that opens and closes, and half that behaviour stops existing (FR-010). **This will look like losing coverage in the diff** — three green tests disappear — so the PR has to say what replaced them.

**Checkpoint**: the unreachable component is gone.

---

## Phase 4: The checks

**Goal**: Neither failure can recur silently, and it is clear which check catches
which.

- [ ] T007 [US3] In `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`, stub `CameraViewer` with `vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', …)` returning a `data-testid` element that **captures the props it was given**. Follow `CameraViewerPanel.test.tsx`'s idiom and carry its reasoning across: jsdom has no `RTCPeerConnection`, so the composite is stubbed rather than simulated. **The ten existing tests must pass unchanged** (FR-008) — without this stub they break, which is expected rather than a regression.
- [ ] T008 [US3] In the same file, assert the viewer is **reached from the page**: render the page for an active camera and find the stub. Rendering the component directly does not count (FR-006) — three passing tests on an unmounted component is precisely what the broken state looked like, and it was indistinguishable from working.
- [ ] T009 [US3] In the same file, assert the captured `getToken` **resolves to the session's token**, and that its identity is **the same function across a re-render**. This is the assertion the current code fails: the placeholder renders perfectly and resolves to `null`, so a viewer wired to a credential nobody issues fails identically to no viewer at all (FR-007). The stability half guards the spec-042 bug rather than trusting the comment.
- [ ] T010 [US3] In the same file, assert a retired camera gets **no viewer** and that the notice **mentions the stream**. Both: an unexplained absence lets a reader conclude the video is broken, which is the ambiguity FR-004 removes.
- [ ] T011 [P] [US1] In `e2e/camera-detail.spec.ts`, add a check that opens a camera as the seeded operator (`signInAsOperator` from `e2e/support/sign-in.ts`) and finds a `<video>` element on the page. Genuinely parallel with T007–T010 — different file, different failure mode. State in the file what it does **not** prove: CI produces no video, so a `<video>` element is not a picture.

**Checkpoint**: US3 complete. The page test and the e2e fail for different reasons.

---

## Phase 5: The part CI cannot do

**Goal**: Somebody watches the page.

- [ ] T012 Follow [quickstart.md](./quickstart.md) against `dotnet run --project src/AppHost`: open a camera the simulator feeds and record **which of the four viewer states** you saw, with the picture if you got one; confirm rename, address correction and retirement still work; retire it and confirm no viewer **and** an explained absence; navigate away and confirm the stream is released; then cause both check failures from quickstart §6 and record which check did **not** fire. No realm change is involved, so the Keycloak volume can stay. Name any step not performed.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003 ─▶ T004        (all one file, sequential by necessity)
                          │
                          ├─▶ T005, T006      (the deletion, parallel with each other)
                          │
                          ├─▶ T007 ─▶ T008, T009, T010
                          │
                          └─▶ T011            (e2e, parallel with the page tests)
                                    │
                                    ▼
                                  T012
```

**T001 before T002**, or the viewer is mounted with no token to give it.

**T007 before T008–T010.** Without the stub the page test cannot render at all,
so every assertion after it is unreachable.

**The deletion comes after the page works**, so the page is never without a
viewer — even though nothing imports the panel, doing it in this order keeps
every intermediate commit shippable.

---

## Parallel opportunities

- **T005 and T006** — the component and its test, deleted together.
- **T011** with **T007–T010** — the e2e is a different file and a different
  failure mode.
- **T001–T004 are NOT parallel**: four edits to `CameraDetailPage.tsx`.

Small feature, small opportunity. Marking more would be pretending.

---

## Implementation strategy

**MVP is T002.** The moment the page renders the viewer, an operator can watch a
camera — which is the whole feature. Everything after it makes the fix honest.

**Do T001 through T004 as one commit.** Four edits to one file that only make
sense together: the token, the viewer, the retired gate and the retired words.
Splitting them leaves intermediate states where a retired camera shows a viewer
that can only fail.

**Expect the page test to go red before T007.** Ten tests render that page, and
mounting a `WhepClient` in jsdom finds no `RTCPeerConnection`. That is the
expected consequence of T002, not a regression to diagnose.

**Budget time for T012.** It is the only place a picture can be seen, and it is
the third feature running where that is true.

---

## Three things most likely to go wrong

1. **The green suite gets read as "video works".** It proves the viewer is
   reached and credentialled and nothing more — the page test stubs the composite
   and CI has no video. That is the exact substitution this feature exists to
   correct, one level up: a component that looked supported because tests were
   green. T012 is the only thing that sees a frame, and it is a person.

2. **The diff reads as lost coverage.** Three passing tests disappear with the
   panel. They tested opening and closing something no operator could open, and
   what replaces them is stronger — that the page reaches the viewer, and that
   the credential it hands over is real. The PR has to say so, or a reviewer is
   right to object.

3. **`getToken` is written stable and then quietly destabilised.** Someone
   inlines the arrow back into the JSX, everything still renders, and the peer
   connection starts tearing down on every render. Nothing visible fails. T009
   asserts the identity rather than trusting the comment — which is the lesson
   spec 042 paid for in a whole PR.

---

## What the automated suite does and does not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The page reaches the viewer | T008 (page test) + T011 (e2e) | — |
| The viewer gets the operator's real token | T009 | T011 — a `<video>` is on the page either way |
| `getToken` is stable across renders | T009 | anything visible |
| A retired camera gets no viewer, explained | T010 | — |
| The rest of the page still works | the ten existing tests, unchanged | — |
| **A picture appears** | **T012 — a person, and nothing else** | both checks |

The last row is the honest one. Break `CameraViewer` itself and everything above
stays green.
