import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined }],
  };
});

const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('OverlayEditorDialog', () => {
  beforeEach(() => createDraftMock.mockClear());

  it('Renders the name input and the embedded WYSIWYG editor controls', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-text')).toBeInTheDocument();
    expect(screen.getByTestId('overlay-editor-font-size')).toBeInTheDocument();
  });

  it('Submits the form with the default Label and the typed name', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1 Title');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const payload = (createDraftMock.mock.calls[0] as unknown as ReadonlyArray<{
      name: string;
      label: { text: string; fontSizePx: number };
    }>)[0]!;
    expect(payload.name).toBe('Line-1 Title');
    expect(payload.label.text).toBe('Overlay text');
    expect(payload.label.fontSizePx).toBe(32);
  });

  it('Surfaces a validation error when the name is blank', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.click(screen.getByRole('button', { name: /save as draft/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createDraftMock).not.toHaveBeenCalled();
  });
});
