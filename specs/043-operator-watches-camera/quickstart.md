# Quickstart: watching a camera from the console

**Feature**: 043 | **Date**: 2026-08-26

Everything automated here proves the viewer is **reached** and **credentialled**.
Whether a picture appears is a person looking at a page, because CI produces no
video — `camera-sim` and `scenario-simulator` both sit inside
`if (isRunMode && !isE2ETests)`.

That is the whole shape of this feature: a component that rendered correctly for
four specs and reached nobody, while three unit tests stayed green.

---

## 1. Boot the stack

```sh
dotnet run --project src/AppHost
```

Wait for `camera-sim`, `mediamtx` and `management-web` to report healthy. Unlike
specs 041 and 042, **no realm change is involved**, so the Keycloak volume can
stay as it is.

Confirm there is something to watch: MediaMTX's path list should show
`cam-<guid>` entries at `ready: true`.

---

## 2. Open a camera and look at it

Sign in at `http://localhost:5173` as `operator` / `Operator1234`, open Cameras,
and click one the simulator feeds.

**Expected**: the picture is on the page, under the name and above the record
fields. No extra click — opening the camera is the whole interaction (SC-001).

**Record what you see**, and be specific about which:

| What the viewer shows | Means |
|---|---|
| moving video | the feature works |
| **Connecting…**, and it stays | the handshake is not completing — check `stream-distribution` for `POST /streams/authorize` |
| **Stream is offline** | the camera has no stream; try one the simulator feeds |
| **Viewer error** | read the hint under the label |

A `401` on `/streams/authorize` means the token did not arrive — which is the
old placeholder's failure mode, and the thing FR-007 exists to prevent.

---

## 3. Check the rest of the page is untouched

Same page, same visit (SC-006, FR-008):

- name, fab, RTSP URL, registered date and status all still shown;
- **Rename**, **Correct the address** and **Retire camera** all still offered and
  still working — open one dialog and cancel it.

This is a page an operator uses to manage a camera; the picture is an addition to
it, not a replacement for it.

---

## 4. Retire it and look again

Retire the camera you were watching, from its own page.

**Expected**: the page keeps the record, offers no controls, and shows **no
viewer** — and says the stream has stopped, not merely that the record cannot be
changed.

**The distinction is the point.** An operator must not read the absent picture as
a broken camera. Retirement stops the stream deliberately; the retire
confirmation says so before you agree to it.

---

## 5. Leave, and confirm the stream is released

With a live camera open, navigate to another camera, then back to the list.

**Expected**: the first stream stops. In the `mediamtx` path list the reader
count for that path drops back.

A console that accumulates connections while an operator works down a list is a
different kind of broken, and it is invisible from the page itself (SC-005).

---

## 6. Prove the checks can fail

Not strictly Phase 5, but do it here while the stack is up:

1. Remove the viewer from `CameraDetailPage`. **The page test and the e2e both go
   red.**
2. Restore it, and hand it the old placeholder getter — the one reading
   `sessionStorage.getItem('keycloak:access_token')`. **The page test goes red;
   the e2e stays green**, because a `<video>` element is still on the page.

The second is worth doing deliberately. It is the exact shape of the bug being
fixed, and it shows why "the viewer is on the page" is not the same claim as "the
viewer can work".

---

## What to write down

- Which of the four viewer states you saw in step 2, and the picture if you got
  one.
- That the other page controls still work.
- That the retired page shows no viewer **and explains it**.
- That the stream was released on navigation.
- Both failures from step 6, including which check did **not** fire.

If a step was not performed, **say which**. A component looking supported while
nobody could reach it is what this feature is about.
