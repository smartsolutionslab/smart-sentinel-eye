# Quickstart: watching a kiosk show a wall

**Feature**: 041 | **Date**: 2026-08-25

This is the half no automated check can perform. CI produces no video —
`camera-sim` and `scenario-simulator` both sit inside
`if (isRunMode && !isE2ETests)` — so **the two blockers this feature fixes are
invisible to the test suite in both directions**. A green run says the kiosk
reaches a wall; it says nothing about whether the tiles show a picture.

Everything below is done by a person against `dotnet run`, and the PR states
which claims rest on it.

---

## 0. Before you start

The realm is imported at Keycloak start-up. **A realm edit needs the container
recreated**, not just restarted — an existing volume keeps the old realm and you
will spend twenty minutes debugging a client that is still there.

---

## 1. Boot the stack

```sh
dotnet run --project src/AppHost
```

Wait for `keycloak`, `mediamtx`, `camera-sim`, `scenario-simulator` and both web
apps to report healthy in the Aspire dashboard.

**Check the realm import first.** In the `keycloak` container log:

```text
Referenced client scope 'basic' doesn't exist. Ignoring
```

That warning is expected (research R2) and is *why* the `sub` mapper exists. What
must **not** appear is any warning naming `smart-sentinel-eye-kiosk` — the client
is gone, and nothing should still refer to it.

---

## 2. Sign in

Open `http://localhost:5174/`, press **Sign in**, and log in as `operator` /
`Operator1234`.

**This is where a wrong redirect URI fails**, before any API call — so if the
Keycloak page shows *"Invalid parameter: redirect_uri"*, stop here: the client
switch is wrong and nothing after this point means anything.

**Expected**: you land back on the kiosk showing **"Pick a layout"** with at
least one layout on it.

**Before this feature it said "Could not load layouts."** — that is the defect,
and seeing the picker populated is the first observation worth recording.

---

## 3. Read the token

DevTools -> Application -> Session storage -> `http://localhost:5174` ->
`oidc.user:http://localhost:8080/realms/smart-sentinel-eye:kiosk-web`.

Decode the `access_token` payload (paste into `jwt.io`, or in the console:

```js
JSON.parse(atob(JSON.parse(sessionStorage.getItem(
  Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'))
)).access_token.split('.')[1]))
```

).

**Record all four**:

| Claim | Expected |
|---|---|
| `azp` | `kiosk-web` |
| `groups` | `["/fabs/munich"]` — the fab, absent before this feature |
| `scope` | `openid` plus exactly the six `sse.*` kiosk scopes |
| `sub` | **present** — a GUID. This is blocker A; if it is missing, video will not work and step 5 will fail |

`sse.management` must **not** appear. Its absence is SC-002 and it is only
visible here — a kiosk that works proves nothing about it.

---

## 4. Open a wall

Tap a layout.

**Expected**: the grid renders and each populated cell shows a tile.

Tiles render whether or not video arrives, so **this step alone does not prove
the feature works**. It is what CI can see. Step 5 is what CI cannot.

---

## 5. The blockers: does a picture appear?

**This is the step that matters.** Watch a tile for ten seconds.

| Observation | Meaning |
|---|---|
| Live video | Both blockers are fixed. Record it. |
| Tile stays in its connecting/failed state | Something is still refusing WHEP — go to the table below |

If there is no picture, look at the `stream-distribution` log for the
`POST /streams/authorize` call:

| Response | Cause | Where |
|---|---|---|
| 401 | the forwarded token has no `sub` | the `oidc-sub-mapper` on `kiosk-web` (blocker A, research R3) |
| 403 | the gate is still asking for `sse.management` | `AuthorizeWhepCommandHandler.RequiredScope` (blocker B, research R4) |
| 403, path invalid | the camera has no stream — not this feature | seed one and retry |

**Also check management-web still shows video** at `http://localhost:5173/`, on a
camera's detail page. It signs in with a token holding `sse.management` and *not*
`sse.streams.read`, so it is the case the grandfather clause exists to keep
working — and the one a narrowed gate would break.

---

## 6. What this feature repaired that nobody had seen

`LayoutLifecycleHub` joins one SignalR group per fab in the caller's `groups`
claim. A kiosk holding no fab joined nothing, so **live overlay text and per-tile
highlights have never reached a browser kiosk** (research R6).

With a wall open, change a system variable referenced by the tile's overlay in
management-web. **Expected**: the tile's text updates within about a second,
without a reload.

Worth recording either way: it is the first time this path has been exercised.

---

## 7. Prove the check can fail (SC-004)

FR-007 exists because the old assertion accepted the error as a pass. So cause
the failure rather than reasoning about it.

1. In `apps/kiosk-web/src/app/auth.ts`, put `client_id` back to
   `smart-sentinel-eye-kiosk` and `scope` back to `openid sse.management`.
2. Re-import the realm with the legacy client restored (or point at any client
   without `sse-groups`).
3. `pnpm test:e2e --project=kiosk`

**Expected**: **red.** If it is green, the assertion still cannot tell a working
kiosk from a broken one and nothing has been fixed.

4. Revert both edits and re-run. Green.

Record the failing output in the verification note. A claim that a check *would*
fail is the same class of claim this feature exists to correct.

---

## What to write down

- The four token claims from step 3, verbatim.
- Whether a picture appeared in step 5 — and if not, the `authorize` status code.
- Whether management-web still shows video.
- Whether the live overlay text updated in step 6.
- The red run from step 7, with its output.

If any step was not performed, **say which**. Spec 040's PR is open in draft for
exactly this reason, and this feature is what unblocks it.
