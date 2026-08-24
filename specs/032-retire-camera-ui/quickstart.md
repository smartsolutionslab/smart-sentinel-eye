# Quickstart: Retire a camera from the management app

**Feature**: `032-retire-camera-ui` · 2026-08-24

How to see this working, and how to prove the three things most likely to be
wrong. All three are about what the app **says** or **does not show** — none of
them is about whether the request succeeds.

```sh
dotnet run --project src/AppHost
```

Sign in to the management app as an operator.

---

## 1. The happy path

Register a camera, open it from the listing, and retire it.

```
Cameras → Register camera → name it, give it an RTSP URL → Register
        → click the camera's name
        → Retire camera → read the confirmation → confirm
```

> **Expect**: the page stays where it is and now shows the camera as retired.
> The *Retire camera* control is **gone**. The *Correct the address* control is
> also gone (it already was, for retired cameras).
>
> Go back to Cameras: **the camera is not in the listing.**
>
> Paste the camera's own address back into the browser: **it opens**, marked
> retired.

If retiring navigated you somewhere, that is FR-009/FR-011 reversed — the
decision was to stay.

---

## 2. Read the confirmation, word by word

**This is the check most likely to be skipped, because the feature works
without it.** Open the confirmation again on a fresh camera and read it before
confirming.

> Must contain, all four:
>
> 1. **The camera's name** — not "this camera".
> 2. That retirement is **permanent / cannot be undone**.
> 3. That the **live stream stops**.
> 4. That the **name becomes available again** in that fab.

Points 3 and 4 are the ones an operator cannot discover from the camera's own
page. Point 4 is the payoff spec 028 built and nothing has ever surfaced.

Then **cancel**, and confirm the camera is still active. A confirmation that
retires on dismiss is worse than no confirmation.

---

## 3. Nothing claims you did it

After a successful retirement, read the whole page.

> **Expect**: no sentence anywhere saying *"Camera retired"*, *"You retired
> this camera"*, or similar.

The endpoint answers `204` whether or not this operator caused the transition —
retiring an already-retired camera succeeds identically. So a past-tense claim
of authorship is a claim the app cannot support (**FR-012**). The page showing
the camera as retired **is** the feedback.

To see why it matters, retire the same camera twice: open its address in a
second tab before retiring in the first, then confirm in the second tab too.
Both succeed. If either tab announced *"Camera retired"*, one of them said
something false.

---

## 4. The property that regresses by kindness

```
/cameras/00000000-0000-4000-8000-000000000001    (never existed)
/cameras/<a camera in a fab you do not hold>
```

> **Expect**: **identical** rendered output. Same words, same absence of a
> retire control, no hint that one of them is real.

Compare the two renderings, not merely that each showed something. A camera
record carries its RTSP address, so a page that distinguishes *"not yours"* from
*"not found"* lets an operator enumerate another plant's cameras one URL at a
time (**FR-013**, inherited from spec 029 FR-006).

This feature can break it in a new way: the retire control is one more thing
that could appear for one case and not the other.

---

## Automated equivalents

| Check | Where |
|---|---|
| 1 | `e2e/camera-detail.spec.ts` — SC-005, driving the app end to end |
| 2 | `RetireCameraDialog.test.tsx` — one assertion per required sentence |
| 3 | `CameraDetailPage.test.tsx` — asserted as **absence** of the claim |
| 4 | `CameraDetailPage.test.tsx` + `e2e/camera-detail.spec.ts` |

**No `fetch` to the API appears in this feature's e2e.** Spec 030 removed a test
that reached around the app to arrange state, because repairing it would have
produced a test exercising the API while claiming to exercise the application.
Registering and retiring are both things the app can now do, so it does them.
