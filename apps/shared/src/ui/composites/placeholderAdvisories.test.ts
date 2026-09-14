import { describe, expect, it } from 'vitest';
import { placeholderAdvisories } from './placeholderAdvisories.js';
import type { ResolvedTextPreview } from '@smart-sentinel-eye/shared/api/systemVariables.api';

/**
 * Spec 148 T012 + T017 — the pure mapping from a resolve response (plus the
 * raw authored text) onto the rows `PlaceholderPreviewPanel` renders. No DOM,
 * no store, no fetch: `placeholderAdvisories` is a pure function precisely so
 * this file can pin its wording and its US4 heuristic without mounting
 * anything.
 *
 * <p><b>Phase 4a — RED.</b> `placeholderAdvisories` is a phase-4a scaffold
 * that always returns `[]`. Every fact below is expected to fail on a
 * missing row, not a missing export.</p>
 */
describe('placeholderAdvisories (spec 148 US1 — server-reported outcomes)', () => {
  it('Names the value and the fab for a Resolved reference', () => {
    const response: ResolvedTextPreview = {
      resolvedText: 'Line 1: 23.4',
      placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
    };

    const rows = placeholderAdvisories('Line 1: {{temperature}}', response);

    expect(rows).toEqual([{ kind: 'resolved', reference: 'temperature', message: 'temperature — 23.4 (munich)' }]);
  });

  /**
   * Spec 148 decision 3 (ADR-0115): the honest phrasing is scoped to what
   * the editor can actually know — a name absent from the *author's* fabs
   * may be present in the *viewer's*. The wording is fixed by CLAUDE.md and
   * the spec, so this test asserts the exact string rather than a substring.
   */
  it("Says no variable of this name is defined in the caller's fab(s) for an Unknown reference — never that it does not exist", () => {
    const response: ResolvedTextPreview = {
      resolvedText: '{{temperatuer}}',
      placeholders: [{ name: 'temperatuer', outcome: 'Unknown', fab: null, renderedValue: null }],
    };

    const rows = placeholderAdvisories('{{temperatuer}}', response);

    expect(rows).toEqual([
      {
        kind: 'unknown',
        reference: 'temperatuer',
        message:
          'temperatuer — no variable named temperatuer is defined in your fab(s). It will render as {{temperatuer}}.',
      },
    ]);
    expect(rows[0]?.message).not.toContain('does not exist');
  });

  it('Reports an Unset reference distinctly from Unknown', () => {
    const response: ResolvedTextPreview = {
      resolvedText: '{{shift}}',
      placeholders: [{ name: 'shift', outcome: 'Unset', fab: 'munich', renderedValue: null }],
    };

    const rows = placeholderAdvisories('{{shift}}', response);

    expect(rows).toEqual([
      {
        kind: 'unset',
        reference: 'shift',
        message: 'shift is defined in munich but has no value set yet. It will render as {{shift}}.',
      },
    ]);
  });

  it('Reports an Archived reference distinctly from Unknown and Unset', () => {
    const response: ResolvedTextPreview = {
      resolvedText: '{{oldOne}}',
      placeholders: [{ name: 'oldOne', outcome: 'Archived', fab: 'munich', renderedValue: null }],
    };

    const rows = placeholderAdvisories('{{oldOne}}', response);

    expect(rows).toEqual([
      {
        kind: 'archived',
        reference: 'oldOne',
        message: 'oldOne was archived in munich. It will render as {{oldOne}}.',
      },
    ]);
  });

  it('Resolves every reference in a label mixing all four outcomes, in order', () => {
    const response: ResolvedTextPreview = {
      resolvedText: 'L1 23.4 {{shift}} {{oldOne}} {{temperatuer}}',
      placeholders: [
        { name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' },
        { name: 'shift', outcome: 'Unset', fab: 'munich', renderedValue: null },
        { name: 'oldOne', outcome: 'Archived', fab: 'munich', renderedValue: null },
        { name: 'temperatuer', outcome: 'Unknown', fab: null, renderedValue: null },
      ],
    };

    const rows = placeholderAdvisories('L1 {{temperature}} {{shift}} {{oldOne}} {{temperatuer}}', response);

    expect(rows.map((r) => r.kind)).toEqual(['resolved', 'unset', 'archived', 'unknown']);
  });

  it('Returns no rows for text with no placeholders', () => {
    const response: ResolvedTextPreview = { resolvedText: 'PRODUCTION LINE 1', placeholders: [] };

    expect(placeholderAdvisories('PRODUCTION LINE 1', response)).toEqual([]);
  });

  it('Returns no rows while the response has not arrived yet', () => {
    expect(placeholderAdvisories('{{temperature}}', undefined)).toEqual([]);
  });
});

/**
 * Spec 148 US4 — text that is brace-shaped but does not match the server's
 * grammar. Detected by a loose client-side heuristic
 * (`\{\{[^{}]*\}\}`) flagging any match whose inner text is not among the
 * names the server actually returned — the strict grammar stays
 * server-side only (spec 148 plan.md "How it is detected without a second
 * copy of the grammar").
 */
describe('placeholderAdvisories (spec 148 US4 — brace-shaped but not a placeholder)', () => {
  it('Flags internal whitespace as not a placeholder', () => {
    const response: ResolvedTextPreview = { resolvedText: '{{ temperature }}', placeholders: [] };

    const rows = placeholderAdvisories('{{ temperature }}', response);

    expect(rows).toEqual([
      {
        kind: 'malformed',
        reference: '{{ temperature }}',
        message: '{{ temperature }} is not a placeholder and will render literally.',
      },
    ]);
  });

  it('Flags a name starting with a digit as not a placeholder', () => {
    const response: ResolvedTextPreview = { resolvedText: '{{1temp}}', placeholders: [] };

    expect(placeholderAdvisories('{{1temp}}', response)[0]?.kind).toBe('malformed');
  });

  it('Flags an illegal character as not a placeholder', () => {
    const response: ResolvedTextPreview = { resolvedText: '{{camera-1}}', placeholders: [] };

    expect(placeholderAdvisories('{{camera-1}}', response)[0]?.kind).toBe('malformed');
  });

  it('Never flags a well-formed, defined name as malformed', () => {
    const response: ResolvedTextPreview = {
      resolvedText: '23.4',
      placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
    };

    const rows = placeholderAdvisories('{{temperature}}', response);

    expect(rows.some((r) => r.kind === 'malformed')).toBe(false);
  });

  it('Reports a well-formed but unknown name as Unknown, never as malformed', () => {
    const response: ResolvedTextPreview = {
      resolvedText: '{{temperatuer}}',
      placeholders: [{ name: 'temperatuer', outcome: 'Unknown', fab: null, renderedValue: null }],
    };

    const rows = placeholderAdvisories('{{temperatuer}}', response);

    expect(rows.some((r) => r.kind === 'malformed')).toBe(false);
    expect(rows.some((r) => r.kind === 'unknown')).toBe(true);
  });

  it('Costs at most one extra row for exotic nesting', () => {
    const response: ResolvedTextPreview = { resolvedText: '{{{temp}}}', placeholders: [] };

    const rows = placeholderAdvisories('{{{temp}}}', response);

    expect(rows.length).toBeLessThanOrEqual(1);
  });
});
