import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';

const retireCamera = vi.hoisted(() => vi.fn());
const mutationState = vi.hoisted(() => ({
  current: { isLoading: false, error: undefined as unknown, reset: vi.fn() },
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useRetireCameraMutation: () => [retireCamera, mutationState.current],
  };
});

const { RetireCameraDialog } = await import('./RetireCameraDialog.js');
const { store } = await import('../../app/store.js');

const CAMERA = 'line-3-inlet';

function renderDialog(onOpenChange = vi.fn()) {
  render(
    <Provider store={store}>
      <RetireCameraDialog
        open
        onOpenChange={onOpenChange}
        cameraIdentifier="11111111-1111-1111-1111-111111111111"
        name={CAMERA}
      />
    </Provider>,
  );
  return { onOpenChange };
}

/**
 * Spec 032 T011–T012 — the confirmation for the product's first irreversible
 * action.
 *
 * The four content assertions are deliberately four assertions. One check that
 * "a confirmation appeared" passes while three of the four required sentences
 * are missing, and two of those three describe consequences an operator cannot
 * see from the camera's own page.
 */
describe('RetireCameraDialog', () => {
  beforeEach(() => {
    retireCamera.mockReset();
    retireCamera.mockResolvedValue({ data: undefined });
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
  });

  // FR-003. Named, so an operator with two similar cameras open knows which
  // one they are about to lose. "This camera" would satisfy a dialog-appeared
  // assertion and none of the requirement.
  it('Names the camera being retired', () => {
    renderDialog();

    expect(screen.getByRole('alertdialog')).toHaveTextContent(CAMERA);
  });

  // FR-005. The single most important sentence: there is no un-retire, by
  // spec 028's decision, so a misclick is unrecoverable through any interface.
  it('Says retirement is permanent and cannot be undone', () => {
    renderDialog();

    expect(screen.getByRole('alertdialog')).toHaveTextContent(/permanent/i);
    expect(screen.getByRole('alertdialog')).toHaveTextContent(/cannot be undone/i);
  });

  // FR-006. Spec 028 FR-008 retires the stream and removes the SFU path, so
  // anyone watching loses the feed. Invisible from this page; told before, not
  // after.
  it('Says the live stream will stop', () => {
    renderDialog();

    expect(screen.getByRole('alertdialog')).toHaveTextContent(/live stream will stop/i);
  });

  // FR-007. The payoff spec 028 built and nothing has ever surfaced: the name
  // is reusable within the fab immediately, so the replacement can take it.
  it('Says the name becomes available again', () => {
    renderDialog();

    expect(screen.getByRole('alertdialog')).toHaveTextContent(/available again/i);
  });

  it('Retires the camera when confirmed', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /retire camera/i }));

    expect(retireCamera).toHaveBeenCalledTimes(1);
    expect(retireCamera).toHaveBeenCalledWith({
      cameraIdentifier: '11111111-1111-1111-1111-111111111111',
    });
  });

  /**
   * FR-016 restated at the call site. The page holds a version for the address
   * correction, so passing one here is the natural mistake; the endpoint is
   * idempotent rather than version-checked and declares no 412 or 428.
   */
  it('Sends no version — retirement is not version-checked', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /retire camera/i }));

    expect(retireCamera.mock.calls[0]?.[0]).not.toHaveProperty('version');
  });

  /**
   * FR-008, asserted as a **call count**. A confirmation that closes cleanly
   * and retires anyway passes any assertion about the dialog closing — which
   * is the assertion that gets written when the author is confident.
   */
  it('Retires nothing when cancelled', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /cancel/i }));

    expect(retireCamera).not.toHaveBeenCalled();
  });

  // FR-015. isLoading is the in-flight state; while it is set the action must
  // refuse, or a slow network turns one retirement into two requests.
  it('Refuses a second confirmation while the first is in flight', async () => {
    const user = userEvent.setup();
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderDialog();

    const action = screen.getByRole('button', { name: /retire camera/i });
    await user.click(action);
    await user.click(action);

    expect(retireCamera).not.toHaveBeenCalled();
  });

  /**
   * A refusal is reported where the operator is looking, and the dialog stays
   * open. Closing on failure reports it to an empty screen.
   */
  it('Keeps the confirmation open and shows the refusal when retiring fails', async () => {
    const user = userEvent.setup();
    retireCamera.mockResolvedValue({ error: { status: 500, data: {} } });
    mutationState.current = {
      isLoading: false,
      error: { status: 500, data: { title: 'SERVER_ERROR' } },
      reset: vi.fn(),
    };
    const { onOpenChange } = renderDialog();

    await user.click(screen.getByRole('button', { name: /retire camera/i }));

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  /**
   * FR-012, guarded at the source of the wording. Everything the dialog says
   * is future tense — what confirming *will* do. Nothing claims the camera has
   * been retired, because the endpoint answers 204 whether or not this
   * operator caused it.
   */
  it('Describes what will happen, never what has happened', () => {
    renderDialog();

    const dialog = screen.getByRole('alertdialog');
    expect(dialog).not.toHaveTextContent(/camera retired/i);
    expect(dialog).not.toHaveTextContent(/you retired/i);
    expect(dialog).not.toHaveTextContent(/has been retired/i);
  });
});
