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
configure({ asyncUtilTimeout: 10_000 });
