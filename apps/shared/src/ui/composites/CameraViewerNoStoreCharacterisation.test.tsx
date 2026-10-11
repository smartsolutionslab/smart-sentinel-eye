// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * Issue #2846 (ADR-0169). `CameraViewer` must render without a Redux
 * `<Provider>` in the tree — it no longer calls `useGetStreamQuery` itself,
 * taking the stream as a prop instead. No `vi.mock` of `streams.api` here,
 * deliberately: if this file ever had to mock the hook to pass, that would
 * mean the component still depended on it.
 */
describe('CameraViewer renders without a Redux Provider in the tree (#2846)', () => {
  afterEach(() => {
    cleanup();
  });

  it('Renders with no Provider in the tree', () => {
    expect(() => {
      render(<CameraViewer cameraIdentifier="cam-42" getToken={async () => null} />);
    }).not.toThrow();
  });
});
