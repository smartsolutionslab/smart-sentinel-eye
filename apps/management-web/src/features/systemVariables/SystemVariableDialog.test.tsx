import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const defineMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useDefineVariableMutation: () => [defineMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

const { SystemVariableDialog } = await import('./SystemVariableDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <SystemVariableDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

describe('SystemVariableDialog', () => {
  beforeEach(() => defineMock.mockClear());

  it('Renders the name input and the type selector', () => {
    renderDialog();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /type/i })).toBeInTheDocument();
  });

  it('Submits a String variable with no initial value', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledWith({
      name: 'lineStatus',
      type: 'String',
      initialValue: '',
    });
  });

  it('Surfaces a validation error when the name does not match the grammar', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), '1bad');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(
      await screen.findByText(/must start with a letter/i),
    ).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Reveals the truthy/falsy label inputs when Type is Boolean', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    expect(screen.getByLabelText(/truthy label/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/falsy label/i)).toBeInTheDocument();
  });
});
