# Contract: Renaming a camera from the management app

**Feature**: `035-rename-camera-ui` · 2026-08-25

A UI contract. The HTTP contract exists and is unchanged — spec 033 defined it.

---

## The endpoint, restated to be checked against

```
PATCH /cameras/{camera}      { "name": "line-4-inlet" }
```

| | |
|---|---|
| Scope | `sse.cameras.write` |
| Required header | `If-Match` with the version from the read |
| Body | **exactly one** of `name` or `rtspUrl` — never both |

| Status | Title | Means |
|---|---|---|
| `204` | — | Renamed, or already had that name |
| `400` | `CAMERA_INVALID_REQUEST` | Unusable name, or both fields sent |
| `404` | `CAMERA_NOT_FOUND` | No such camera **in the caller's fabs** — including another fab's |
| `409` | `CAMERA_RETIRED` | Terminal |
| `409` | `CAMERA_NAME_TAKEN` | Another **active** camera in this fab holds it |
| `412` | `CAMERA_VERSION_STALE` | The quoted version is no longer current |
| `428` | `IF_MATCH_REQUIRED` | No `If-Match` |

**Exactly one field per request** is why this is its own dialog and not a second
field on the address form: each is applied under its own version, so a combined
form's second request would quote a version its own first request had just
advanced.

---

## The control

| Camera status | Rename control |
|---|---|
| `Registered` | Present |
| `Decommissioned` | **Absent** |

Absent, not disabled — matching the two controls beside it and asserted the same
way, `toHaveCount(0)` rather than "clicking it fails".

Header order: **Rename · Correct the address · Retire camera · Back to
cameras** — destructive last before the link.

---

## The dialog

- **Pre-filled** with the camera's current name. A correction is an edit.
- Sends the **version** the operator was shown.
- Sends the name **as typed**. `.trim()` only; **no case normalisation, ever** —
  see below.
- Keeps the operator's typing when a rename is refused.

### Why "as typed" is a contract term and not a detail

`Line-4-Inlet` and `line-4-inlet` **normalise identically** but are different
things to read on a wall of live video. Spec 033 found that trap in three
separate layers — the repository predicate, the aggregate's idempotency guard,
and EF's change tracker — and fixed each.

A client that lower-cases before sending would be the fourth, and it would fail
the same way all three did: **success reported, nothing changed.**

---

## Three refusals, three answers

The address correction had two. This has three, and each has a different remedy:

| Refusal | What the operator is told | Remedy |
|---|---|---|
| `CAMERA_NAME_TAKEN` | which name, which fab, **and to choose a different one** | pick another name |
| `CAMERA_VERSION_STALE` | someone changed this while you were working | reload, then reapply |
| `CAMERA_RETIRED` | the camera is retired and cannot be changed | none — terminal |

### How each is produced

- **Taken** — the **server's own detail**, plus an action clause the dialog adds.
  The server says *"Another camera in fab 'munich' is already called
  'line-4-inlet'. Names are unique per fab, ignoring case."* — which names the
  actual conflict and is better than any generic sentence — but never says what
  to do. Recognised by **code, at the call site**, following
  `OverlayEditorDialog`. **No new shared predicate**: one call site does not earn
  one.
- **Stale** — the existing shared lost-update wording. Unchanged.
- **Retired** — the existing shared terminal wording. Unchanged.

### What must never happen

A taken name **must not** inherit the lost-update wording. Both are `409`, and
*"someone else changed this, reload to see their version"* is wrong in both
halves: nobody changed this camera, and reloading will not release the name.

---

## After a successful rename

The page shows the new name; the listing shows it too. **Nothing announces it.**

The reason is *not* spec 032's. That spec forbade announcing a retirement
because retiring is idempotent — the app cannot know whether this operator
caused it. **A rename is version-checked, so a success genuinely is this
operator's change.** There is simply nothing for a message to add that the
changed name on the page does not already say.

Recorded because the two rules look alike and are not, and a rule applied
without its reason is right by accident.

---

## Not in this contract

- **No rename for rules or variables.** ADR-0120 — their names are their
  addresses.
- **No fab change.** Forbidden, not deferred.
- **No bulk rename**, and no renaming from the listing: the detail page is where
  the operator has already established which camera they mean.
- **No backend change.**
