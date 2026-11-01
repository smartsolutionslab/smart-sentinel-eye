import { listOverlaysMock, archiveMock, chain, response, renderPage } from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/** Spec 036 — the confirmations. Same shape as LayoutsPage, same prohibitions. */
describe('OverlaysPage — archive confirmation', () => {
  function publishedChain() {
    return chain({
      revisions: [
        {
          revisionIdentifier: '33333333-3333-3333-3333-333333333333',
          revisionNumber: 2,
          state: 'Published',
          elements: [
            {
              kind: 'Text',
              color: '#9F9F9FD9',
              text: 'Production Line 1',
              normalizedX: 0.1,
              normalizedY: 0.1,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 32,
            },
          ],
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: '2026-05-28T10:00:00Z',
          archivedAt: null,
        },
      ],
    });
  }

  function showing(chains: ReturnType<typeof chain>[]) {
    listOverlaysMock.mockReturnValue({
      data: response(chains),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  }

  async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: /^archive$/i }));
    return screen.getByRole('alertdialog');
  }

  /** T012 / FR-002 — asserted as a call count, not as the dialog closing. */
  it('Archives nothing when the confirmation is dismissed', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);
    await user.click(within(confirmation).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  it('Archives once confirmed, sending the revision it named', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);
    expect(archiveMock).not.toHaveBeenCalled();

    await user.click(within(confirmation).getByRole('button', { name: /^archive$/i }));

    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 2,
      version: 0,
    });
  });

  /** T013 / FR-003 — the name and revision, never the identifier. */
  it('Names the overlay and revision, and shows no identifier', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent('Line-1 Title');
    expect(confirmation).toHaveTextContent('2');
    expect(confirmation).not.toHaveTextContent('11111111-1111-1111-1111-111111111111');
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });

  /**
   * Spec 037 T025 / FR-011, FR-012 — **rewritten**, not deleted.
   *
   * This was spec 036's T014, asserting the overlay could never be edited or
   * published again. ADR-0121 made that false, so the claim changed and the test
   * changed with it. Deleting it instead would have removed the only check on
   * this wording at the exact moment the wording changed.
   *
   * It asserts the **absence** of both false sentences as well as the presence
   * of the true one. A test that merely stopped asserting the old sentence would
   * pass against a page that still said it.
   */
  it('Says the overlay can be brought back and keeps its label', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent(/bring it back/i);
    expect(confirmation).toHaveTextContent(/label is kept/i);
    expect(confirmation).toHaveTextContent(/out of service/i);
    expect(confirmation).not.toHaveTextContent(/never be edited or published again/i);
    expect(confirmation).not.toHaveTextContent(/cannot be undone/i);
  });

  /** T015 / FR-008 — both directions, because published-only passes against a confirmation that always warns. */
  it('Always warns about kiosks, because archive only ever targets the live revision', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    expect(await openConfirmation(user)).toHaveTextContent(/kiosks/i);
  });
});

/**
 * Spec 038 T021 / T024 — the two destructive actions on **one** chain. The twin
 * of LayoutsPage's, and the shape where the old row was wrong: **Archive**
 * archived the draft while its confirmation said the overlay was going out of
 * service.
 */
describe('OverlaysPage — archive and discard on one chain', () => {
  function rev(revisionNumber: number, state: 'Draft' | 'Published') {
    return {
      revisionIdentifier: `r${revisionNumber}`,
      revisionNumber,
      state,
      elements: [
        {
          kind: 'Text' as const,
          color: '#9F9F9FD9',
          text: 'Production Line 1',
          normalizedX: 0.1,
          normalizedY: 0.1,
          normalizedWidth: 0.3,
          normalizedHeight: 0.08,
          fontSizePx: 32,
        },
      ],
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: state === 'Published' ? '2026-05-28T10:00:00Z' : null,
      archivedAt: null,
    };
  }

  beforeEach(() => {
    archiveMock.mockClear();
    listOverlaysMock.mockReturnValue({
      data: response([chain({ revisions: [rev(4, 'Published'), rev(5, 'Draft')] })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  async function confirm(user: ReturnType<typeof userEvent.setup>, label: RegExp, button: RegExp) {
    await user.click(screen.getByRole('button', { name: label }));
    const dialog = screen.getByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: button }));
  }

  /**
   * **The targets, on the same chain.** Both calls succeed whichever revision
   * they name, so asserting that the request fired asserts nothing. Asserted on
   * separate chains, a swap would pass twice.
   */
  it('Archives the LIVE revision and discards the DRAFT, not the other way round', async () => {
    const user = userEvent.setup();
    renderPage();

    await confirm(user, /^archive$/i, /^archive$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 4,
      version: 0,
    });

    archiveMock.mockClear();

    await confirm(user, /discard draft/i, /^discard$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 5,
      version: 0,
    });
  });

  /**
   * The forbidden claims are that the overlay goes **out of service**, that
   * kiosks **stop showing** it, and that it can be **brought back**. Not the
   * word "kiosk": saying kiosks are *unaffected* is true, and it is the most
   * reassuring thing this dialog can say.
   */
  it('Claims no consequence that does not apply when discarding a draft', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
    const dialog = screen.getByRole('alertdialog');

    expect(dialog).toHaveTextContent(/cannot be recovered/i);
    expect(dialog).toHaveTextContent(/stays exactly as it is/i);
    expect(dialog).toHaveTextContent(/kiosks are unaffected/i);
    expect(dialog).not.toHaveTextContent(/out of service/i);
    expect(dialog).not.toHaveTextContent(/stop showing/i);
    expect(dialog).not.toHaveTextContent(/bring it back/i);
  });

  it('Names a different revision in each confirmation', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /^archive$/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('revision 4');
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('draft revision 5');
  });

  it('Discards nothing when the discard confirmation is dismissed', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  /**
   * Spec 038 FR-009 / T023. Asserts the revision **number**, not the word
   * `Published` — that appears either way.
   */
  it('Names the live revision in the badge, and says a draft is open', () => {
    renderPage();

    expect(screen.getByText('v4 · Published · draft v5')).toBeInTheDocument();
  });
});
