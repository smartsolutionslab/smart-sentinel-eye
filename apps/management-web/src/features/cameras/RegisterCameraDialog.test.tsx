import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const registerMock = vi.fn(async (_input: Record<string, unknown>) => ({ data: 'ok' }));

// Mutable so a test can put the operator in one fab or several; the dialog
// only asks when there is something to ask about (ADR-0114).
const assignedGroups = { current: ['/fabs/munich'] as string[] };

// Mutable so a test can put a register in flight (ADR-0151 focus-loss guard).
const mutationState = { current: { isLoading: false, error: undefined as unknown, reset: vi.fn() } };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useRegisterCameraMutation: () => [registerMock, mutationState.current],
  };
});

const { RegisterCameraDialog } = await import('./RegisterCameraDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <RegisterCameraDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

async function fillValidCamera(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/name/i), 'Line-1-North');
  await user.type(screen.getByLabelText(/rtsp/i), 'rtsp://10.0.5.12/h264');
}

// Drives the *same* mounted RegisterCameraDialog instance's `open` prop false
// then true, inside the Provider renderDialog() already rendered (mirrors
// SystemVariableDialog.test.tsx's toggleOpen). CamerasPage.tsx:192 keeps the
// dialog mounted while closed, so this is what a real Cancel/Esc/overlay-click
// close does to the component instance; unmounting and remounting would reset
// every useState/useForm value for free and pass on unfixed code, proving
// nothing about issue #2579.
function toggleOpen(rerender: ReturnType<typeof render>['rerender'], open: boolean) {
  rerender(
    <Provider store={store}>
      <RegisterCameraDialog open={open} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('RegisterCameraDialog', () => {
  beforeEach(() => {
    registerMock.mockClear();
    assignedGroups.current = ['/fabs/munich'];
    mutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
  });

  it('Never asks a single-fab operator which fab', async () => {
    // The whole point of ADR-0114: the fab is inferred and the operator is not
    // made to state something the server already knows.
    renderDialog();

    expect(screen.queryByLabelText(/^fab$/i)).not.toBeInTheDocument();
  });

  it('Registers without a fabId for a single-fab operator', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidCamera(user);
    await user.click(screen.getByRole('button', { name: /register/i }));

    const sent = registerMock.mock.calls[0]?.[0];
    expect(sent).toBeDefined();
    expect(sent).not.toHaveProperty('fabId');
  });

  it('Asks a multi-fab operator which fab', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    renderDialog();

    expect(screen.getByLabelText(/^fab$/i)).toBeInTheDocument();
  });

  it('Refuses to submit for a multi-fab operator with no fab chosen', async () => {
    // Caught in the dialog rather than sent: the server answers this with
    // 400 CAMERA_FAB_REQUIRED, which is the right answer to the wrong question
    // when the operator can simply be asked.
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidCamera(user);
    await user.click(screen.getByRole('button', { name: /register/i }));

    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();
    expect(registerMock).not.toHaveBeenCalled();
  });

  it('Sends the chosen fab for a multi-fab operator', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidCamera(user);
    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');
    await user.click(screen.getByRole('button', { name: /register/i }));

    // dresden, not munich: munich is first in the list and the default
    // everywhere else, so a dialog that ignored the selection would pass
    // against it.
    expect(registerMock).toHaveBeenCalledWith(expect.objectContaining({ fabId: 'dresden' }));
  });

  /**
   * Spec 231 (#2433) US2 — new behaviour, RED. The Fab `<select>`'s `onChange`
   * sets the value but never clears `fabError`, so the "Choose which fab…"
   * message survives picking a fab and only disappears on the next submit.
   */
  it('Clears the missing-fab message as soon as a fab is chosen', async () => {
    assignedGroups.current = ['/fabs/munich', '/fabs/dresden'];
    const user = userEvent.setup();
    renderDialog();

    await fillValidCamera(user);
    await user.click(screen.getByRole('button', { name: /register/i }));
    expect(await screen.findByText(/choose which fab/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(/^fab$/i), 'dresden');

    expect(screen.queryByText(/choose which fab/i)).not.toBeInTheDocument();
    expect(registerMock).not.toHaveBeenCalled();
  });

  /**
   * Issue #2579 (spec 256) US2 — new behaviour, RED. Cancel calls the
   * parent's onOpenChange(false) directly, bypassing the Dialog's own
   * onOpenChange wrapper (`:81-86`), which is the only place today's code
   * resets the form. The close effect (`:33-46`) resets the mutation state
   * and fabId/fabError but not the form, so typed Name and RTSP URL survive
   * a close/reopen that never touches the wrapper.
   */
  it('Drops the typed name and RTSP URL when the dialog is closed and reopened without unmounting', async () => {
    const user = userEvent.setup();
    const { rerender } = renderDialog();

    await fillValidCamera(user);

    toggleOpen(rerender, false);
    toggleOpen(rerender, true);

    expect(screen.getByLabelText(/name/i)).toHaveValue('');
    expect(screen.getByLabelText(/rtsp/i)).toHaveValue('');
  });

  // ---- Issue #2624 / ADR-0151: focus must survive an in-flight submit ----

  it('Announces Register as unavailable with aria-disabled, not native disabled, while in flight', () => {
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };

    renderDialog();

    const register = screen.getByRole('button', { name: /^(register|registering…)$/i });
    expect(register).toHaveAttribute('aria-disabled', 'true');
    expect(register).not.toHaveAttribute('disabled');
  });

  it('Refuses a form-level submit while a register is in flight', async () => {
    const user = userEvent.setup();
    mutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderDialog();

    await fillValidCamera(user);
    // Dialog renders through a Radix Portal into document.body, outside
    // render()'s own container, so the form is looked up from the document.
    // The submit event is dispatched at the form itself, bypassing whatever
    // the submit button's own disabled/aria-disabled state is — this is the
    // form-level guard, not a click or an implicit-submission proof (that is
    // e2e/in-flight-focus.spec.ts). One macrotask flush lets react-hook-form's
    // (async) zodResolver validation and the mocked mutation call settle
    // before asserting, since both resolve on the microtask queue with no
    // real timer involved.
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(registerMock).not.toHaveBeenCalled();
  });

  it('Submits a form-level submit once when nothing is in flight', async () => {
    const user = userEvent.setup();
    renderDialog();

    await fillValidCamera(user);
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(registerMock).toHaveBeenCalledTimes(1);
  });
});
