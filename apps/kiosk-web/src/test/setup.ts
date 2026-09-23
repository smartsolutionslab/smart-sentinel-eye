import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';

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
configure({ defaultIgnore: 'script, style, [data-testid="camera-viewer-status"]' });
