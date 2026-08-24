# Quickstart: A name is mutable exactly when it is not an address

**Feature**: `033-rename-convention` · 2026-08-24

How to see this working, and how to prove the four things most likely to be
wrong. Three of the four are about a rename being refused for the *right*
reason — a refusal that arrives with the wrong explanation is worse than no
refusal, because the operator acts on it.

```sh
dotnet run --project src/AppHost
```

---

## 1. The rename keeps the camera

Register a camera, read it, rename it with the version you were given:

```sh
curl -si -X PATCH "http://localhost:<camera-catalog>/cameras/<guid>" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -H 'If-Match: "1"' -d '{"name":"line-4-inlet"}'
```

> Expect **204**, and a new version on the `ETag`.
>
> Read it back: **same identifier**, same `registeredAt`, new name.

That identifier is the entire point. The retire-and-re-register workaround
produces a *different* one, which is what splits a camera's history in two.

---

## 2. It collides with everyone except itself

**The check most likely to be got wrong, and the one that fails first.**

```sh
# a) rename to a name another ACTIVE camera in this fab holds
> Expect 409 CAMERA_NAME_TAKEN.

# b) same, differing only in case: "LINE-4-INLET"
> Expect 409 CAMERA_NAME_TAKEN. Uniqueness ignores case (#1434).

# c) rename to a name held by a camera in ANOTHER fab
> Expect 204. Uniqueness is per fab.

# d) rename to a name held only by a RETIRED camera
> Expect 204. Retirement releases the name (spec 028 FR-006).

# e) rename the camera to the name it ALREADY has
> Expect 204, and NO new audit entry.

# f) rename "Line-4-Inlet" to "line-4-inlet" — case only, same camera
> Expect 204. This is a real change to what is displayed.
```

**(e) and (f) are the ones that break.** The existence check asks *does any
active camera in this fab hold this name* — and the camera being renamed is one.
If it finds itself, both are refused as `CAMERA_NAME_TAKEN` against their own
name.

A short-circuit on "new name equals current name" fixes (e) and **still fails
(f)**, because `Line-4-Inlet` and `line-4-inlet` normalise to the same value
while being a genuine change. If (e) passes and (f) does not, that is the fix
that was reached for.

---

## 3. The two conflicts do not look alike

Provoke both against the same camera:

```sh
# stale version: quote a version you already spent
> Expect 412, "title": "CAMERA_VERSION_STALE"

# taken name: quote the CURRENT version, ask for a name in use
> Expect 409, "title": "CAMERA_NAME_TAKEN"
```

> The two must differ in **code**. Per ADR-0119 the code is what a caller keys
> on; `CAMERA_NAME_TAKEN` **must not end `_STALE`**.

Why it matters: re-reading and retrying resolves the first and never resolves
the second. A caller that cannot tell them apart retries forever against a name
that belongs to somebody else.

---

## 4. The freed name is usable immediately

```sh
# rename line-3-inlet -> line-4-inlet, then:
curl -si -X POST "http://localhost:<camera-catalog>/cameras" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"name":"line-3-inlet","rtspUrl":"rtsp://10.0.7.9/h264"}'
```

> Expect **201**. The old name is on no active row the moment the rename
> commits.

This falls out of the index's shape — which is exactly why it is tested. Spec
028's research read this same index, concluded a requirement needed no
production code, and was wrong about the layer above it.

---

## 5. The convention is enforced, not just written

Temporarily add a rename to a **name-addressed** context — a
`RenameRuleCommand` in `src/Automation`, say, whose endpoints bind `{name}`:

```sh
dotnet test tests/Architecture.Tests
```

> Expect **red**, naming the context, the route parameter, and the rule.
>
> Then remove it and expect green.

A check that only recognises today's five aggregates would pass forever for a
sixth. The test for the test is that it fails for a context that does not exist
yet.

---

## Automated equivalents

| Check | Where |
|---|---|
| 1 | `RenameCameraIntegrationTests` — over real HTTP |
| 2 (a–d) | `RenameCameraCommandHandlerTests` + integration |
| 2 (e, f) | **`RenameCameraCommandHandlerTests`** — the self-collision pair |
| 3 | `RenameCameraCommandHandlerTests` asserts both codes, and that neither ends `_STALE` except the version one |
| 4 | `RenameCameraIntegrationTests` — register under the freed name |
| 5 | `NameMutabilityConventionTests`, proved by deliberate breakage |
