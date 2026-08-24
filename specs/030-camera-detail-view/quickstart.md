# Quickstart: Open one camera, and fix it

**Feature**: `030-camera-detail-view` · 2026-08-24

How to see this working, and how to prove the three things most likely to be
wrong.

---

## Boot

```sh
dotnet run --project src/AppHost
```

The management app is an Aspire JS resource; read its URL from the dashboard's
resource list rather than assuming a port.

Sign in as `op-3@munich.test` / `Operator1234` for Munich, or
`op-dresden@dresden.test` for the cross-fab check. The seeded names follow
`op-N@fab` for some fabs and `op-<fab>@fab` for others — spec 028 lost a CI
round to guessing one that did not exist.

---

## 1. Open one camera (US1)

Register a camera, then open it from the list.

**Then copy the address bar, open a new tab, and paste it.** The same camera
must open directly. That is FR-002, and it is precisely what a `useState` panel
cannot do — the reason the shell got a router.

**Then press back.** You must land on the list, not outside the application.

## 2. Correct the address (US2)

Change the address and save. What is displayed must become what the server
stored rather than what you typed — the same value here, which is why the
interesting cases are below.

## 3. Prove the two refusals say the right thing

**The check this feature is most likely to get wrong.** Both cases produce *a*
message, so "an error appeared" proves nothing. Read the words.

**Stale version.** Open the same camera in two tabs. Correct the address in the
first. Then correct it in the second, which still holds the old version.

> Must say: *someone else changed this — reload to see their version.*
> Must **not** say: *try again.* Resubmitting replays your change over theirs,
> which is the lost update the whole version mechanism exists to prevent.

This is the case that fails by default: `isStaleConflict` is 409-only and this
refusal is a **412**, so without the shared-helper change it falls through to
the generic wording.

**Retired.** Retire a camera, then open it.

> Must say: *this camera is retired.*
> Must **not** say: *someone else changed this, reload* — nobody did, and
> reloading will not help. `CAMERA_RETIRED` is a 409, so it matches `isConflict`
> and inherits the wrong words unless it is distinguished.

Also: a retired camera must show **no edit control at all**. If you can open the
dialog and only discover the refusal on submit, FR-007 is not met.

## 4. Prove the app did not undo the API's discretion (FR-008)

As the **Dresden** operator, paste the location of a **Munich** camera:

```
/cameras/<munich-camera-identifier>
```

> Must say exactly what an identifier that never existed says.

Then try a random identifier and compare. Any difference — *"you do not have
access"*, a different heading, a different shape — reintroduces the enumeration
spec 029's FR-006 and SC-003 were built to prevent. It would do so at the last
hop, in the one layer that spec could not test.

## 5. Check the shell conversion did not lose anything

The router touches every surface, so:

- All six nav destinations still reachable.
- **A page crash still shows the crash panel with the nav intact** (spec 011
  FR-016). The `ErrorBoundary` was keyed on the view; under a router it must be
  keyed on the location. **Nothing in this feature's own tests would notice if
  this broke** — which is why it is on the list.
- Sign-in, sign-out and session expiry still behave: the auth gate wraps the
  router, not the other way round.

---

## Verification checklist

| | |
|---|---|
| A camera opens from the list | FR-001 |
| Its location pastes into a new tab and resolves | FR-002 |
| Back returns to the list | FR-002 |
| Opening one camera does not fetch the catalogue | FR-003 / SC-002 |
| The address can be corrected; what is shown is what is stored | FR-004 |
| A stale version says **reload**, never "try again" | FR-005 / FR-006 |
| A retired camera says **retired**, not "someone else changed this" | FR-006 / FR-007 |
| A retired camera offers no edit control | FR-007 |
| Another fab's camera reads exactly as one that does not exist | FR-008 |
| A bad address is caught before it is sent | FR-009 |
| No name field in the edit form | FR-010 |
| The list still works; six surfaces reachable; crash panel intact | FR-011 / spec 011 FR-016 |
