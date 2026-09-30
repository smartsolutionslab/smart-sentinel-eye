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
  /**
   * Spec 297 (issue #2635) T008, plan.md §5.4. RED — rewritten from the old
   * `/20` call-site-alpha classes (`bg-accent-active/20`, ...) to the shared
   * Badge's token-based tone classes: this is the exact behaviour this spec
   * changes (spec §6), the one existing test the architect names as allowed
   * to change. Quoted in the PR body.
   */
  it.each([
    ['Healthy', 'bg-accent-active-subtle'],
    ['Degraded', 'bg-accent-warning-subtle'],
    ['Offline', 'bg-accent-fault-subtle'],
    ['Provisioning', 'bg-bg-raised'],
  ] as ReadonlyArray<readonly [StreamState, string]>)(
    'Renders the %s pill with the correct tone class',
    (state, toneClass) => {
      render(<StreamHealthBadge stream={streamWith({ state })} />);

      const pill = screen.getByText(state);
      expect(pill).toBeInTheDocument();
      expect(pill.className).toContain(toneClass);
    },
  );

  /**
   * Spec 297 (issue #2635) T008. RED — rewritten from the old
   * `bg-fg-muted/10` call-site alpha to the shared Badge's neutral tone
   * (`bg-bg-raised`). The other existing test line the architect allows to
   * change (spec §6).
   */
  it("Renders 'Unknown' when no stream data is available", () => {
    render(<StreamHealthBadge stream={undefined} />);

    const pill = screen.getByText('Unknown');
    expect(pill).toBeInTheDocument();
    expect(pill.className).toContain('bg-bg-raised');
  });

  /**
   * Spec 297 (issue #2635) T008, FR-004. RED: today's PILL constant
   * (`StreamHealthBadge.tsx:17`) includes `rounded border`, and every TONES
   * entry carries a call-site alpha modifier on its border colour (e.g.
   * `border-accent-active` at 40%).
   */
  it('Carries no call-site alpha modifier or border class, for any state', () => {
    render(<StreamHealthBadge stream={streamWith({ state: 'Degraded' })} />);

    const pill = screen.getByText('Degraded');
    const offending = pill.className.split(/\s+/).filter((token) => /\/\d+$|^border/.test(token));
    expect(offending).toEqual([]);
  });

  /**
   * Spec 297 (issue #2635) T008, FR-006. RED: today's lookup
   * (`TONES[stream.state] ?? TONES.unknown`) falls back to the *unknown*
   * tone's classes for a state the client's union does not name, but that
   * tone is still `bg-fg-muted/10` — after the Badge conversion the
   * fallback must be the neutral Badge tone.
   */
  it('Falls back to the neutral tone for an unrecognised state', () => {
    render(<StreamHealthBadge stream={streamWith({ state: 'Unrecognised' as StreamState })} />);

    const pill = screen.getByText('Unrecognised');
    expect(pill.className).toContain('bg-bg-raised');
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
