import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

// Set per test so the error banner can be exercised; the mutation hook is a
// module-level mock and cannot take arguments.
let createError: unknown = undefined;

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: createError, reset: vi.fn() }],
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
  beforeEach(() => {
    createDraftMock.mockClear();
    createError = undefined;
  });

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

/**
 * Spec 012 T050. This dialog only creates, so its 409 is OVERLAY_NAME_TAKEN —
 * never the stale-version conflict LayoutEditorDialog handles. Keying the copy
 * on the status alone would hand the operator "reload to see their version",
 * which is useless advice for a name collision.
 */
describe('Conflict copy (spec 012 T050)', () => {
  beforeEach(() => {
    createDraftMock.mockClear();
    createError = undefined;
  });

  it('Names the collision instead of telling the operator to try again', async () => {
    createError = { status: 409, data: { title: 'OVERLAY_NAME_TAKEN' } };
    renderDialog();

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('already taken');
    expect(alert.textContent).not.toContain('Try again');
  });

  it("Prefers the server's own detail when it carries one", async () => {
    createError = {
      status: 409,
      data: { title: 'OVERLAY_NAME_TAKEN', detail: "An overlay named 'Line-1 Title' already exists." },
    };
    renderDialog();

    expect((await screen.findByRole('alert')).textContent).toContain("named 'Line-1 Title'");
  });

  it('Keeps retry wording for a failure that is not a name collision', async () => {
    createError = { status: 500, data: {} };
    renderDialog();

    expect((await screen.findByRole('alert')).textContent).toContain('Try again');
  });
});
