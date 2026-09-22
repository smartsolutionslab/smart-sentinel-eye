// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { FormErrorSummary, type FormErrorSummaryProps } from './FormErrorSummary.js';

/**
 * Spec 212 (issue #2430) T010 — US3, "a refusal an operator can read".
 * New behaviour, RED (ADR-0139/ADR-0144): `FormErrorSummary.tsx` does not
 * exist yet, so every case below is expected to fail on import resolution,
 * not on an assertion. T011 adds the component and turns this file green.
 *
 * Contract under test (spec.md S10/S11/S12, plan.md §US3):
 * `{ errors: FieldErrorLike; renderedFields: readonly string[] }` — collect
 * the top-level error entries whose key is *not* in `renderedFields` and
 * surface their messages inside one `role="alert"` region; render nothing
 * when that collection is empty. This local build helper stands in for
 * react-hook-form's own `FieldErrors` shape (which `errors` is assignable
 * from in production) without this package depending on that library.
 */
function buildErrors(entries: Record<string, string>): FormErrorSummaryProps['errors'] {
  const errors: Record<string, { message: string }> = {};
  for (const [field, message] of Object.entries(entries)) {
    errors[field] = { message };
  }
  return errors;
}

describe('FormErrorSummary', () => {
  afterEach(() => {
    cleanup();
  });

  it('Surfaces an error whose field is not currently rendered inside a role="alert" region', () => {
    const errors = buildErrors({
      truthyLabel: 'BooleanLabels can only be set on Boolean variables.',
    });

    render(<FormErrorSummary errors={errors} renderedFields={['name', 'type']} />);

    expect(screen.getByRole('alert').textContent).toContain('BooleanLabels can only be set on Boolean variables.');
  });

  it('Contributes no role="alert" region when every error names a currently-rendered field', () => {
    const errors = buildErrors({
      name: 'must start with a letter',
    });

    render(<FormErrorSummary errors={errors} renderedFields={['name', 'type']} />);

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('Renders nothing when there are no errors at all', () => {
    const { container } = render(<FormErrorSummary errors={{}} renderedFields={['name', 'type']} />);

    expect(container.innerHTML).toBe('');
  });
});
