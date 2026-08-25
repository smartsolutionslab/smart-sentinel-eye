# Quickstart: Rename a camera from the management app

**Feature**: `035-rename-camera-ui` · 2026-08-25

How to see this working, and how to prove the three things most likely to be
wrong. Two of the three are about what the operator *reads*.

```sh
dotnet run --project src/AppHost
```

Sign in to the management app as an operator.

---

## 1. The happy path

```
Cameras → Register camera → name it → Register
        → click its name → Rename → correct it → Save
```

> **Expect**: the heading shows the new name, without a full page reload.
> Go back to Cameras: **the row shows the new name too.**
>
> Reopen the camera's own address: same camera, same registration time.

The dialog must open **pre-filled** with the current name. A correction is an
edit, not a retype — a blank field makes the operator reconstruct what they are
fixing.

---

## 2. Read all three refusals

**This is the check most likely to be skipped, because the feature works
without it.**

Each of these is a *different problem with a different remedy*, and an operator
told the wrong one acts on it.

```
a) Rename camera A to camera B's name, same fab.
b) Open the rename dialog, rename the camera in another tab, then submit.
c) Open the rename dialog, retire the camera in another tab, then submit.
```

> **(a) taken** — must name the conflicting name and fab, **and say to choose a
> different one**. The server supplies the first half; the dialog adds the
> second.
>
> **(b) stale** — must say *reload*, and must **not** say to choose a different
> name. Nothing is wrong with the name.
>
> **(c) retired** — must say the camera is retired and cannot be changed.
> Neither of the other two remedies applies.

> **Expect**: three visibly different sentences. If (a) reads *"someone else
> changed this while you were working"*, the taken branch is inheriting the
> lost-update wording — wrong in both halves, and it sends the operator to
> reload something that will not change.

Then check the field still holds what they typed. A refusal must not cost an
operator their input.

---

## 3. A case-only correction must reach the server

```
Rename "Line-4-Inlet" to "line-4-inlet".
```

> **Expect**: 204, and the heading now reads `line-4-inlet`.

The two normalise identically, so every layer that compares normalised values
sees no change. Spec 033 found that trap in the repository predicate, the
aggregate's guard **and** EF's change tracker. A client that lower-cases before
sending would make it a fourth — and the symptom is a rename that reports
success and changes nothing.

If the heading still reads `Line-4-Inlet`, something normalised on the way out.

---

## 4. The controls tell the truth

```
Retire a camera, then look at its page.
```

> **Expect**: **no** rename control. Absent, not greyed out — a disabled control
> says the action is conceptually available, and for a terminal state that is
> untrue.
>
> The address and retire controls are already gone; rename joins them.

```
/cameras/00000000-0000-4000-8000-000000000001      (never existed)
/cameras/<a camera in a fab you do not hold>
```

> **Expect**: **identical** output, rename control absent in both. A page that
> distinguishes them lets an operator enumerate another plant's cameras one URL
> at a time.

---

## Automated equivalents

| Check | Where |
|---|---|
| 1 | `e2e/camera-detail.spec.ts` — SC-005, driving the app |
| 2 | `RenameCameraDialog.test.tsx` — one assertion per refusal, on rendered text |
| 3 | `RenameCameraDialog.test.tsx` + the e2e run |
| 4 | `CameraDetailPage.test.tsx` — absence, and the two renderings compared |
