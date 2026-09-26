import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

const dryRunMock = vi.fn(async (_input: Record<string, unknown>) => ({ data: undefined }));

// Mutable so a test can put a dry run in flight (ADR-0151 focus-loss guard).
const mutationState = {
  current: { data: undefined as unknown, error: undefined as unknown, isLoading: false, reset: vi.fn() },
};

vi.mock('@smart-sentinel-eye/shared/api/rules.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/rules.api')>();
  return {
    ...actual,
    useDryRunRuleMutation: () => [dryRunMock, mutationState.current],
  };
});

const { DryRunPanel } = await import('./DryRunPanel.js');

function renderPanel() {
  return render(<DryRunPanel ruleName="high-oee-on-fast-cycle" fabId="munich" />);
}

describe('DryRunPanel', () => {
  beforeEach(() => {
    dryRunMock.mockClear();
    mutationState.current = { data: undefined, error: undefined, isLoading: false, reset: vi.fn() };
  });

  // ---- Issue #2624 / ADR-0151: focus must survive an in-flight dry run ----

  it('Announces Run as unavailable with aria-disabled, not native disabled, while in flight', () => {
    mutationState.current = { data: undefined, error: undefined, isLoading: true, reset: vi.fn() };

    renderPanel();

    const run = screen.getByRole('button', { name: /^(run|running…)$/i });
    expect(run).toHaveAttribute('aria-disabled', 'true');
    expect(run).not.toHaveAttribute('disabled');
  });

  it('Refuses a click on Run while a dry run is in flight', () => {
    mutationState.current = { data: undefined, error: undefined, isLoading: true, reset: vi.fn() };

    renderPanel();
    fireEvent.click(screen.getByRole('button', { name: /^(run|running…)$/i }));

    expect(dryRunMock).not.toHaveBeenCalled();
  });

  it('Runs once on a click when nothing is in flight, with a valid sample', () => {
    renderPanel();
    fireEvent.click(screen.getByRole('button', { name: /^run$/i }));

    expect(dryRunMock).toHaveBeenCalledTimes(1);
  });
});
