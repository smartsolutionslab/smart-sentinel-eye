# Tasks 303 — A new operator starts with an empty cache

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2524 (observation 2 only)
**Engineer:** `frontend-engineer` (single engineer).
**Phase 4a colour:** behaviour-changing → **red first** (T001). Plan §4 test 4 is
a green-before guard against over-resetting and must stay green.

**Parallelism:** none. This is a strict chain, T001 → T002 → T003. The slice
edits `apps/management-web/src/app/store.ts` and `App.tsx`. No other in-flight
slice owns them, so it may run concurrently with backend slices.

---

## US1 — A changed subject starts from an empty cache

### [T001] [US1] Red: tests first (test-writer)

**New:**
- `apps/management-web/src/app/subjectWatcher.test.ts` (plan §4 test 1)
- `apps/management-web/src/app/store.test.ts` (plan §4 test 2)
- `apps/management-web/src/app/subjectChangeResetsCache.test.tsx` (plan §4 tests 3–6; real `AuthProvider` + `UserManager`, no `react-oidc-context` mock; subject switch staged exactly as `staleBearerRetry.test.tsx:120-131`, with a `sub` parameter added to `userWith`)

**Edit (fixture only, no assertion changes):** `apps/management-web/src/App.test.tsx`
and `App.affordance.test.tsx`. Add
`events: { addUserLoaded: vi.fn(), removeUserLoaded: vi.fn() }` and
`profile: { sub: 'operator' }` on the stubbed `user`.

Run `pnpm --filter management-web test` and `tsc --noEmit` (a missing named
import binds to `undefined` under Vitest). Quote the verbatim output.

**Expected:** tests 1, 2, 3, 5 and 6 red for the reasons in plan §4. Test 4
green. `App*.test.tsx` green before and after the fixture edit.

**Done when:** red observed for the stated reasons and the output captured for
the PR body.

### [T002] [US1] Implement (frontend-engineer), depends on T001

1. `store.ts`: add `apiSlices` (8 slices), build `middleware` from it, and
   export `resetApiCaches(dispatch)`. Keep the explicit `reducer` object.
2. New `subjectWatcher.ts`, as specified in plan §1.
3. New `useResetApiCachesOnSubjectChange.ts`: `addUserLoaded` / `removeUserLoaded`
   in one effect, and the seed effect on `auth.user?.profile.sub`.
4. `App.tsx`: `AuthGate` calls the hook right after `useAuth()`.

Do not touch `apps/shared`, the six pages or `kiosk-web`. Do not branch on any
error status (FR-006). Do not edit T001's test files.

**Done when:** all T001 tests green with their files unchanged since T001, the
full management-web suite green, and lint, typecheck and format clean.

### [T003] [US1] Verify (phase 5)

Follow plan §5. If Keycloak cannot be made to return a different subject,
record that as observed (plan §5 assumption) and cite T001 test 5 as the
end-to-end evidence. Latency: N/A.
