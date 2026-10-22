import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { WallCredentialNotice } from './WallCredentialNotice.js';

/**
 * Spec 439 (issue #2781) T005 — RED. The two wall-level states, in the owner's
 * exact words, on a calm dark surface (ADR-0146): no motion, no bright fill.
 */

const RENEWING = "This display's access was refused. Reconnecting with a new credential.";
const REFUSED = 'Access refused. Ask an administrator to re-enrol this display.';

describe('WallCredentialNotice', () => {
  it('Says the display was refused and is reconnecting while renewing, as a status', () => {
    render(<WallCredentialNotice state="renewing" />);

    const notice = screen.getByTestId('wall-credential-state');
    expect(notice).toHaveTextContent(RENEWING);
    expect(notice).toHaveAttribute('role', 'status');
  });

  it('Tells someone to ask an administrator to re-enrol once renewal has failed, as an alert', () => {
    render(<WallCredentialNotice state="refused" />);

    const notice = screen.getByTestId('wall-credential-state');
    expect(notice).toHaveTextContent(REFUSED);
    expect(notice).toHaveAttribute('role', 'alert');
  });

  it.each(['renewing', 'refused'] as const)(
    'Carries no animation, transition or bright fill in the %s state',
    (state) => {
      const { container } = render(<WallCredentialNotice state={state} />);

      const classes = Array.from(container.querySelectorAll('*'))
        .flatMap((element) => Array.from(element.classList))
        .join(' ');
      expect(classes).not.toBe('');
      expect(classes).not.toMatch(/(^|\s)(animate-|transition)/);
      expect(classes).not.toMatch(/(^|\s)bg-(white|status-|accent)/);
    },
  );
});
