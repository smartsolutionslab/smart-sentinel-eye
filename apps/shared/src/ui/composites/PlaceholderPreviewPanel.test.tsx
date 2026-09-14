// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { PlaceholderPreviewPanel } from './PlaceholderPreviewPanel.js';

/**
 * Spec 148 T013. Covers US1 scenarios 12, 13 (partially — the "Save as
 * draft stays enabled" half is asserted where the form lives, not here) and
 * 15.
 *
 * <p><b>Phase 4a — RED for the advisory rows.</b> This panel calls the
 * phase-4a `placeholderAdvisories` scaffold, which always returns `[]`, so
 * every assertion below that expects a row is expected to fail until T012
 * implements it. The resolved-text and status-line assertions are real
 * rendering, not decision logic, and may pass on arrival.</p>
 */
describe('PlaceholderPreviewPanel', () => {
  afterEach(() => {
    cleanup();
  });

  it('Shows the resolved text once the response has arrived (US1 scenario 12)', () => {
    render(
      <PlaceholderPreviewPanel
        text="Line 1: {{temperature}}"
        data={{
          resolvedText: 'Line 1: 23.4',
          placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
        }}
        isFetching={false}
        isError={false}
      />,
    );

    expect(screen.getByTestId('placeholder-preview-text').textContent).toBe('Line 1: 23.4');
  });

  it('Names the value and fab in a row for a resolved reference (US1 scenario 12)', () => {
    render(
      <PlaceholderPreviewPanel
        text="{{temperature}}"
        data={{
          resolvedText: '23.4',
          placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
        }}
        isFetching={false}
        isError={false}
      />,
    );

    expect(screen.getByTestId('placeholder-advisory-resolved').textContent).toBe('temperature — 23.4 (munich)');
  });

  it('Shows the Unknown row for an unresolved reference (US1 scenario 13)', () => {
    render(
      <PlaceholderPreviewPanel
        text="{{temperatuer}}"
        data={{
          resolvedText: '{{temperatuer}}',
          placeholders: [{ name: 'temperatuer', outcome: 'Unknown', fab: null, renderedValue: null }],
        }}
        isFetching={false}
        isError={false}
      />,
    );

    expect(screen.getByTestId('placeholder-advisory-unknown')).not.toBeNull();
  });

  it('Falls back to the raw text before a response arrives', () => {
    render(<PlaceholderPreviewPanel text="{{temperature}}" data={undefined} isFetching isError={false} />);

    expect(screen.getByTestId('placeholder-preview-text').textContent).toBe('{{temperature}}');
  });

  /**
   * US1 scenario 15 — a failed resolve request is a non-blocking notice.
   * This component has no submit control of its own; the "stays enabled"
   * half of scenario 15 is asserted at `OverlayEditorDialog` /
   * `OverlayEditor` level, where the button actually lives.
   */
  it('Shows a non-blocking notice when the resolve request fails, and no role="alert"', () => {
    render(<PlaceholderPreviewPanel text="{{temperature}}" data={undefined} isFetching={false} isError />);

    expect(screen.getByTestId('placeholder-preview-error')).not.toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
