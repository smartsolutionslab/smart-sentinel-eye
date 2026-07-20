// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary.js';

function Bomb({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) {
    throw new Error('boom');
  }
  return <p>healthy child</p>;
}

const fallback = (error: unknown, reset: () => void) => (
  <div role="alert">
    <p>caught: {error instanceof Error ? error.message : 'unknown'}</p>
    <button type="button" onClick={reset}>
      reset
    </button>
  </div>
);

describe('ErrorBoundary', () => {
  beforeEach(() => {
    // React reports caught boundary errors via console.error; keep output clean.
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('Renders its children while they are healthy', () => {
    render(
      <ErrorBoundary fallback={fallback}>
        <Bomb shouldThrow={false} />
      </ErrorBoundary>,
    );

    expect(screen.getByText('healthy child')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('Catches a throwing child and renders the fallback with the error', () => {
    render(
      <ErrorBoundary fallback={fallback}>
        <Bomb shouldThrow />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText('caught: boom')).toBeTruthy();
    expect(screen.queryByText('healthy child')).toBeNull();
  });

  it('reset re-renders the children once they stop throwing', () => {
    const { rerender } = render(
      <ErrorBoundary fallback={fallback}>
        <Bomb shouldThrow />
      </ErrorBoundary>,
    );
    rerender(
      <ErrorBoundary fallback={fallback}>
        <Bomb shouldThrow={false} />
      </ErrorBoundary>,
    );
    // The boundary holds its error state until reset is invoked.
    expect(screen.getByRole('alert')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'reset' }));

    expect(screen.getByText('healthy child')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('Calls onError exactly once per caught error', () => {
    const onError = vi.fn();
    render(
      <ErrorBoundary fallback={fallback} onError={onError}>
        <Bomb shouldThrow />
      </ErrorBoundary>,
    );

    expect(onError).toHaveBeenCalledTimes(1);
    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'boom' }));
  });
});
