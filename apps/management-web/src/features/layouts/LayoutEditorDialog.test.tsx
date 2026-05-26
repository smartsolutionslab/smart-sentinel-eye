import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: {
        items: [
          {
            cameraIdentifier: '11111111-1111-1111-1111-111111111111',
            name: 'Line-1-Entrance',
            rtspUrl: 'rtsp://10.0.5.12/h264',
            registeredAt: '2026-05-25T10:00:00Z',
          },
        ],
        count: 1,
        offset: 0,
        limit: 50,
      },
      isLoading: false,
    }),
  };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <LayoutEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('LayoutEditorDialog', () => {
  beforeEach(() => createDraftMock.mockClear());

  it('Renders the name input and a populated camera picker', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /camera/i })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Line-1-Entrance' })).toBeInTheDocument();
  });

  it('Submits the form with valid input', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1');
    await user.selectOptions(
      screen.getByRole('combobox', { name: /camera/i }),
      '11111111-1111-1111-1111-111111111111',
    );
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledWith({
      name: 'Line-1',
      cameraIdentifier: '11111111-1111-1111-1111-111111111111',
    });
  });

  it('Surfaces a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});
