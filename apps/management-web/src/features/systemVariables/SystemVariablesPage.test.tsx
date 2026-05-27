import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { Variable } from '@smart-sentinel-eye/shared/api/systemVariables.api';

const listMock = vi.fn();
const setValueMock = vi.fn(async () => ({ data: 'noop' }));
const defineMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useListVariablesQuery: (...args: unknown[]) => listMock(...args),
    useSetVariableValueMutation: () => [setValueMock, { isLoading: false }],
    useDefineVariableMutation: () => [defineMock, { isLoading: false, error: undefined }],
  };
});

const { SystemVariablesPage } = await import('./SystemVariablesPage.js');

function variable(overrides: Partial<Variable> = {}): Variable {
  return {
    variableIdentifier: '11111111-1111-1111-1111-111111111111',
    name: 'oeeLine1',
    type: 'Number',
    state: 'Defined',
    value: null,
    truthyLabel: null,
    falsyLabel: null,
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    ...overrides,
  };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <SystemVariablesPage />
    </Provider>,
  );
}

describe('SystemVariablesPage', () => {
  beforeEach(() => {
    listMock.mockReset();
    setValueMock.mockClear();
  });

  it('Shows an empty-state message when there are no variables', () => {
    listMock.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/no system variables to show/i)).toBeInTheDocument();
  });

  it('Renders one row per variable with type, state and current value', () => {
    listMock.mockReturnValue({
      data: [variable({ value: '82.4' })],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByRole('heading', { name: 'oeeLine1' })).toBeInTheDocument();
    expect(screen.getByText(/82\.4/)).toBeInTheDocument();
  });

  it('Renders the unset placeholder when value is null', () => {
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/\(unset\)/)).toBeInTheDocument();
  });

  it('Submitting a new value calls setVariableValue with the typed string', async () => {
    const user = userEvent.setup();
    listMock.mockReturnValue({
      data: [variable()],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    const input = screen.getByPlaceholderText(/new value/i);
    await user.type(input, '99.5');
    await user.click(screen.getByRole('button', { name: /set value/i }));

    expect(setValueMock).toHaveBeenCalledWith({ name: 'oeeLine1', value: '99.5' });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });
});
