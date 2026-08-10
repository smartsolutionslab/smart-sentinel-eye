import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const registerMock = vi.fn(async (_input: Record<string, unknown>) => ({ data: 'ok' }));

// Mutable so a test can put the operator in one fab or several; the dialog
// only asks when there is something to ask about (ADR-0114).
const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useRegisterCameraMutation: () => [
      registerMock,
      { isLoading: false, error: undefined, reset: vi.fn() },
    ],
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

describe('RegisterCameraDialog', () => {
  beforeEach(() => {
    registerMock.mockClear();
    assignedGroups.current = ['/fabs/munich'];
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
});
