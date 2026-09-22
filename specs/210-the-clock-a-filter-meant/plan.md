# Plan — Spec 210, the clock a filter meant

**Spec:** `specs/210-the-clock-a-filter-meant/spec.md`
**Issue:** #2431
**Branch:** `2431-audit-date-clock-mismatch`
**Base:** `origin/develop` @ `3263084c`
**Phase-4a colour:** **RED** (behaviour-changing).
**Agents:** phase 4a `test-writer`, phase 4b `frontend-engineer`, phase 6 `frontend-reviewer`.

---

## Context and layers

**No bounded context is involved.** This is `apps/management-web`, a frontend feature module. No `src/` project is touched, so none of §III's boundary rules, no NetArchTest rule, no `Shared.Contracts` version, no migration, no Wolverine route and no EF model is in play. `PrimitiveBoundaryTests`, `HandlerDeconstructionTests` and the coverage gates (ADR-0065) all scope to `src/` and are unaffected.

The layering that does apply is the frontend one (ADR-0074, ADR-0075):

| Layer | File | Role here |
|---|---|---|
| Feature page | `apps/management-web/src/features/audit/AuditPage.tsx` | Owns `FilterDraft` — the **wall-clock domain**. Converts to the wire domain on submit. **The only file changed.** |
| Shared API client | `apps/shared/src/api/audit.api.ts` | Owns `SearchAuditInput` — the **instant domain**. Unchanged, deliberately. |
| UI primitives | `apps/shared/src/ui/primitives/Input.tsx`, `.../composites/FormField.tsx` | Pass-through. Unchanged. |

**The boundary this fix establishes is the one already implied by the two type names.** `FilterDraft.since: string` is a wall clock; `SearchAuditInput.since?: string` is an instant. They are the same TypeScript type, which is why the mismatch compiled and shipped. `toQuery` is the function that crosses between them and is therefore where the conversion belongs — it is already the only crossing point, and it already contains the per-field logic (`if (draft.x !== '')`) that the conversion slots into.

## Entities, value objects and invariants

No domain model, no value object — this is TypeScript in a React app, and constitution §II (the primitive ban, ADR-0139/0140) scopes to .NET domain models. The invariants are stated here instead so the reviewer has something to check against:

| Invariant | Held by |
|---|---|
| Every `since`/`until` that leaves the page is an ISO 8601 instant with an explicit `Z`, or is absent. | The conversion in `toQuery`. |
| An absent filter is absent, not empty and not `Invalid Date`. | The existing `!== ''` guard, plus the `NaN` fallback returning `undefined`, plus `definedParams` dropping `undefined`. |
| The zone a filter is interpreted in equals the zone the *When* column renders in. | Both are the browser's, via `new Date(...)` / `toLocaleString()`. Nothing names a zone explicitly, which is what makes them agree by construction rather than by coincidence. |
| No other filter field is transformed. | The conversion is applied at two named call sites, not mapped over the draft. |

## Messaging

**None.** No domain event, no integration event, no queue, no outbox. A `GET` through the YARP gateway (ADR-0106) is the only I/O, and its shape is unchanged apart from the value of two query params.

## The change, precisely

One new module-local function in `AuditPage.tsx`, and two lines of `toQuery` re-pointed at it.

```ts
/**
 * A `datetime-local` input reports a bare wall clock (`2026-09-17T08:00`) with
 * no UTC offset. Sent verbatim it is resolved in the *server's* zone — UTC in a
 * container — while the When column renders `occurredAt` in the *browser's*,
 * so the filter and the table disagreed by the operator's offset (#2431).
 * `new Date(...)` reads the bare form as local time (ECMA-262 §21.4.3.2), so
 * `toISOString()` is the instant the operator meant. An unparseable value
 * drops the filter rather than throwing from a render path.
 */
function toInstant(wallClock: string): string | undefined {
  const parsed = new Date(wallClock);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}
```

```diff
-  if (draft.since !== '') query.since = draft.since;
-  if (draft.until !== '') query.until = draft.until;
+  if (draft.since !== '') query.since = toInstant(draft.since);
+  if (draft.until !== '') query.until = toInstant(draft.until);
```

US2 adds, in the JSX only:

```diff
-        <FormField label="Since" htmlFor="audit-since">
+        <FormField label="Since (your local time)" htmlFor="audit-since">
-        <FormField label="Until" htmlFor="audit-until">
+        <FormField label="Until (your local time)" htmlFor="audit-until">
```

**Notes the implementer needs and would otherwise have to rediscover:**

- `query.since = undefined` type-checks: `tsconfig.base.json` sets `exactOptionalPropertyTypes: false`. Verified in the file, because with it `true` this line would be an error and the shape would have to change.
- `definedParams` (`audit.api.ts:55-63`) already skips `undefined`, so an unparseable value produces a request with no `since` key — no extra branch needed.
- The comment explains *why* (CLAUDE.md: comments say why). The issue number belongs in the comment here only because the defect is invisible from the code — a future reader deleting `toInstant` as "redundant" is exactly the regression it guards.
- Do **not** hoist `toInstant` into `apps/shared`. One consumer; see spec §*Where the conversion belongs*.

## Testing strategy

### The trap this test must not fall into

**CI's frontend job runs on `ubuntu-latest` (`.github/workflows/ci.yml:136`), which is UTC — the one zone in which this bug does not exist.** A test written the obvious way, "type 08:00, expect `2026-09-17T06:00:00.000Z`", is red on a German developer's machine and **green in CI against the unfixed code**. It would be a guard that cannot fail where it matters, and this repo has shipped that shape before in the opposite direction (`source-scanning tests need slash normalising` — green on Windows, red on Linux CI).

Two mechanisms, and **both** are required:

1. **A zone-independent assertion.** Assert the issued value is exactly `new Date(typed).toISOString()`. Under the fix this is true in every zone. Under the bug the issued value is the raw `'2026-09-17T08:00'`, which equals an ISO instant in **no** zone — it lacks the `Z` and the milliseconds. Measured: in UTC the expected value is `'2026-09-17T08:00:00.000Z'` ≠ `'2026-09-17T08:00'`. **So this assertion is red in CI too.**

   This is not an assertion checking its own input: the subject is the string the page put on the request, and under the bug that subject is untransformed. The expected value and the subject diverge precisely when the defect is present.

2. **Pinned-zone assertions for the shift itself**, so the test proves the *value* is right and not merely well-formed. Set `process.env.TZ` in `beforeEach` and restore it in `afterEach`. **Verified on Node 22** (the version CI installs, `ci.yml:147`): mutating `process.env.TZ` mid-process does change subsequent `Date` behaviour — three different zones produced three different instants from one string in a single process. Do **not** put `TZ` in `vite.config.ts`'s `test.env`: that pins it for every test file in `management-web`, and a global zone change is a behaviour change to tests this spec is not allowed to touch.

   Cover **both signs** — `Europe/Berlin` (+2) and `America/New_York` (−4). One zone alone cannot distinguish a correct conversion from a constant shift.

### Phase 4a — RED (`test-writer`), all before any production edit

All in `apps/management-web/src/features/audit/AuditPage.test.tsx`, extending the existing file. The pattern already there is exactly right and must be reused, not reinvented (`:77-86`):

```ts
expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({ eventKind: 'CameraRegisteredV1' }));
```

`searchMock` is the `vi.mock`ed `useSearchAuditQuery` (`:8-16`), so it receives the `SearchAuditInput` the page built — the request argument, one hop before the wire. That is the right level: it is the actual object RTK Query turns into query params, and asserting on it needs no server and no fetch interception.

**Setting the input value.** `user.type()` on a `datetime-local` types character by character and jsdom sanitises the intermediate values, so it is unreliable here. Use `fireEvent.change(input, { target: { value: '2026-09-17T08:00' } })`. The test-writer must confirm which works and report what they observed rather than assuming — if `user.type` does work, prefer it for consistency with the neighbouring test.

Cases (mapping to spec §Acceptance scenarios):

- **Zone-independent**: applying a *Since* issues `since === new Date('2026-09-17T08:00').toISOString()`. **This is the case that must be observed red in CI's zone.**
- **Berlin**: `TZ=Europe/Berlin`, typed `2026-09-17T08:00` → `since === '2026-09-17T06:00:00.000Z'`.
- **New York**: `TZ=America/New_York`, typed `2026-09-17T08:00` → `since === '2026-09-17T12:00:00.000Z'`.
- **Both bounds**: *Since* and *Until* both convert; neither retains the bare form.
- **Clear**: after **Clear**, the issued input has no `since` and no `until` key, and nothing throws.
- **Unparseable**: a non-datetime value in the draft issues no `since` key and the page still renders.
- **Other fields untouched**: an event kind and an actor are issued verbatim.
- **US2**: `screen.getByLabelText(/Since \(your local time\)/i)` resolves. Red today because the label reads `Since`.

**Expected red output**, which the engineer will receive and the PR body will quote: an `objectContaining` mismatch showing `since: "2026-09-17T08:00"` received against an ISO instant expected. The test-writer reports the output **verbatim**; the engineer may not edit these tests to pass.

**Existing tests must stay green throughout.** None of `:63-111` asserts on a date field, and none targets the *Since*/*Until* labels — checked. If one goes red, the change moved behaviour it should not have.

### Phase 4b — GREEN (`frontend-engineer`)

Apply §*The change, precisely*. US1 and US2 in separate commits. Then:

```sh
pnpm --filter @smart-sentinel-eye/management-web test
pnpm --filter @smart-sentinel-eye/management-web typecheck
pnpm lint && pnpm format:check
```

All four must pass; `lint` runs with `--max-warnings 0`.

### Coverage, analyzers, gates

ADR-0065's 90/80/90 gates are .NET-only and do not move. No analyzer, no `TreatWarningsAsErrors`, no NetArchTest rule is in scope. **No gate is weakened, no test deleted, no suppression added** (ADR-0144).

### Security review trigger

**Not security-sensitive.** No auth, scope, fab guard, endpoint, claim or secret is touched; the change is client-side and reaches no new data. A widened `since` can only return rows the caller's existing `sse.audit.read` + fab claims already permit — `SearchAuditQueryHandler` applies `callerFabs` independently of the date range. Phase 6 is `frontend-reviewer` only; `/security-review` is not required and skipping it should be stated in the PR.

## Why this is the smallest possible change

- One function, four production lines for US1, two strings for US2.
- The shared API client is untouched, so no other endpoint's behaviour can move.
- No backend, no contract, no migration, no dependency added.
- The `!== ''` guards, `definedParams`, `toLocaleString()` and every existing test are left exactly as they are.
- The bug fix and the label are separate commits, so the label can be dropped without disturbing the fix — CLAUDE.md's "a bug fix changes the bug, nothing else".

## Review focus for phase 6

1. **Is the new test actually red in UTC?** The single highest-value check. Ask for the verbatim phase-4a output and confirm the mismatch is on the ISO-instant assertion, not only on a zone-pinned one.
2. `process.env.TZ` is restored in `afterEach`, so no later test file inherits a pinned zone.
3. The conversion is applied to `since` and `until` only — not mapped across `FilterDraft`.
4. The `!== ''` guards survived; `new Date('')` is never reached.
5. `apps/shared` is unmodified (`git diff --stat` should show three paths plus `specs/`).
6. The comment on `toInstant` explains *why*, not *what*.
7. US2's label wording does not break an existing `getByLabelText` — none targets these two fields, but re-check at review time.
