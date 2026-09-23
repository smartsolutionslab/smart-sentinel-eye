import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';

// ADR-0150 §1: the deadline is a FAILURE BOUND, not a wait. Testing Library's
// 1000 ms default is not a bound this repository ever chose, and it is the
// whole of #2520/#2419: the resolve-preview error advisory takes 29 ms to
// appear on an idle machine and 319 ms under contention, so twenty polls ran
// out on a loaded runner seven times in a week. 10_000 is the value
// `useSessionExpiry.test.ts` and `CameraViewerCameraSwap.test.tsx` each
// reached independently; a third number would be tuning, which is the habit
// ADR-0150 was written against.
//
// Do NOT reach for `vi.useFakeTimers()` to make a `waitFor` deterministic
// instead. Under Vitest it HANGS: `@testing-library/dom`'s
// `jestFakeTimersAreEnabled()` requires a global `jest`, which Vitest does not
// define, so `waitFor` takes its real-timer branch and schedules its own poll
// and timeout on faked timers nobody advances. Measured: two standard
// `userEvent.setup({ advanceTimers })` configurations, both dying at
// `testTimeout` with no `waitFor` message at all (spec 216 §Claim 7).
//
// This deadline is necessary but not sufficient: 3 of 20 contended runs still
// outran it before the fix below. The cause, per CI's own `frontend` job log
// (run 35725455764, `ubuntu-latest` = 4 cores): `apps/kiosk-web` and
// `apps/management-web` ran their Vitest suites concurrently, each sizing its
// own fork pool from the runner's core count -- 8 processes demanded on 4
// cores, a measured 2.0x oversubscription, on every single CI run. The
// workspace root's `test` script now caps `--workspace-concurrency` at 1 to
// remove it (spec 216 §R2 materialised).
// Spec 228 US2 (item 3): CameraViewer's always-mounted status region mirrors
// the visible overlay's text verbatim (FR-005), so while a stream is not
// live the two are the *same* string in the DOM by design — a screen reader
// is told exactly what a sighted operator sees. That duplication is meant to
// be read from `getByTestId('camera-viewer-status')`, never from a plain
// `getByText`/`queryByText`, which cannot tell which of the two identical
// strings a caller meant and throws on the ambiguity. Mirrors
// `apps/shared/src/test/setup.ts`'s identical exclusion, needed here too
// because this workspace's `CameraViewerLifecycle.test.tsx` renders the real
// composite rather than a mock.
configure({ asyncUtilTimeout: 10_000, defaultIgnore: 'script, style, [data-testid="camera-viewer-status"]' });
