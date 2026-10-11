// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { BackdropControls } from './BackdropControls.js';
import { FrameGrabber } from './FrameGrabber.js';

/**
 * Issue #2847 (ADR-0169). `BackdropControls` and `FrameGrabber` used to call
 * `useListAllCameraChoicesQuery`/`useGetStreamQuery` themselves, which throws
 * without a Redux `<Provider>` in the tree — exactly the dependency ADR-0169
 * bans for a composite in `apps/shared`. Both now take the query's result as
 * a prop instead, so rendering either with no `<Provider>` — and critically,
 * no mocked hook either, so a reintroduced call would throw exactly as it
 * did before — must succeed. Red before this issue's fix (confirmed by
 * running this file against the pre-fix component; both renders threw
 * "could not find react-redux context value"), green after.
 */
describe('BackdropControls and FrameGrabber render with no Redux Provider (issue #2847)', () => {
  afterEach(() => {
    cleanup();
  });

  it('BackdropControls renders with getToken and no Provider', () => {
    expect(() =>
      render(
        <BackdropControls
          backdrop="checkerboard"
          onBackdropChange={() => {}}
          hasCapturedFrame={false}
          getToken={async () => 'token'}
          cameras={{ data: { items: [], count: 0, complete: true }, isLoading: false, isError: false }}
          selectedCamera=""
          onCameraChange={() => {}}
          captureState="idle"
          onCapture={() => {}}
          onCancelCapture={() => {}}
        />,
      ),
    ).not.toThrow();
  });

  it('FrameGrabber renders with no Provider', () => {
    expect(() =>
      render(
        <FrameGrabber
          cameraIdentifier="cam-42"
          stream={undefined}
          getToken={async () => 'token'}
          onCaptured={() => {}}
          onFailed={() => {}}
        />,
      ),
    ).not.toThrow();
  });
});
