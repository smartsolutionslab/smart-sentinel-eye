import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';
import '@smart-sentinel-eye/shared/test/radixJsdom';

// Mirrors apps/management-web/src/test/setup.ts (plan 316 §6: "mirroring the
// shell's"). ADR-0150 §1: the deadline is a FAILURE BOUND, not a wait.
// Testing Library's 1000 ms default is not a bound this repository ever
// chose (#2520/#2419); 10_000 matches the shell's own setup and
// apps/shared's workspace-level default, rather than a third number tuned
// independently.
//
// Do NOT reach for `vi.useFakeTimers()` to make a `waitFor` deterministic
// instead — see the shell's setup.ts for the measured failure mode
// (spec 216 §Claim 7/§R2, spec 290): a fresh store per test is the fix, not
// a longer deadline.
//
// This remote renders CameraViewer (ADR-0107's moved cameras feature), whose
// always-mounted status region mirrors the visible overlay's text verbatim
// (spec 228 US2 item 3) — the same reason the shell's setup.ts excludes
// `[data-testid="camera-viewer-status"]` from Testing Library's default
// ignore list, so a plain `getByText`/`queryByText` cannot match both
// identical strings ambiguously.
configure({ asyncUtilTimeout: 10_000, defaultIgnore: 'script, style, [data-testid="camera-viewer-status"]' });
