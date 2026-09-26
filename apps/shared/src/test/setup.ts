import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';

// ADR-0150 §1: the deadline is a FAILURE BOUND, not a wait. 10_000 mirrors
// `apps/management-web/src/test/setup.ts` and `apps/kiosk-web/src/test/setup.ts`
// (spec 216, #2520/#2419). DORMANT here today: this workspace has no RTL
// `waitFor`/`findBy*` call — its async waits are all `vi.waitFor`, a
// different, Vitest-native mechanism this key does NOT govern. Set now so the
// first composite test added here that does use `findBy*`/`waitFor` inherits
// the bound instead of its author re-deriving it from scratch, which is
// exactly the drift ADR-0150 §1 is about.
//
// Spec 228 US2 (item 3): CameraViewer's always-mounted status region mirrors
// the visible overlay's text verbatim (FR-005), so the two are the *same*
// string in the DOM while a stream is not live — by design, so a screen
// reader is told exactly what a sighted operator sees. That duplication is
// meant to be read from `getByTestId('camera-viewer-status')` /
// `getByRole('status')` only, never from a plain `getByText`/`queryByText`,
// which cannot otherwise tell which of the two identical strings a caller
// meant and throws on the ambiguity. Excluding the region from `ByText`
// queries' default `ignore` (mirroring the built-in `script, style`) keeps
// every other suite's `getByText('Reconnecting…')` resolving to the one a
// sighted operator would actually read.
configure({ asyncUtilTimeout: 10_000, defaultIgnore: 'script, style, [data-testid="camera-viewer-status"]' });
