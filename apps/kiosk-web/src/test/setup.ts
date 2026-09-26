import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';

// ADR-0150 §1: the deadline is a FAILURE BOUND, not a wait. Testing Library's
// 1000 ms default is not a bound this repository ever chose. #2520/#2419
// measured why in `apps/management-web` (spec 216): its resolve-preview
// error advisory takes 29 ms to appear on an idle machine and 319 ms under
// contention, so twenty polls ran out on a loaded runner seven times in a
// week. Neither this workspace nor `apps/shared` had a reported flake of
// that shape at the time, so spec 216 deferred both (ADR-0036); #2536
// adopts the same fix here on the strength of that precedent rather than a
// fresh occurrence. 10_000 mirrors `apps/management-web/src/test/setup.ts`
// — `useSessionExpiry.test.ts`'s own `waitFor` and
// `CameraViewerCameraSwap.test.tsx`'s per-test Vitest timeout (in
// `apps/shared`) had each hand-tuned that same number at their own call
// sites; #2536 folds both into this workspace-level default instead. A
// third number would be tuning, which is the habit ADR-0150 was written
// against.
//
// Do NOT reach for `vi.useFakeTimers()` to make a `waitFor` deterministic
// instead: under Vitest it HANGS, because `@testing-library/dom`'s
// `jestFakeTimersAreEnabled()` requires a global `jest` that Vitest does not
// define, so `waitFor` takes its real-timer branch and schedules its own poll
// and timeout on faked timers nobody advances.
//
// Spec 228 US2 (item 3): CameraViewer's always-mounted status region mirrors
// the visible overlay's text verbatim (FR-005), so while a stream is not
// live the two are the *same* string in the DOM by design — a screen reader
// is told exactly what a sighted operator sees. That duplication is meant to
// be read from `getByTestId('camera-viewer-status')` / `getByRole('status')`
// only, never from a plain `getByText`/`queryByText`, which cannot tell which
// of the two identical strings a caller meant and throws on the ambiguity.
// Mirrors `apps/shared/src/test/setup.ts` and
// `apps/management-web/src/test/setup.ts`'s identical exclusion — dormant
// here today (no kiosk-web test currently mounts a real `CameraViewer`), but
// needed so the first one that does doesn't hit "multiple elements found".
configure({ asyncUtilTimeout: 10_000, defaultIgnore: 'script, style, [data-testid="camera-viewer-status"]' });
