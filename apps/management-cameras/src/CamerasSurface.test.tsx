import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import CamerasSurface from './CamerasSurface';

// Scaffold-only (T007): CamerasSurface is a placeholder until T009 moves the
// real features/cameras pages in behind it. This just confirms the remote's
// own test harness (jsdom, Testing Library, src/test/setup.ts) is wired up.
describe('CamerasSurface', () => {
  it('renders without throwing', () => {
    const { container } = render(<CamerasSurface />);
    expect(container).toBeEmptyDOMElement();
  });
});
