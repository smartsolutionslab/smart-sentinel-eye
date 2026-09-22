# Plan — Spec 211, the record a failed refresh keeps

**Spec:** `specs/211-the-record-a-failed-refresh-keeps/spec.md`
**Issue:** #2432
**Branch:** `2432-transient-refetch-camera-exists` (worktree `D:/Github/sse-2432`)
**Base:** `origin/develop` @ `1cd84443`
**Phase-4a colour:** **RED.** Behaviour-changing — the page renders something different for a state it can actually reach. The `test-writer` must observe the new tests **failing** and return the verbatim output; that output is the engineer's brief and is quoted in the PR body (ADR-0139, ADR-0144, constitution §Testing). A test that arrives green here is a phase-4 failure, not a shortcut. **Do not default to characterisation-green.**

---

## Context and layers

This is frontend-only. There is no bounded context, no aggregate, no domain event, no migration, and nothing crosses a context boundary — so the usual Domain/Application/Infrastructure/Api decomposition has nothing to say here and is not being skipped silently.

| Layer | File | Change |
|---|---|---|
| `management-web` feature (ADR-0074) | `apps/management-web/src/features/cameras/CameraDetailPage.tsx` | **The only production edit.** |
| `apps/shared` API client (ADR-0075) | `apps/shared/src/api/cameras.api.ts` | **None.** Tags, endpoints and cache config are already correct. |
| `apps/shared` UI | `primitives/`, `composites/` | **None.** No shared banner component is created (see spec §Blessed). |
| Backend | every `src/**` project | **None.** |

**Boundary rules that apply.** `management-web` may consume `@smart-sentinel-eye/shared/api/*` and `@smart-sentinel-eye/shared/ui/*`; it must not reach into `camerasApi`'s internals (no `camerasApi.endpoints.*.select`, no `api.util.*`). Everything this fix needs is already on the `useGetCameraQuery` result object.

## Entities, value objects, invariants

None — this is a React page, not a domain model. Constitution §II (no primitives on a domain model) does not reach here, and `PrimitiveBoundaryTests` does not scan TypeScript.

The nearest thing to an invariant is the **rendering contract** stated as a truth table below. That table is the spec of the change; the exact boolean expression is the engineer's to choose.

## Messaging

None. No domain event, no integration event, no `Shared.Contracts` change. The nearest analogue — the RTK Query tag invalidation that triggers the refetch (`cameras.api.ts:207/226/246`) — is read-only context for this fix and is not modified.

---

## The change, precisely

### Today

```tsx
const { data: camera, isLoading, error } = useGetCameraQuery({ cameraIdentifier });

if (isLoading) { return <Surface>Loading…</Surface>; }

// FR-008 comment (:48-56)
if (error !== undefined || camera === undefined) {
  return (<Surface><h1>No such camera</h1>…</Surface>);
}
```

### After — the rendering contract

The hook result gains two fields that already exist on it: **`currentData`** (the cache entry for the identifier in the URL, and only that one) and **`refetch`**. `data` keeps its role as what is rendered; `currentData` becomes the gate.

| `isLoading` | `camera` (`data`) | `currentData` | `error` | Render | Change |
|---|---|---|---|---|---|
| `true` | any | any | any | `Loading…` | unchanged |
| `false` | `undefined` | `undefined` | `undefined` | **No such camera** | unchanged |
| `false` | `undefined` | `undefined` | set | **No such camera** | unchanged (FR-008) |
| `false` | defined | `undefined` | set | **No such camera** | **new** — the record on hand belongs to a *previous* identifier |
| `false` | defined | defined | set | **record + alert + Retry** | **new — the fix** |
| `false` | defined | defined | `undefined` | record, no alert | unchanged |
| `false` | defined | `undefined` | `undefined` | record | unchanged (the pre-existing navigation flash; out of scope) |

One expression that satisfies the table exactly:

```tsx
if (camera === undefined || (error !== undefined && currentData === undefined)) {
  return (<Surface><h1>No such camera</h1>…</Surface>);
}
```

and, inside the rendered page, immediately after `<header>` and before the viewer:

```tsx
{error !== undefined && (
  <div
    role="alert"
    className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
  >
    Could not refresh this camera — what you see may be out of date.{' '}
    <button type="button" className="underline" onClick={() => void refetch()}>
      Retry
    </button>
  </div>
)}
```

The `div`'s classes, the `role="alert"`, the `underline` retry `<button>` and the `void refetch()` are **copied verbatim** from `CamerasPage.tsx:123-133`. Only the sentence differs, and it differs for a stated reason: the listing's banner replaces rows, so it says the load failed; this one sits *above data that is still shown*, so it must say the data may be stale (spec §A1).

### What must not happen

- **Do not gate on `data`.** The spec's §"What that does to the issue's literal condition" shows `data` can hold the *previous* camera's record; gating on it renders camera A under camera B's URL after B is refused. `currentData` is the whole correctness of this change.
- **Do not inspect `error.status`.** FR-008's guarantee — restated in the comment at `:48-56` — is that nothing rendered varies with the refusal's code. Keep that comment, and extend it to say what the second condition is for.
- **Do not add a dismiss control.** `CamerasPage` has none, and a fulfilled refetch clears `error` itself (`writeFulfilledCacheEntry` → `delete substate.error`).
- **Do not change the `isLoading` branch, the "No such camera" markup, or any of the three control gates.**
- **Do not extract a shared banner component.** Six inline copies is the smaller change; extraction is a separate refactor issue.
- **Do not touch `cameras.api.ts`, `apps/shared/`, `kiosk-web`, or `CellPage.tsx`.**

### Comment obligation

The existing FR-008 comment block (`:48-56`) explains one condition and will now sit above two. It must be extended — not replaced — to say why the second condition (`currentData === undefined`) is there: the page refuses to show a record that belongs to a different identifier than the one in the URL, which is what keeps FR-008 true across a navigation. House rule: comments say *why*; the "what" is the truth table above and lives in this plan, not in the file.

---

## Testing strategy

### The trap these tests must not fall into

`CameraDetailPage.test.tsx` mocks `useGetCameraQuery` wholesale (`:12-25`), so a test can return any hook shape it likes. That is also the trap: **a mock that returns `{ data: camera, error: {...} }` without `currentData` proves nothing about the fix** — it would pass against `data`-gated code and against `currentData`-gated code alike. Every new test must set `currentData` explicitly, and the pair US1-A / US1-C is what makes the distinction load-bearing:

- US1-A: `data: camera`, `currentData: camera`, `error` set → record + alert.
- US1-C: `data: camera`, `currentData: undefined`, `error` set → **No such camera**.

A `data`-gated implementation passes US1-A and **fails** US1-C. That is the assertion that cannot check its own input.

### Existing tests: none need editing

Checked at HEAD. `CameraDetailPage.test.tsx` has 13 tests. The default mock (`:89`) and every per-test override return either `{ data: camera, error: undefined }` or `{ data: undefined, error: { status: 404 } }`. Against the expression above:

- `data` defined + `error` undefined → gate false → renders. Unchanged.
- `data` undefined → gate true → No such camera. Unchanged.

Neither shape needs `currentData`, and neither needs `refetch` (the alert is the only caller, and it does not render in those states). **No existing assertion moves, and no existing mock return is edited** — including the two FR-008 tests at `:312` and `:338`, whose `innerHTML`-equality comparison stays byte-identical. If the engineer finds themselves editing an existing assertion, that is evidence the behaviour moved further than this spec allows: **block, do not adjust** (ADR-0144).

New tests supply the fields they need on their own `mockReturnValue`. Adding a field to a mock's *return shape* is not editing a test to pass; editing an *assertion* is.

### Phase 4a — RED (`test-writer`), before any production edit

Two files, disjoint, both red before anything is implemented:

**`apps/management-web/src/features/cameras/CameraDetailPage.test.tsx`** — unit, covering US1-A, US1-B, US1-C, US1-F, US1-G, and the auth wording of US1-E for the alert state. Expected red for US1-A/B/G ("No such camera" renders instead of the record and the alert); US1-C and US1-F are expected **green** today and are there as the regression fence — the `test-writer` must say which of the new tests were red and which were green, and why, rather than reporting a single colour for the file.

**`e2e/camera-detail.spec.ts`** — one Playwright test: register a camera, open it, install a `page.route` that fails **only** the `GET` of the detail path (and only after the first successful load), rename, assert the heading and the alert are both present, then `unroute`, press Retry, assert the alert is gone and the new name is shown.

The route handler must discriminate by **method**, exactly as `system-variables.spec.ts:110-130` records: `GET /cameras/{id}` and `PATCH /cameras/{id}` are the same URL, so a handler that did not check `route.request().method()` would fail the rename itself and the test would be red for the wrong reason — and would stay red after the fix. It must also let the **first** `GET` through, or the page never loads a record and the test exercises US1-D instead of US1-A.

### Phase 4b — GREEN (`frontend-engineer`)

One file: `CameraDetailPage.tsx`. Destructure `currentData` and `refetch` from the existing hook call, split the condition per the truth table, add the alert, extend the FR-008 comment. Nothing else.

### Gates

- `pnpm lint`, `pnpm typecheck`, `pnpm typecheck:e2e`, `pnpm test` (vitest) all clean. Note: `typecheck:e2e` can fail on a clean `develop` for an unrelated missing `@types/node` — stash and re-run before attributing it to this branch.
- No C# is touched, so the 90/80/90 coverage gates (ADR-0065) and the SonarAnalyzer metrics (ADR-0084) have nothing to say about this change.
- No new dependency, so no lockfile change.

### Security review trigger

**Yes — phase 6 runs `security-reviewer` as well as `frontend-reviewer`.** The change sits directly on an information-disclosure control (FR-008, spec 029 FR-006: a camera in another fab must be indistinguishable from one that never existed). The reviewer's single question is: *can any rendered byte now differ between "refused because another fab" and "refused because it does not exist"?* The answer must be no, and the `innerHTML`-equality test at `CameraDetailPage.test.tsx:312` is the mechanical evidence.

---

## Why this is the smallest possible change

One file, one condition split into two, one banner copied from a sibling, one comment extended. No new component, no new dependency, no shared-code change, no backend change, no API change. The five other pages with the same banner are left alone; the kiosk's superficially similar `||` is left alone; the pre-existing navigation flash is left alone and filed.

## Review focus for phase 6

1. **The gate reads `currentData`, not `data`.** Everything else is cosmetic next to this.
2. **No `error.status` is read anywhere in the file.**
3. The FR-008 `innerHTML`-equality test still passes **unmodified**, and no existing assertion in the file was edited.
4. The alert's markup matches `CamerasPage.tsx:123-133` — same `role`, same classes, same retry shape — and adds no dismiss state.
5. The e2e's route handler discriminates by method and lets the first `GET` through (otherwise it is red for the wrong reason).
6. The PR body quotes the **verbatim** red output from phase 4a (ADR-0139).
