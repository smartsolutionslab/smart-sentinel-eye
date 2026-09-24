# Tasks 233: A refusal is not a reconnect

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2355 (feature-level issue; no per-task issues, per the CLAUDE.md Phase 3 note)
**Phase 4a colour**: **RED** (behaviour-changing), with named characterisation tests observed green.
**Engineer**: `frontend-engineer`. The tests are written first by `test-writer`, who returns verbatim output.
**Gate before phase 4**: spec §5 **R1 needs written acceptance** (human, via the orchestrator). Otherwise hold the issue.

No foundational tasks: no AppHost, Kernel or Contracts work. Everything is in `apps/shared`.

## Phase 4a: tests first (`test-writer`)

| ID | P | Story | Task | Colour |
|---|---|---|---|---|
| T001 | [P] | US1 | Create `apps/shared/src/ui/composites/CameraViewerRefusal.test.tsx` with the harness copied from `CameraViewer.test.tsx`. Red cases: **401 → "Access refused"**, the status-region text, 1 POST and 1 peer connection after `advance(60_000)`, and the `whep-refused` line with `kind:'unauthorized'`; **403 "forbidden"** → the same with `kind:'forbidden'`; **network then 401** → refused, no third POST; **null token + 401** → refused, 1 POST; **refused → Degraded→Healthy** → exactly one more POST, refused again, no more; **refused then unmount** → no further POST. | RED |
| T002 | (T001 file) | US1 | In the same file, characterisation: **403 "stream unavailable"**, **500** and **fetch rejects** each read `Reconnecting…`, make a 2nd POST after `advance(1000)` and log no `whep-refused` line. Also **refused → camera swap to a 201 camera** → exactly one POST for the new camera, and the label is no longer "Access refused". | GREEN before and after (the swap case is green today via the ladder) |
| T003 | [P] | US2 | Append to `apps/shared/src/ui/composites/FrameCapture.test.tsx`: *"Abandons a capture whose WHEP offer is refused without waiting for the timeout"*. WHEP answers 401, the could-not-capture alert appears before `advance(10_000)`, and exactly one POST was made. | GREEN before and after (catches the FR-006 regression) |
| T004 | [P] | US1 | `apps/shared/src/ui/composites/CameraViewerMedia.test.tsx`: add `'Access refused'` to `NON_LIVE_LABELS`. | GREEN (a strengthening edit; the existing tests still pass) |
| T005 | | — | Run `pnpm --filter @smart-sentinel-eye/shared test` (the workspace's vitest target). Quote the verbatim output: every T001 case red with the messages from spec §6, and T002–T004 green. | — |

T001/T002 share one file, so they are one agent pass. T003 and T004 own disjoint files and can run in parallel with it.

## Phase 4b: implementation (`frontend-engineer`; the tests may not be edited)

| ID | P | Story | Task | Depends |
|---|---|---|---|---|
| T010 | | US1 | `useWhepSession.ts`: add a value import of `WhepError`, a module-local `isRefusal`, the `REFUSED_MESSAGE` constant, and the refusal branch in the `connect()` `.catch` (plan D2/D3). Update the hook docblock (`:108-110`) so it no longer says "retried indefinitely" without qualification. | T005 |
| T011 | [P] | US1 | `CameraViewer.tsx`: `labelFor('error')` → `'Access refused'`. Leave `statusInfoFor`'s `'Viewer error'` (failedRead) unchanged (FR-007). | T005 |
| T012 | [P] | US2 | `FrameGrabber.tsx`: add `status === 'error'` to the fail-fast arm and rewrite the `:90-98` comment (plan D6). | T005 |
| T013 | | — | Run the tests again, all green with T001–T004 unmodified. Then run `pnpm -r typecheck`, `pnpm -r lint`, and the full `apps/shared` suite, including `CameraViewer*.test.tsx`, `FrameCapture.test.tsx`, and `apps/kiosk-web` / `apps/management-web` tests that render `CameraViewer`. | T010–T012 |

T011 and T012 own disjoint files. T010 is the only file with logic in it.

## Phase 5: verify

| ID | Task |
|---|---|
| T020 | Run spec §4 steps 1–7 against the real stack. Record the stub calls per tile over 120 s, the observed `kind` (expected `'unauthorized'` even for a stub 403), both screenshots, and the recovery by reload. |

## Phase 6: review

| ID | Task |
|---|---|
| T030 | `frontend-reviewer` and `security-reviewer`: check that a refusal cannot re-enter the ladder by any path, that the raw server body is never rendered, that exactly one resilience line fires per refusal, and that R1 is stated in the PR body. |

## Dependencies

T001–T004 → T005 → (T010 ∥ T011 ∥ T012) → T013 → T020 → T030.
