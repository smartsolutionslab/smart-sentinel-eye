import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { StreamHealth, StreamState } from '@smart-sentinel-eye/shared/api/streams.api';
import { StreamHealthBadge } from './StreamHealthBadge.js';

function streamWith(overrides: Partial<StreamHealth> = {}): StreamHealth {
  return {
    cameraIdentifier: 'cam-1',
    state: 'Healthy',
    whepUrl: 'http://mediamtx/cam-1/whep',
    transcodeMode: 'Passthrough',
    lastSuccessAt: '2026-05-26T10:00:00Z',
    error: null,
    ...overrides,
  };
}

describe('StreamHealthBadge', () => {
  it.each([
    ['Healthy', 'bg-accent-active/20'],
    ['Degraded', 'bg-accent-warning/20'],
    ['Offline', 'bg-accent-fault/20'],
    ['Provisioning', 'bg-fg-muted/20'],
  ] as ReadonlyArray<readonly [StreamState, string]>)(
    'Renders the %s pill with the correct tone class',
    (state, toneClass) => {
      render(<StreamHealthBadge stream={streamWith({ state })} />);

      const pill = screen.getByText(state);
      expect(pill).toBeInTheDocument();
      expect(pill.className).toContain(toneClass);
    },
  );

  it("Renders 'Unknown' when no stream data is available", () => {
    render(<StreamHealthBadge stream={undefined} />);

    const pill = screen.getByText('Unknown');
    expect(pill).toBeInTheDocument();
    expect(pill.className).toContain('bg-fg-muted/10');
  });

  /**
   * Spec 266 (issue #2335) US3 — new behaviour, RED. Replaces the case above:
   * `StreamHealthBadge` becomes a focusable trigger opening a shared
   * `Popover` (plan.md §4.3) rather than a hover-only `Tooltip`, so the
   * detail is reachable from the keyboard (spec §1 finding 4).
   */
  it('Is a focusable button, not an inert span', () => {
    render(<StreamHealthBadge stream={streamWith({ state: 'Degraded' })} />);

    expect(screen.getByRole('button', { name: /degraded/i })).toBeInTheDocument();
  });

  it('Enter opens a popover naming the state, the last-frame time and the error', async () => {
    const user = userEvent.setup();
    render(
      <StreamHealthBadge
        stream={streamWith({ state: 'Degraded', error: 'source unreachable', lastSuccessAt: '2026-05-26T10:00:00Z' })}
      />,
    );

    screen.getByRole('button', { name: /degraded/i }).focus();
    await user.keyboard('{Enter}');

    const detail = await screen.findByRole('dialog');
    expect(detail).toHaveTextContent('State: Degraded');
    expect(detail).toHaveTextContent('Error: source unreachable');
  });

  it('Shows no Error: line for a Healthy stream, even when its last poll carried an error string', async () => {
    const user = userEvent.setup();
    render(<StreamHealthBadge stream={streamWith({ state: 'Healthy', error: 'stale error from a prior poll' })} />);

    await user.click(screen.getByRole('button', { name: /healthy/i }));

    const detail = await screen.findByRole('dialog');
    expect(detail).not.toHaveTextContent(/error:/i);
  });

  it('Unknown stays inert: no stream record renders no button', () => {
    render(<StreamHealthBadge stream={undefined} />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText('Unknown')).toBeInTheDocument();
  });
});
