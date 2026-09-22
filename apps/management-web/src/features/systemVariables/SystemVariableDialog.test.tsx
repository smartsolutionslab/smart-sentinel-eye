import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { DefineVariableInput } from '@smart-sentinel-eye/shared/api/systemVariables.api';

// A single-fab operator is never asked which fab (ADR-0114), so the default
// keeps the existing cases reading as they did; the multi-fab case overrides it.
const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

// Typed so `defineMock.mock.calls[0][0]` narrows to the payload shape instead
// of an empty tuple — a bare `vi.fn(async () => …)` infers a zero-arg
// signature, which is what the new toggle-and-back cases index into.
const defineMock = vi.fn(async (_payload: DefineVariableInput & { fabId?: string }) => ({ data: 'noop' }));

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

    expect(await screen.findByText(/must start with a letter/i)).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Reveals the truthy/falsy label inputs when Type is Boolean', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    expect(screen.getByLabelText(/truthy label/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/falsy label/i)).toBeInTheDocument();
  });

  it('Submits a String variable after the operator has looked at Boolean and changed back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'lineStatus');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledTimes(1);
    expect(defineMock.mock.calls[0]?.[0]).not.toHaveProperty('truthyLabel');
    expect(defineMock.mock.calls[0]?.[0]).not.toHaveProperty('falsyLabel');
    expect(defineMock).toHaveBeenCalledWith(expect.objectContaining({ name: 'lineStatus', type: 'String' }));
  });

  it('Restores the Boolean labels when the operator toggles there, away, and back', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'doorOpen');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(defineMock).toHaveBeenCalledTimes(1);
    expect(defineMock).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'doorOpen', type: 'Boolean', truthyLabel: 'Yes', falsyLabel: 'No' }),
    );
  });

  it('Still refuses a name that breaks the grammar after a type toggle', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), '1bad');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'String');
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByText(/must start with a letter/i)).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });

  it('Still refuses a Boolean variable whose truthy label has been cleared', async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'doorOpen');
    await user.selectOptions(screen.getByRole('combobox', { name: /type/i }), 'Boolean');
    await user.clear(screen.getByLabelText(/truthy label/i));
    await user.click(screen.getByRole('button', { name: /define/i }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(defineMock).not.toHaveBeenCalled();
  });
});
