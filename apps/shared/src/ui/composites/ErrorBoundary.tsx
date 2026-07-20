import { Component, type ReactNode } from 'react';

export interface ErrorBoundaryProps {
  /** Fallback receives the error and a reset callback. */
  fallback: (error: unknown, reset: () => void) => ReactNode;
  /** Called once per caught error (spec 011 FR-017 logging hook). */
  onError?: (error: unknown) => void;
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: unknown;
}

/**
 * Crash containment seam (spec 011 FR-016, contracts §7). A class component
 * because error boundaries are the one React feature without a hook
 * equivalent. The render-prop fallback lets each app own its recovery
 * posture: management renders a bounded panel + reset, the kiosk schedules
 * a watchdog reload.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { hasError: false, error: undefined };

  static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
    return { hasError: true, error };
  }

  override componentDidCatch(error: unknown): void {
    this.props.onError?.(error);
  }

  private readonly reset = (): void => {
    this.setState({ hasError: false, error: undefined });
  };

  override render(): ReactNode {
    if (this.state.hasError) {
      return this.props.fallback(this.state.error, this.reset);
    }
    return this.props.children;
  }
}
