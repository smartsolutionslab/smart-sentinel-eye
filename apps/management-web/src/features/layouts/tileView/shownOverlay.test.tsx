import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render } from '@testing-library/react';
import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';

const asked: string[] = [];

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useResolveOverlayTextQuery: ({ text }: { text: string }) => {
      asked.push(text);
      return { data: { resolvedText: `<${text}>`, placeholders: [] }, isError: false };
    },
  };
});

const { useResolvedElements } = await import('./shownOverlay');

function label(text: string): OverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFFF',
    text,
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.5,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function Probe({ raw }: { raw: readonly OverlayElement[] }) {
  const { resolvers } = useResolvedElements(raw);
  return <>{resolvers}</>;
}

describe('useResolvedElements — resolving placeholders while the text is being typed', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    asked.length = 0;
  });
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it('asks about the text the operator stopped on, not about every keystroke on the way', () => {
    const { rerender } = render(<Probe raw={[label('{{a}}')]} />);

    rerender(<Probe raw={[label('{{ab}}')]} />);
    rerender(<Probe raw={[label('{{abc}}')]} />);
    act(() => {
      vi.advanceTimersByTime(2_000);
    });

    expect(asked).toContain('{{abc}}');
    expect(asked).not.toContain('{{ab}}');
  });
});
